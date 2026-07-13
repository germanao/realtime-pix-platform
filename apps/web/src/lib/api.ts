const apiBase = process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:5100";

export const presenceHubUrl =
  process.env.NEXT_PUBLIC_PRESENCE_HUB_URL ?? "http://localhost:5101/presence/hub";

export const eventsHubUrl =
  process.env.NEXT_PUBLIC_EVENTS_HUB_URL ?? "http://localhost:5104/events/hub";

export async function api<T>(path: string, init?: RequestInit): Promise<T> {
  const headers = new Headers(init?.headers);
  if (init?.body && !headers.has("content-type")) {
    headers.set("content-type", "application/json");
  }

  const response = await fetch(`${apiBase}${path}`, {
    ...init,
    headers,
    cache: "no-store"
  });

  if (!response.ok) {
    const body = await response.text();
    let message = body;

    try {
      const parsed = JSON.parse(body) as { message?: string };
      message = parsed.message ?? body;
    } catch {
      // Preserve non-JSON service errors.
    }

    throw new Error(message || `Request failed with ${response.status}`);
  }

  return response.json() as Promise<T>;
}

export function sendPresenceLeave(userId: string, connectionId?: string | null) {
  const body = JSON.stringify({ userId, connectionId });
  navigator.sendBeacon(`${apiBase}/presence/leave`, new Blob([body], { type: "application/json" }));
}

