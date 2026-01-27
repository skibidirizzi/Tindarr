type CacheEntry<T> = {
  v: number;
  savedAtMs: number;
  ttlMs: number;
  value: T;
};

function nowMs() {
  return Date.now();
}

export function getCachedJson<T>(key: string): T | null {
  try {
    const raw = localStorage.getItem(key);
    if (!raw) return null;

    const parsed = JSON.parse(raw) as CacheEntry<T>;
    if (!parsed || typeof parsed !== "object") return null;
    if (typeof parsed.savedAtMs !== "number" || typeof parsed.ttlMs !== "number") return null;

    const age = nowMs() - parsed.savedAtMs;
    if (age < 0) return null;
    if (age > parsed.ttlMs) return null;

    return parsed.value ?? null;
  } catch {
    return null;
  }
}

export function setCachedJson<T>(key: string, value: T, ttlMs: number, version = 1) {
  try {
    const entry: CacheEntry<T> = { v: version, savedAtMs: nowMs(), ttlMs, value };
    localStorage.setItem(key, JSON.stringify(entry));
  } catch {
    // Ignore quota / storage disabled.
  }
}

