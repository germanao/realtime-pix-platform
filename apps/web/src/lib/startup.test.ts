import { afterEach, describe, expect, it, vi } from "vitest";
import { HttpError, pause, requestJson, retryStartup } from "./startup";

afterEach(() => { vi.useRealTimers(); vi.unstubAllGlobals(); });

describe("cold-start retries", () => {
  it("recovers after more than the old eight-second timeout", async () => {
    vi.useFakeTimers();
    const operation = vi.fn().mockRejectedValueOnce(new HttpError("sleeping", 503))
      .mockRejectedValueOnce(new HttpError("sleeping", 504))
      .mockRejectedValueOnce(new TypeError("Failed to fetch"))
      .mockRejectedValueOnce(new HttpError("sleeping", 503)).mockResolvedValue("ready");
    const result = retryStartup(operation, new AbortController().signal);
    await vi.advanceTimersByTimeAsync(30_000);
    expect(await result).toBe("ready");
    expect(operation).toHaveBeenCalledTimes(5);
  });

  it("honors Retry-After instead of hammering a throttled service", async () => {
    vi.useFakeTimers();
    const operation = vi.fn().mockRejectedValueOnce(new HttpError("busy", 429, 20_000)).mockResolvedValue("ok");
    const result = retryStartup(operation, new AbortController().signal);
    await vi.advanceTimersByTimeAsync(19_999);
    expect(operation).toHaveBeenCalledTimes(1);
    await vi.advanceTimersByTimeAsync(1);
    expect(await result).toBe("ok");
  });

  it("does not retry validation failures", async () => {
    const operation = vi.fn().mockRejectedValue(new HttpError("invalid", 400));
    await expect(retryStartup(operation, new AbortController().signal)).rejects.toThrow("invalid");
    expect(operation).toHaveBeenCalledTimes(1);
  });

  it("cancels backoff on unmount", async () => {
    const controller = new AbortController();
    const result = pause(30_000, controller.signal);
    controller.abort();
    await expect(result).rejects.toMatchObject({ name: "AbortError" });
  });

  it("surfaces the actual problem detail and Retry-After", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(JSON.stringify({ title: "Bank unavailable" }), {
      status: 503, headers: { "Retry-After": "12" }
    })));
    await expect(requestJson("/test")).rejects.toMatchObject({ message: "Bank unavailable", status: 503, retryAfterMs: 12_000 });
  });
});
