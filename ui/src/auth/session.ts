const ACCESS_TOKEN_KEY = "tindarr_access_token";
const ACCESS_TOKEN_EXPIRES_KEY = "tindarr_access_token_expires_at_utc";
const USER_ID_KEY = "tindarr_user_id";

export type StoredSession = {
  accessToken: string;
  expiresAtUtc: string;
  userId: string;
  displayName: string;
  roles: string[];
};

export function getStoredUserId(): string | null {
  return localStorage.getItem(USER_ID_KEY);
}

export function setStoredUserId(userId: string) {
  localStorage.setItem(USER_ID_KEY, userId);
}

export function getAccessToken(): string | null {
  const token = localStorage.getItem(ACCESS_TOKEN_KEY);
  if (!token) return null;
  const expiresAtUtc = localStorage.getItem(ACCESS_TOKEN_EXPIRES_KEY);
  if (!expiresAtUtc) return token;
  const expiresAtMs = Date.parse(expiresAtUtc);
  if (Number.isNaN(expiresAtMs)) return token;
  // Add small skew to avoid racing expiry.
  if (Date.now() > expiresAtMs - 30_000) return null;
  return token;
}

export function setSession(session: StoredSession) {
  localStorage.setItem(ACCESS_TOKEN_KEY, session.accessToken);
  localStorage.setItem(ACCESS_TOKEN_EXPIRES_KEY, session.expiresAtUtc);
  localStorage.setItem(USER_ID_KEY, session.userId);
}

export function clearSession() {
  localStorage.removeItem(ACCESS_TOKEN_KEY);
  localStorage.removeItem(ACCESS_TOKEN_EXPIRES_KEY);
}

