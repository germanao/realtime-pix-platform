import { HttpError, requestJson, retryStartup } from "./startup";

const localApiBase = process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:5100";
const localPresenceHubUrl = process.env.NEXT_PUBLIC_PRESENCE_HUB_URL ?? "http://localhost:5101/presence/hub";
const localEventsHubUrl = process.env.NEXT_PUBLIC_EVENTS_HUB_URL ?? "http://localhost:5104/events/hub";
const defaultProductionRuntime = "https://djb1ah1j5qyrj.cloudfront.net";
const configuredRuntime = process.env.NEXT_PUBLIC_AWS_RUNTIME_URL?.trim().replace(/\/$/, "");
const awsRuntimeBase = configuredRuntime ||
  (process.env.NODE_ENV === "production" ? defaultProductionRuntime : undefined);
// A page's HTTP requests and hub must stay on the same runtime. Never change it on a TTL.
let selectedBase: string | undefined;
let preparation: Promise<void> | undefined;
let preparationSignal: AbortSignal | undefined;

export function resetRuntime() {
  selectedBase = undefined;
  preparation = undefined;
}

export function prepareRuntime(signal: AbortSignal, status: (message: string) => void): Promise<void> {
  if (selectedBase) return Promise.resolve();
  if (preparation && !preparationSignal?.aborted) return preparation;
  preparationSignal = signal;
  const task = (async () => {
    if (awsRuntimeBase) {
      status("Starting AWS demo…");
      try {
        // This control-plane endpoint works even when the application VM is stopped.
        await retryStartup(() => requestJson(`${awsRuntimeBase}/runtime/wake`, {
          method: "POST", signal
        }), signal, 20_000);
        await retryStartup(() => requestJson(`${awsRuntimeBase}/health/ready`, { signal }), signal, 120_000);
        signal.throwIfAborted();
        selectedBase = awsRuntimeBase;
        return;
      } catch (error) {
        signal.throwIfAborted();
        if (error instanceof HttpError && error.status === 409) {
          throw new HttpError("Today's six-hour demo allowance is used. Please return after 00:00 UTC.", 409);
        }
        throw error;
      }
    } else status("Starting demo services…");

    await retryStartup(() => requestJson(`${localApiBase}/health/ready`, { signal }), signal);
    signal.throwIfAborted();
    selectedBase = localApiBase;
  })();
  preparation = task;
  void task.finally(() => { if (preparation === task) preparation = undefined; }).catch(() => undefined);
  return task;
}

export async function resolvePresenceHubUrl() {
  return selectedBase === awsRuntimeBase && awsRuntimeBase ? `${awsRuntimeBase}/presence/hub` : localPresenceHubUrl;
}

export async function resolveEventsHubUrl() {
  return selectedBase === awsRuntimeBase && awsRuntimeBase ? `${awsRuntimeBase}/events/hub` : localEventsHubUrl;
}

export function api<T>(path: string, init: RequestInit = {}): Promise<T> {
  return requestJson<T>(`${selectedBase ?? awsRuntimeBase ?? localApiBase}${path}`, init);
}

export async function keepRuntimeAwake(signal: AbortSignal) {
  if (selectedBase === awsRuntimeBase && awsRuntimeBase) {
    await requestJson(`${awsRuntimeBase}/runtime/wake`, { method: "POST", signal });
  }
}

export function sendPresenceLeave(userId: string, connectionId: string) {
  navigator.sendBeacon(`${selectedBase ?? awsRuntimeBase ?? localApiBase}/presence/leave`, new Blob([
    JSON.stringify({ userId, connectionId })
  ], { type: "application/json" }));
}
