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

