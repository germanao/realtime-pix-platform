import { afterEach, describe, expect, it, vi } from "vitest";
import { api } from "./api";

function jsonResponse(body: unknown) {
  return new Response(JSON.stringify(body), {
    status: 200,
    headers: { "content-type": "application/json" }
  });
}

describe("api", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    vi.unstubAllEnvs();
    vi.resetModules();
  });

  it("does not add a JSON content type to a GET request", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse([]));
    vi.stubGlobal("fetch", fetchMock);

    await api("/presence/users");

    const [, request] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(new Headers(request.headers).has("content-type")).toBe(false);
  });

  it("adds a JSON content type when sending a request body", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({}));
    vi.stubGlobal("fetch", fetchMock);

    await api("/sessions/anonymous", {
      method: "POST",
      body: JSON.stringify({ clientId: "test-client" })
    });

    const [, request] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(new Headers(request.headers).get("content-type")).toBe("application/json");
  });
});

describe("runtime selection", () => {
  afterEach(() => { vi.unstubAllGlobals(); vi.unstubAllEnvs(); vi.resetModules(); });

  it("wakes AWS outside office hours and pins every request to that runtime", async () => {
    vi.stubEnv("NEXT_PUBLIC_AWS_RUNTIME_URL", "https://aws.example");
    vi.stubGlobal("fetch", vi.fn().mockImplementation(() => Promise.resolve(jsonResponse({ status: "ready" }))));
    const runtime = await import("./api");
    await runtime.prepareRuntime(new AbortController().signal, vi.fn());
    expect(await runtime.resolveEventsHubUrl()).toBe("https://aws.example/events/hub");
    await runtime.api("/presence/users");
    const urls = vi.mocked(fetch).mock.calls.map(([url]) => url);
    expect(urls).toEqual(["https://aws.example/runtime/wake", "https://aws.example/health/ready", "https://aws.example/presence/users"]);
  });

  it("selects Azure when AWS daily runtime is exhausted", async () => {
    vi.stubEnv("NEXT_PUBLIC_AWS_RUNTIME_URL", "https://aws.example");
    vi.stubGlobal("fetch", vi.fn().mockImplementation((url: string) => Promise.resolve(
      url.endsWith("/runtime/wake") ? new Response("{}", { status: 409 }) : jsonResponse({ status: "ready" })
    )));
    const runtime = await import("./api");
    await runtime.prepareRuntime(new AbortController().signal, vi.fn());
    expect(await runtime.resolveEventsHubUrl()).toBe("http://localhost:5104/events/hub");
    await runtime.api("/presence/users");
    expect(vi.mocked(fetch).mock.lastCall?.[0]).toBe("http://localhost:5100/presence/users");
  });

  it("shares one in-flight startup across callers", async () => {
    vi.stubGlobal("fetch", vi.fn().mockImplementation(() => Promise.resolve(jsonResponse({}))));
    const runtime = await import("./api");
    const controller = new AbortController();
    const first = runtime.prepareRuntime(controller.signal, vi.fn());
    const second = runtime.prepareRuntime(controller.signal, vi.fn());
    expect(first).toBe(second);
    await first;
    expect(fetch).toHaveBeenCalledTimes(3);
  });
});
