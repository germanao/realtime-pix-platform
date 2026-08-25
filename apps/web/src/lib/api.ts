const azureApiBase = process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:5100";
const azurePresenceHubUrl =
  process.env.NEXT_PUBLIC_PRESENCE_HUB_URL ?? "http://localhost:5101/presence/hub";
const azureEventsHubUrl =
  process.env.NEXT_PUBLIC_EVENTS_HUB_URL ?? "http://localhost:5104/events/hub";
const awsRuntimeBase = process.env.NEXT_PUBLIC_AWS_RUNTIME_URL?.replace(/\/$/, "");

const selectionTtlMs = 60_000;
let runtimeSelection: { base: string; expiresAt: number } | undefined;

function isAwsWarmWindow(date = new Date()) {
  const parts = new Intl.DateTimeFormat("en-US", {
    timeZone: "America/Sao_Paulo",
    weekday: "short",
    hour: "2-digit",
    hourCycle: "h23"
  }).formatToParts(date);
  const weekday = parts.find((part) => part.type === "weekday")?.value;
  const hour = Number(parts.find((part) => part.type === "hour")?.value);
  return weekday !== "Sat" && weekday !== "Sun" && hour >= 9 && hour < 15;
}

async function resolveApiBase() {
  if (!awsRuntimeBase || !isAwsWarmWindow()) {
    return azureApiBase;
  }

  const now = Date.now();
  if (runtimeSelection && runtimeSelection.expiresAt > now) {
    return runtimeSelection.base;
  }

  try {
    const response = await fetch(`${awsRuntimeBase}/health/live`, {
      cache: "no-store",
      signal: AbortSignal.timeout(3_000)
    });
    runtimeSelection = {
      base: response.ok ? awsRuntimeBase : azureApiBase,
      expiresAt: now + selectionTtlMs
    };
  } catch {
    runtimeSelection = { base: azureApiBase, expiresAt: now + selectionTtlMs };
  }

  return runtimeSelection.base;
}

export async function resolvePresenceHubUrl() {
  return (await resolveApiBase()) === awsRuntimeBase
    ? `${awsRuntimeBase}/presence/hub`
    : azurePresenceHubUrl;
}

export async function resolveEventsHubUrl() {
  return (await resolveApiBase()) === awsRuntimeBase
    ? `${awsRuntimeBase}/events/hub`
    : azureEventsHubUrl;
}

export async function api<T>(path: string, init?: RequestInit): Promise<T> {
  const headers = new Headers(init?.headers);
  if (init?.body && !headers.has("content-type")) {
    headers.set("content-type", "application/json");
  }

  const apiBase = await resolveApiBase();
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
  const apiBase = runtimeSelection?.base ??
    (awsRuntimeBase && isAwsWarmWindow() ? awsRuntimeBase : azureApiBase);
  navigator.sendBeacon(`${apiBase}/presence/leave`, new Blob([body], { type: "application/json" }));
}
