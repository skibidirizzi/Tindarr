import type { SwipeAction, SwipeDeckResponse } from "../types";

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? "";
const DEFAULT_SERVICE_TYPE = "tmdb";
const DEFAULT_SERVER_ID = "tmdb";

function getUserId(): string {
  const stored = localStorage.getItem("tindarr_user_id");
  if (stored) {
    return stored;
  }
  const fallback = `user-${Math.random().toString(16).slice(2, 10)}`;
  localStorage.setItem("tindarr_user_id", fallback);
  return fallback;
}

function baseHeaders() {
  return {
    "Content-Type": "application/json",
    "X-User-Id": getUserId(),
    "X-User-Role": "Contributor"
  };
}

export async function fetchSwipeDeck(limit = 10, serviceType = DEFAULT_SERVICE_TYPE, serverId = DEFAULT_SERVER_ID): Promise<SwipeDeckResponse> {
  const params = new URLSearchParams({
    serviceType,
    serverId,
    limit: limit.toString()
  });

  const response = await fetch(`${API_BASE_URL}/api/v1/swipedeck?${params.toString()}`, {
    headers: baseHeaders()
  });

  if (!response.ok) {
    throw new Error(`Failed to load swipedeck (${response.status})`);
  }

  return response.json();
}

export async function sendSwipe(tmdbId: number, action: SwipeAction, serviceType = DEFAULT_SERVICE_TYPE, serverId = DEFAULT_SERVER_ID) {
  const response = await fetch(`${API_BASE_URL}/api/v1/interactions`, {
    method: "POST",
    headers: baseHeaders(),
    body: JSON.stringify({
      tmdbId,
      action,
      serviceType,
      serverId
    })
  });

  if (!response.ok) {
    throw new Error(`Failed to send swipe (${response.status})`);
  }

  return response.json();
}

export async function undoSwipe(serviceType = DEFAULT_SERVICE_TYPE, serverId = DEFAULT_SERVER_ID) {
  const params = new URLSearchParams({
    serviceType,
    serverId
  });

  const response = await fetch(`${API_BASE_URL}/api/v1/interactions/undo?${params.toString()}`, {
    method: "POST",
    headers: baseHeaders()
  });

  if (!response.ok) {
    throw new Error(`Failed to undo swipe (${response.status})`);
  }

  return response.json();
}
