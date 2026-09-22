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

  it("uses the public AWS runtime for production builds without injected preview configuration", async () => {
    vi.stubEnv("NODE_ENV", "production");
    vi.stubEnv("NEXT_PUBLIC_AWS_RUNTIME_URL", "");
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse({ status: "ready" })));
    const runtime = await import("./api");
    await runtime.api("/health/ready");
    const urls = vi.mocked(fetch).mock.calls.map(([url]) => url);
    expect(urls).toEqual([
      "https://djb1ah1j5qyrj.cloudfront.net/health/ready"
    ]);
  });

  it("wakes AWS outside office hours and pins every request to that runtime", async () => {
    vi.stubEnv("NEXT_PUBLIC_AWS_RUNTIME_URL", "https://aws.example");
    vi.stubGlobal("fetch", vi.fn().mockImplementation(() => Promise.resolve(jsonResponse({ status: "ready" }))));
    const runtime = await import("./api");
    await runtime.prepareRuntime(new AbortController().signal, vi.fn());
    expect(await runtime.resolveEventsHubUrl()).toBe("https://aws.example/events/hub");
    await runtime.api("/presence/users");
    const urls = vi.mocked(fetch).mock.calls.map(([url]) => url);
    expect(urls).toEqual(["/api/runtime/wake", "https://aws.example/health/ready", "https://aws.example/presence/users"]);
  });

  it("reports the daily limit without contacting a fallback runtime", async () => {
    vi.stubEnv("NEXT_PUBLIC_AWS_RUNTIME_URL", "https://aws.example");
    vi.stubGlobal("fetch", vi.fn().mockImplementation((url: string) => Promise.resolve(
      url.endsWith("/runtime/wake") ? new Response("{}", { status: 409 }) : jsonResponse({ status: "ready" })
    )));
    const runtime = await import("./api");
    await expect(runtime.prepareRuntime(new AbortController().signal, vi.fn())).rejects.toThrow("24-hour demo allowance");
    expect(fetch).toHaveBeenCalledTimes(1);
  });

  it("shares one in-flight startup across callers", async () => {
    vi.stubGlobal("fetch", vi.fn().mockImplementation(() => Promise.resolve(jsonResponse({}))));
    const runtime = await import("./api");
    const controller = new AbortController();
    const first = runtime.prepareRuntime(controller.signal, vi.fn());
    const second = runtime.prepareRuntime(controller.signal, vi.fn());
    expect(first).toBe(second);
    await first;
    expect(fetch).toHaveBeenCalledTimes(1);
  });
});
