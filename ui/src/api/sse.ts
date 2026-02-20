import { getAccessToken } from "../auth/session";

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? "";

type QueryValue = string | number | boolean | null | undefined;

function buildUrl(path: string, query?: Record<string, QueryValue>) {
  const normalizedPath = path.startsWith("/") ? path : `/${path}`;
  const url = new URL(`${API_BASE_URL}${normalizedPath}`, window.location.origin);

  if (query) {
    for (const [key, value] of Object.entries(query)) {
      if (value === null || value === undefined) continue;
      url.searchParams.set(key, String(value));
    }
  }

  return url.toString();
}

export type SseMessage = {
  event?: string;
  data: string;
};

export type ConnectSseOptions = {
  path: string;
  query?: Record<string, QueryValue>;
  signal: AbortSignal;
  onMessage: (msg: SseMessage) => void;
};

function readErrorMessage(text: string | null | undefined, fallback: string) {
  const t = (text ?? "").trim();
  if (t) return t;
  return fallback;
}

/**
 * Opens a Server-Sent Events (SSE) stream over `fetch`.
 * This is used instead of `EventSource` so we can attach Authorization headers.
 */
export async function connectSse(options: ConnectSseOptions): Promise<void> {
  const headers: Record<string, string> = {
    Accept: "text/event-stream"
  };

  const token = getAccessToken();
  if (token) {
    headers.Authorization = `Bearer ${token}`;
  }

  const response = await fetch(buildUrl(options.path, options.query), {
    method: "GET",
    headers,
    cache: "no-store",
    signal: options.signal
  });

  if (!response.ok) {
    let payload: string | null = null;
    try {
      payload = await response.text();
    } catch {
      payload = null;
    }
    throw new Error(readErrorMessage(payload, `SSE request failed (${response.status})`));
  }

  const reader = response.body?.getReader();
  if (!reader) return;

  const decoder = new TextDecoder();
  let buffer = "";

  let eventName: string | undefined;
  let dataLines: string[] = [];

  function dispatchMessage() {
    if (dataLines.length === 0) {
      eventName = undefined;
      return;
    }

    options.onMessage({
      event: eventName,
      data: dataLines.join("\n")
    });

    eventName = undefined;
    dataLines = [];
  }

  while (true) {
    const { value, done } = await reader.read();
    if (done) break;

    buffer += decoder.decode(value, { stream: true });

    // Parse line-by-line.
    while (true) {
      const newlineIndex = buffer.indexOf("\n");
      if (newlineIndex < 0) break;

      let line = buffer.slice(0, newlineIndex);
      buffer = buffer.slice(newlineIndex + 1);

      if (line.endsWith("\r")) line = line.slice(0, -1);

      // Blank line ends a message.
      if (line === "") {
        dispatchMessage();
        continue;
      }

      // Comment line.
      if (line.startsWith(":")) {
        continue;
      }

      if (line.startsWith("event:")) {
        eventName = line.slice("event:".length).trim();
        continue;
      }

      if (line.startsWith("data:")) {
        // Per SSE spec, data can be sent in multiple lines.
        dataLines.push(line.slice("data:".length).trimStart());
        continue;
      }

      // Ignore other fields (id, retry).
    }
  }

  // Flush any trailing message if the stream ends.
  dispatchMessage();
}
