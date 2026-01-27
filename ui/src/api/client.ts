import type { SwipeAction, SwipeDeckResponse } from "../types";
import type {
  AuthResponse,
  LoginRequest,
  MeResponse,
  RegisterRequest,
  UndoResponse,
  UpdateUserPreferencesRequest,
  UserPreferencesDto
} from "./contracts";
import { apiRequest } from "./http";

const DEFAULT_SERVICE_TYPE = "tmdb";
const DEFAULT_SERVER_ID = "tmdb";

export async function fetchSwipeDeck(
  limit = 10,
  serviceType = DEFAULT_SERVICE_TYPE,
  serverId = DEFAULT_SERVER_ID
): Promise<SwipeDeckResponse> {
  return apiRequest<SwipeDeckResponse>({
    path: "/api/v1/swipedeck",
    query: { serviceType, serverId, limit }
  });
}

export async function sendSwipe(
  tmdbId: number,
  action: SwipeAction,
  serviceType = DEFAULT_SERVICE_TYPE,
  serverId = DEFAULT_SERVER_ID
) {
  return apiRequest({
    path: "/api/v1/interactions",
    method: "POST",
    body: { tmdbId, action, serviceType, serverId }
  });
}

export async function undoSwipe(
  serviceType = DEFAULT_SERVICE_TYPE,
  serverId = DEFAULT_SERVER_ID
): Promise<UndoResponse> {
  return apiRequest<UndoResponse>({
    path: "/api/v1/interactions/undo",
    method: "POST",
    query: { serviceType, serverId }
  });
}

export async function register(request: RegisterRequest) {
  return apiRequest<AuthResponse>({
    path: "/api/v1/auth/register",
    method: "POST",
    auth: false,
    body: request
  });
}

export async function login(request: LoginRequest) {
  return apiRequest<AuthResponse>({
    path: "/api/v1/auth/login",
    method: "POST",
    auth: false,
    body: request
  });
}

export async function me() {
  return apiRequest<MeResponse>({
    path: "/api/v1/auth/me"
  });
}

export async function getPreferences() {
  return apiRequest<UserPreferencesDto>({
    path: "/api/v1/preferences"
  });
}

export async function updatePreferences(request: UpdateUserPreferencesRequest) {
  return apiRequest<UserPreferencesDto>({
    path: "/api/v1/preferences",
    method: "PUT",
    body: request
  });
}
