/** Retry only reads and explicitly idempotent startup operations, never arbitrary payments. */
export class HttpError extends Error {
  constructor(message: string, public status: number, public retryAfterMs = 0) {
    super(message);
  }
}

export function pause(ms: number, signal: AbortSignal): Promise<void> {
  signal.throwIfAborted();
  return new Promise((resolve, reject) => {
    const abort = () => { clearTimeout(timer); reject(signal.reason); };
    const timer = setTimeout(() => { signal.removeEventListener("abort", abort); resolve(); }, ms);
    signal.addEventListener("abort", abort, { once: true });
  });
}

export async function requestJson<T>(url: string, init: RequestInit = {}): Promise<T> {
  const headers = new Headers(init.headers);
  if (init.body && !headers.has("content-type")) headers.set("content-type", "application/json");
  const response = await fetch(url, {
    ...init, headers, cache: "no-store",
    signal: init.signal
      ? AbortSignal.any([init.signal, AbortSignal.timeout(15_000)])
      : AbortSignal.timeout(15_000)
  });
  if (!response.ok) {
    const raw = await response.text();
    let message = `Request failed with ${response.status}`;
    try {
      const body = JSON.parse(raw) as { message?: string; title?: string; reason?: string };
      message = body.message ?? body.title ?? body.reason ?? message;
    } catch { /* Do not show proxy HTML as an application error. */ }
    const retryAfter = response.headers.get("retry-after");
    const seconds = Number(retryAfter);
    const delay = retryAfter === null ? 0 : Number.isFinite(seconds)
      ? seconds * 1000 : Date.parse(retryAfter) - Date.now();
    throw new HttpError(message, response.status, Math.max(0, delay || 0));
  }
  return response.json() as Promise<T>;
}

export async function retryStartup<T>(
  operation: () => Promise<T>, signal: AbortSignal, budgetMs = 180_000
): Promise<T> {
  const deadline = Date.now() + budgetMs;
  let attempt = 0;
  while (true) {
    signal.throwIfAborted();
    try { return await operation(); }
    catch (error) {
      signal.throwIfAborted();
      if (error instanceof HttpError && ![408, 425, 429, 500, 502, 503, 504].includes(error.status)) throw error;
      const remaining = deadline - Date.now();
      if (remaining <= 0) throw error;
      const backoff = Math.min(10_000, 1000 * 2 ** Math.min(attempt++, 4)) + Math.random() * 500;
      const delay = Math.max(backoff, error instanceof HttpError ? error.retryAfterMs : 0);
      if (delay >= remaining) throw error;
      await pause(delay, signal);
    }
  }
}
