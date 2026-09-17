import { act, renderHook } from "@testing-library/react";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { HttpError } from "@/lib/startup";

const mocks = vi.hoisted(() => ({ api: vi.fn(), prepare: vi.fn(), start: vi.fn(), leave: vi.fn(), awake: vi.fn(), reset: vi.fn() }));
vi.mock("@/lib/api", () => ({
  api: mocks.api, prepareRuntime: mocks.prepare, resolveEventsHubUrl: async () => "https://demo/events/hub",
  keepRuntimeAwake: mocks.awake, sendPresenceLeave: mocks.leave, resetRuntime: mocks.reset
}));
vi.mock("@microsoft/signalr", () => ({
  HubConnectionState: { Disconnected: "disconnected" },
  HubConnectionBuilder: class {
    withUrl() { return this; }
    withAutomaticReconnect() { return this; }
    build() {
      return { state: "disconnected", on: vi.fn(), onclose: vi.fn(), onreconnecting: vi.fn(), onreconnected: vi.fn(),
        start: mocks.start, stop: async () => undefined, invoke: async () => undefined };
    }
  }
}));
import { useRealtimePixPlatform } from "./use-realtime-pix-platform";

beforeEach(() => {
  vi.useFakeTimers();
  vi.clearAllMocks();
  localStorage.clear();
  mocks.start.mockResolvedValue(undefined);
  mocks.prepare.mockResolvedValue(undefined);
  mocks.awake.mockResolvedValue(undefined);
  mocks.api.mockImplementation(async (path: string, init?: RequestInit) => {
    if (path === "/sessions/anonymous") {
      const { clientId } = JSON.parse(init?.body as string);
      return { clientId, userId: `user-${clientId}`, displayName: "Demo Visitor", sessionToken: "test" };
    }
    if (path.includes("/bootstrap") || path.includes("/heartbeat")) return {};
    return [];
  });
});
afterEach(() => { vi.useRealTimers(); });

it("waits through a 55-second cold start without losing the visitor identity", async () => {
  mocks.prepare.mockImplementation(() => new Promise(resolve => setTimeout(resolve, 55_000)));
  const hook = renderHook(() => useRealtimePixPlatform());
  const clientId = localStorage.getItem("realtime-pix:clientId");
  expect(clientId).toBeTruthy();
  await act(() => vi.advanceTimersByTimeAsync(10_000));
  expect(hook.result.current.error).toBeNull();
  expect(hook.result.current.loading).toBe(true);
  await act(() => vi.advanceTimersByTimeAsync(45_000));
  expect(hook.result.current.session?.clientId).toBe(clientId);
  expect(hook.result.current.loading).toBe(false);
  expect(mocks.start).toHaveBeenCalledTimes(1); // Only the events hub, never a second presence hub.
  hook.unmount();
  expect(mocks.leave).toHaveBeenCalledWith(`user-${clientId}`, expect.stringContaining(`http:${clientId}:`));
});

it("retries wallet preparation without creating another session", async () => {
  const normalApi = mocks.api.getMockImplementation()!;
  let bootstraps = 0;
  mocks.api.mockImplementation((path: string, init?: RequestInit) => {
    if (path.includes("/bootstrap") && bootstraps++ === 0) return Promise.reject(new HttpError("Bank sleeping", 503));
    return normalApi(path, init);
  });
  const hook = renderHook(() => useRealtimePixPlatform());
  await act(() => vi.advanceTimersByTimeAsync(5000));
  expect(hook.result.current.loading).toBe(false);
  expect(mocks.api.mock.calls.filter(([path]) => path === "/sessions/anonymous")).toHaveLength(1);
  expect(bootstraps).toBe(2);
  hook.unmount();
});

it("keeps the session usable and retries an initial live-connection failure", async () => {
  mocks.start.mockRejectedValueOnce(new Error("negotiation failed")).mockResolvedValue(undefined);
  const hook = renderHook(() => useRealtimePixPlatform());
  await act(() => vi.advanceTimersByTimeAsync(0));
  await act(() => vi.advanceTimersByTimeAsync(5000));
  expect(hook.result.current.session).not.toBeNull();
  expect(hook.result.current.error).toBeNull();
  expect(mocks.start).toHaveBeenCalledTimes(2);
  expect(hook.result.current.connectionState).toBe("connected");
  hook.unmount();
});

it("uses a new lease when the AWS allowance resets the startup session", async () => {
  mocks.awake.mockRejectedValueOnce(new HttpError("daily limit", 409)).mockResolvedValue(undefined);
  const hook = renderHook(() => useRealtimePixPlatform());
  await act(() => vi.advanceTimersByTimeAsync(0));
  await act(() => vi.advanceTimersByTimeAsync(0));
  expect(mocks.reset).toHaveBeenCalledTimes(1);
  const joins = mocks.api.mock.calls.filter(([path]) => path === "/sessions/anonymous")
    .map(([, init]) => JSON.parse(init.body));
  expect(joins).toHaveLength(2);
  expect(joins[0].clientId).toBe(joins[1].clientId);
  expect(joins[0].tabId).not.toBe(joins[1].tabId);
  hook.unmount();
});
