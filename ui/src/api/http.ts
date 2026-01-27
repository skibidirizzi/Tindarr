import { getAccessToken } from "../auth/session";

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? "";

export class ApiError extends Error {
  status: number;
  payload: unknown;

  constructor(message: string, status: number, payload: unknown) {
    super(message);
    this.name = "ApiError";
    this.status = status;
    this.payload = payload;
  }
}

type QueryValue = string | number | boolean | null | undefined;

export type ApiRequestOptions = {
  path: string;
  method?: "GET" | "POST" | "PUT" | "DELETE";
  query?: Record<string, QueryValue>;
  body?: unknown;
  headers?: Record<string, string>;
  auth?: boolean;
};

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

async function tryReadBody(response: Response): Promise<unknown> {
  if (response.status === 204) return null;

  const contentType = response.headers.get("content-type") ?? "";
  if (contentType.includes("application/json")) {
    try {
      return await response.json();
    } catch {
      return null;
    }
  }

  try {
    return await response.text();
  } catch {
    return null;
  }
}

function errorMessageFromPayload(payload: unknown, fallback: string) {
  if (typeof payload === "string" && payload.trim()) return payload;
  if (payload && typeof payload === "object" && "message" in payload && typeof (payload as any).message === "string") {
    return (payload as any).message;
  }
  if (payload && typeof payload === "object" && "error" in payload && typeof (payload as any).error === "string") {
    return (payload as any).error;
  }
  return fallback;
}

export async function apiRequest<T>(options: ApiRequestOptions): Promise<T> {
  const headers: Record<string, string> = {
    Accept: "application/json",
    ...(options.headers ?? {})
  };

  if (options.auth !== false) {
    const token = getAccessToken();
    if (token) {
      headers.Authorization = `Bearer ${token}`;
    }
  }

  let body: BodyInit | undefined;
  if (options.body !== undefined) {
    headers["Content-Type"] = "application/json";
    body = JSON.stringify(options.body);
  }

  const response = await fetch(buildUrl(options.path, options.query), {
    method: options.method ?? "GET",
    headers,
    body,
    cache: "no-store"
  });

  const payload = await tryReadBody(response);

  if (!response.ok) {
    throw new ApiError(
      errorMessageFromPayload(payload, `Request failed (${response.status})`),
      response.status,
      payload
    );
  }

  return payload as T;
}

