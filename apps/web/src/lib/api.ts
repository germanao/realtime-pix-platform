import { requestJson, retryStartup } from "./startup";

const azureApiBase = process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:5100";
const azurePresenceHubUrl = process.env.NEXT_PUBLIC_PRESENCE_HUB_URL ?? "http://localhost:5101/presence/hub";
const azureEventsHubUrl = process.env.NEXT_PUBLIC_EVENTS_HUB_URL ?? "http://localhost:5104/events/hub";
const awsRuntimeBase = process.env.NEXT_PUBLIC_AWS_RUNTIME_URL?.trim().replace(/\/$/, "");
// A page's HTTP requests and hub must stay on the same runtime. Never change it on a TTL.
let selectedBase: string | undefined;
let preparation: Promise<void> | undefined;
let preparationSignal: AbortSignal | undefined;
let useAzureFallback = false;

export function resetRuntimeToAzure() {
  selectedBase = undefined;
  preparation = undefined;
  useAzureFallback = true;
}

export function prepareRuntime(signal: AbortSignal, status: (message: string) => void): Promise<void> {
  if (selectedBase) return Promise.resolve();
  if (preparation && !preparationSignal?.aborted) return preparation;
  preparationSignal = signal;
  const task = (async () => {
    if (awsRuntimeBase && !useAzureFallback) {
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
      } catch {
        signal.throwIfAborted();
        status("Starting Azure fallback…");
      }
    } else status("Starting demo services…");

    // Gateway readiness fans out to all downstreams. Poke the other external
    // endpoints in parallel so their ingress starts waking immediately.
    await Promise.all([
      requestJson(azurePresenceHubUrl.replace(/\/presence\/hub$/, "/health/live"), { signal }).catch(() => undefined),
      requestJson(azureEventsHubUrl.replace(/\/events\/hub$/, "/health/live"), { signal }).catch(() => undefined),
      retryStartup(() => requestJson(`${azureApiBase}/health/ready`, { signal }), signal)
    ]);
    signal.throwIfAborted();
    selectedBase = azureApiBase;
  })();
  preparation = task;
  void task.finally(() => { if (preparation === task) preparation = undefined; }).catch(() => undefined);
  return task;
}

export async function resolvePresenceHubUrl() {
  return selectedBase === awsRuntimeBase && awsRuntimeBase ? `${awsRuntimeBase}/presence/hub` : azurePresenceHubUrl;
}

export async function resolveEventsHubUrl() {
  return selectedBase === awsRuntimeBase && awsRuntimeBase ? `${awsRuntimeBase}/events/hub` : azureEventsHubUrl;
}

export function api<T>(path: string, init: RequestInit = {}): Promise<T> {
  return requestJson<T>(`${selectedBase ?? azureApiBase}${path}`, init);
}

export async function keepRuntimeAwake(signal: AbortSignal) {
  if (selectedBase === awsRuntimeBase && awsRuntimeBase) {
    await requestJson(`${awsRuntimeBase}/runtime/wake`, { method: "POST", signal });
  }
}

export function sendPresenceLeave(userId: string, connectionId: string) {
  navigator.sendBeacon(`${selectedBase ?? azureApiBase}/presence/leave`, new Blob([
    JSON.stringify({ userId, connectionId })
  ], { type: "application/json" }));
}
