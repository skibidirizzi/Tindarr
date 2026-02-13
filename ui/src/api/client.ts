import type { SwipeAction, SwipeDeckResponse } from "../types";
import { getServiceScope } from "../serviceScope";
import type {
  AdminInteractionSearchResponse,
  AdminSetPasswordRequest,
  AuthResponse,
  CreateUserRequest,
  EmbyConnectionTestResponse,
  EmbyLibrarySyncResponse,
  EmbyServerDto,
  EmbySettingsDto,
  InteractionListResponse,
  JellyfinConnectionTestResponse,
  JellyfinLibrarySyncResponse,
  JellyfinServerDto,
  JellyfinSettingsDto,
  LoginRequest,
  MeResponse,
  MatchesResponse,
  MovieDetailsDto,
  PlexAuthStatusResponse,
  PlexPinCreateResponse,
  PlexPinStatusResponse,
  PlexServerDto,
  RadarrQualityProfileDto,
  RadarrRootFolderDto,
  RadarrSettingsDto,
  RadarrConnectionTestResponse,
  RadarrLibrarySyncResponse,
  RegisterRequest,
  GuestLoginRequest,
  SetUserRolesRequest,
  UndoResponse,
  UpdateEmbySettingsRequest,
  UpdateJellyfinSettingsRequest,
  UpdateRadarrSettingsRequest,
  UpdateUserRequest,
  UpdateUserPreferencesRequest,
  UserDto,
  UserPreferencesDto,
  ServiceScopeOptionDto
  ,
  TmdbCacheSettingsDto,
  UpdateTmdbCacheSettingsRequest,
  StartTmdbBuildRequest,
  TmdbBuildStatusDto,
  TmdbStoredMovieAdminListResponse,
  TmdbFetchMovieImagesResultDto
  ,
  CreateRoomRequest,
  CreateRoomResponse,
  JoinRoomResponse,
  RoomJoinUrlResponse,
  RoomMatchesResponse,
  RoomStateResponse
} from "./contracts";
import { ApiError, apiRequest } from "./http";
import { getCachedJson, setCachedJson } from "./localCache";

function resolveScope(serviceType?: string, serverId?: string) {
  const stored = getServiceScope();
  return {
    serviceType: (serviceType ?? stored.serviceType).trim().toLowerCase(),
    serverId: (serverId ?? stored.serverId).trim() || "default"
  };
}

export async function fetchConfiguredScopes(): Promise<ServiceScopeOptionDto[]> {
  return apiRequest<ServiceScopeOptionDto[]>({
    path: "/api/v1/scopes"
  });
}

export async function fetchSwipeDeck(
  limit = 10,
  serviceType?: string,
  serverId?: string
): Promise<SwipeDeckResponse> {
  const scope = resolveScope(serviceType, serverId);
  return apiRequest<SwipeDeckResponse>({
    path: "/api/v1/swipedeck",
    query: { serviceType: scope.serviceType, serverId: scope.serverId, limit }
  });
}

export async function sendSwipe(
  tmdbId: number,
  action: SwipeAction,
  serviceType?: string,
  serverId?: string
) {
  const scope = resolveScope(serviceType, serverId);
  return apiRequest({
    path: "/api/v1/interactions",
    method: "POST",
    body: { tmdbId, action, serviceType: scope.serviceType, serverId: scope.serverId }
  });
}

export async function undoSwipe(
  serviceType?: string,
  serverId?: string
): Promise<UndoResponse> {
  const scope = resolveScope(serviceType, serverId);
  return apiRequest<UndoResponse>({
    path: "/api/v1/interactions/undo",
    method: "POST",
    query: { serviceType: scope.serviceType, serverId: scope.serverId }
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

export async function guestLogin(request: GuestLoginRequest = {}) {
  return apiRequest<AuthResponse>({
    path: "/api/v1/auth/guest",
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

export async function listInteractions(
  {
    action,
    tmdbId,
    limit = 500
  }: { action?: SwipeAction; tmdbId?: number; limit?: number } = {},
  serviceType?: string,
  serverId?: string
): Promise<InteractionListResponse> {
  const scope = resolveScope(serviceType, serverId);
  return apiRequest<InteractionListResponse>({
    path: "/api/v1/interactions",
    query: {
      serviceType: scope.serviceType,
      serverId: scope.serverId,
      action,
      tmdbId,
      limit
    }
  });
}

export async function fetchMatches(
  {
    minUsers = 2,
    interactionLimit = 20000
  }: { minUsers?: number; interactionLimit?: number } = {},
  serviceType?: string,
  serverId?: string
): Promise<MatchesResponse> {
  const scope = resolveScope(serviceType, serverId);
  return apiRequest<MatchesResponse>({
    path: "/api/v1/matches",
    query: { serviceType: scope.serviceType, serverId: scope.serverId, minUsers, interactionLimit }
  });
}

export async function createRoom(request: CreateRoomRequest): Promise<CreateRoomResponse> {
  return apiRequest<CreateRoomResponse>({
    path: "/api/v1/rooms",
    method: "POST",
    body: request
  });
}

export async function joinRoom(roomId: string): Promise<JoinRoomResponse> {
  return apiRequest<JoinRoomResponse>({
    path: `/api/v1/rooms/${encodeURIComponent(roomId)}/join`,
    method: "POST"
  });
}

export async function getRoom(roomId: string): Promise<RoomStateResponse> {
  return apiRequest<RoomStateResponse>({
    path: `/api/v1/rooms/${encodeURIComponent(roomId)}`
  });
}

export async function closeRoom(roomId: string): Promise<RoomStateResponse> {
  return apiRequest<RoomStateResponse>({
    path: `/api/v1/rooms/${encodeURIComponent(roomId)}/close`,
    method: "POST"
  });
}

export async function getRoomJoinUrl(roomId: string): Promise<RoomJoinUrlResponse> {
  return apiRequest<RoomJoinUrlResponse>({
    path: `/api/v1/rooms/${encodeURIComponent(roomId)}/join-url`
  });
}

export async function sendRoomSwipe(roomId: string, tmdbId: number, action: SwipeAction) {
  return apiRequest({
    path: `/api/v1/rooms/${encodeURIComponent(roomId)}/swipe`,
    method: "POST",
    body: { tmdbId, action }
  });
}

export async function fetchRoomSwipeDeck(roomId: string, limit = 10): Promise<SwipeDeckResponse> {
  return apiRequest<SwipeDeckResponse>({
    path: `/api/v1/rooms/${encodeURIComponent(roomId)}/swipedeck`,
    query: { limit }
  });
}

export async function getRoomMatches(roomId: string): Promise<RoomMatchesResponse> {
  return apiRequest<RoomMatchesResponse>({
    path: `/api/v1/rooms/${encodeURIComponent(roomId)}/matches`
  });
}

export async function fetchMovieDetails(tmdbId: number): Promise<MovieDetailsDto> {
  const cacheKey = `tindarr:movieDetails:v1:${tmdbId}`;
  const cached = getCachedJson<MovieDetailsDto>(cacheKey);
  if (cached) {
    return cached;
  }

  const fresh = await apiRequest<MovieDetailsDto>({
    path: `/api/v1/tmdb/movies/${tmdbId}`
  });

  // Movie metadata changes rarely; keep this fairly long to avoid constant refetching.
  // If you want “instant updates”, reduce this TTL.
  setCachedJson(cacheKey, fresh, 7 * 24 * 60 * 60 * 1000);
  return fresh;
}

export async function tmdbGetCacheSettings(): Promise<TmdbCacheSettingsDto> {
  return apiRequest<TmdbCacheSettingsDto>({
    path: "/api/v1/tmdb/cache/settings"
  });
}

export async function tmdbUpdateCacheSettings(request: UpdateTmdbCacheSettingsRequest): Promise<TmdbCacheSettingsDto> {
  return apiRequest<TmdbCacheSettingsDto>({
    path: "/api/v1/tmdb/cache/settings",
    method: "PUT",
    body: request
  });
}

export async function tmdbGetBuildStatus(): Promise<TmdbBuildStatusDto> {
  return apiRequest<TmdbBuildStatusDto>({
    path: "/api/v1/tmdb/build/status"
  });
}

export async function tmdbStartBuild(request: StartTmdbBuildRequest): Promise<TmdbBuildStatusDto> {
  try {
    return await apiRequest<TmdbBuildStatusDto>({
      path: "/api/v1/tmdb/build/start",
      method: "POST",
      body: request
    });
  } catch (e) {
    // The API returns 409 with a status payload if a build is already running.
    if (e instanceof ApiError && e.status === 409 && e.payload && typeof e.payload === "object") {
      return e.payload as TmdbBuildStatusDto;
    }
    throw e;
  }
}

export async function tmdbCancelBuild(reason?: string): Promise<TmdbBuildStatusDto> {
  return apiRequest<TmdbBuildStatusDto>({
    path: "/api/v1/tmdb/build/cancel",
    method: "POST",
    query: { reason: reason ?? "Canceled by admin" }
  });
}

export async function tmdbListStoredMovies(params: {
  skip?: number;
  take?: number;
  missingDetailsOnly?: boolean;
  missingImagesOnly?: boolean;
  q?: string;
} = {}): Promise<TmdbStoredMovieAdminListResponse> {
  return apiRequest<TmdbStoredMovieAdminListResponse>({
    path: "/api/v1/tmdb/cache/movies",
    query: {
      skip: params.skip ?? 0,
      take: params.take ?? 50,
      missingDetailsOnly: params.missingDetailsOnly ?? false,
      missingImagesOnly: params.missingImagesOnly ?? false,
      q: params.q
    }
  });
}

export async function tmdbFillMovieDetails(tmdbId: number, rateLimitOverride = false): Promise<void> {
  await apiRequest<void>({
    path: `/api/v1/tmdb/cache/movies/${tmdbId}/fill-details`,
    method: "POST",
    query: { rateLimitOverride }
  });
}

export async function tmdbFetchMovieImages(tmdbId: number, params: { includePoster?: boolean; includeBackdrop?: boolean } = {}): Promise<TmdbFetchMovieImagesResultDto> {
  return apiRequest<TmdbFetchMovieImagesResultDto>({
    path: `/api/v1/tmdb/cache/movies/${tmdbId}/fetch-images`,
    method: "POST",
    query: {
      includePoster: params.includePoster ?? true,
      includeBackdrop: params.includeBackdrop ?? true
    }
  });
}

export async function adminListUsers(skip = 0, take = 200): Promise<UserDto[]> {
  return apiRequest<UserDto[]>({
    path: "/api/v1/admin/users",
    query: { skip, take }
  });
}

export async function adminGetJoinAddressSettings(): Promise<JoinAddressSettingsDto> {
  return apiRequest<JoinAddressSettingsDto>({
    path: "/api/v1/admin/join-address",
    method: "GET"
  });
}

export async function adminUpdateJoinAddressSettings(request: UpdateJoinAddressSettingsRequest): Promise<JoinAddressSettingsDto> {
  return apiRequest<JoinAddressSettingsDto>({
    path: "/api/v1/admin/join-address",
    method: "PUT",
    body: request
  });
}

export async function adminCreateUser(request: CreateUserRequest): Promise<UserDto> {
  return apiRequest<UserDto>({
    path: "/api/v1/admin/users",
    method: "POST",
    body: request
  });
}

export async function adminUpdateUser(userId: string, request: UpdateUserRequest): Promise<void> {
  await apiRequest<void>({
    path: `/api/v1/admin/users/${encodeURIComponent(userId)}`,
    method: "PUT",
    body: request
  });
}

export async function adminDeleteUser(userId: string): Promise<void> {
  await apiRequest<void>({
    path: `/api/v1/admin/users/${encodeURIComponent(userId)}`,
    method: "DELETE"
  });
}

export async function adminSetUserRoles(userId: string, request: SetUserRolesRequest): Promise<void> {
  await apiRequest<void>({
    path: `/api/v1/admin/users/${encodeURIComponent(userId)}/roles`,
    method: "POST",
    body: request
  });
}

export async function adminSetUserPassword(userId: string, request: AdminSetPasswordRequest): Promise<void> {
  await apiRequest<void>({
    path: `/api/v1/admin/users/${encodeURIComponent(userId)}/set-password`,
    method: "POST",
    body: request
  });
}

export async function adminSearchInteractions(params: {
  userId?: string;
  serviceType?: string;
  serverId?: string;
  action?: "Like" | "Nope" | "Skip" | "Superlike";
  tmdbId?: number;
  limit?: number;
}): Promise<AdminInteractionSearchResponse> {
  return apiRequest<AdminInteractionSearchResponse>({
    path: "/api/v1/admin/interactions",
    query: {
      userId: params.userId,
      serviceType: params.serviceType,
      serverId: params.serverId,
      action: params.action,
      tmdbId: params.tmdbId,
      limit: params.limit
    }
  });
}

export async function plexGetAuthStatus(): Promise<PlexAuthStatusResponse> {
  return apiRequest<PlexAuthStatusResponse>({
    path: "/api/v1/plex/auth/status"
  });
}

export async function plexCreatePin(): Promise<PlexPinCreateResponse> {
  return apiRequest<PlexPinCreateResponse>({
    path: "/api/v1/plex/pin",
    method: "POST"
  });
}

export async function plexVerifyPin(pinId: number): Promise<PlexPinStatusResponse> {
  return apiRequest<PlexPinStatusResponse>({
    path: `/api/v1/plex/pins/${pinId}/verify`,
    method: "POST"
  });
}

export async function plexListServers(): Promise<PlexServerDto[]> {
  return apiRequest<PlexServerDto[]>({
    path: "/api/v1/plex/servers"
  });
}

export async function plexSyncServers(): Promise<PlexServerDto[]> {
  return apiRequest<PlexServerDto[]>({
    path: "/api/v1/plex/servers/sync",
    method: "POST"
  });
}

export async function plexSyncLibrary(serviceType: string, serverId: string) {
  return apiRequest<{ serviceType: string; serverId: string; count: number; syncedAtUtc: string }>({
    path: "/api/v1/plex/library/sync",
    method: "POST",
    query: { serviceType, serverId }
  });
}

export async function radarrTestConnection(serviceType: string, serverId: string): Promise<RadarrConnectionTestResponse> {
  return apiRequest<RadarrConnectionTestResponse>({
    path: "/api/v1/radarr/test-connection",
    method: "POST",
    query: { serviceType, serverId }
  });
}

export async function radarrGetSettings(serviceType: string, serverId: string): Promise<RadarrSettingsDto> {
  return apiRequest<RadarrSettingsDto>({
    path: "/api/v1/radarr/settings",
    query: { serviceType, serverId }
  });
}

export async function radarrUpsertSettings(
  serviceType: string,
  serverId: string,
  request: UpdateRadarrSettingsRequest
): Promise<RadarrSettingsDto> {
  return apiRequest<RadarrSettingsDto>({
    path: "/api/v1/radarr/settings",
    method: "PUT",
    query: { serviceType, serverId },
    body: request
  });
}

export async function radarrGetQualityProfiles(serviceType: string, serverId: string): Promise<RadarrQualityProfileDto[]> {
  return apiRequest<RadarrQualityProfileDto[]>({
    path: "/api/v1/radarr/quality-profiles",
    query: { serviceType, serverId }
  });
}

export async function radarrGetRootFolders(serviceType: string, serverId: string): Promise<RadarrRootFolderDto[]> {
  return apiRequest<RadarrRootFolderDto[]>({
    path: "/api/v1/radarr/root-folders",
    query: { serviceType, serverId }
  });
}

export async function radarrSyncLibrary(serviceType: string, serverId: string): Promise<RadarrLibrarySyncResponse> {
  return apiRequest<RadarrLibrarySyncResponse>({
    path: "/api/v1/radarr/library/sync",
    method: "POST",
    query: { serviceType, serverId }
  });
}

export async function jellyfinListServers(): Promise<JellyfinServerDto[]> {
  return apiRequest<JellyfinServerDto[]>({
    path: "/api/v1/jellyfin/servers"
  });
}

export async function jellyfinGetSettings(serviceType: string, serverId: string): Promise<JellyfinSettingsDto> {
  return apiRequest<JellyfinSettingsDto>({
    path: "/api/v1/jellyfin/settings",
    query: { serviceType, serverId }
  });
}

export async function jellyfinUpsertSettings(
  request: UpdateJellyfinSettingsRequest,
  confirmNewInstance = false
): Promise<JellyfinSettingsDto> {
  return apiRequest<JellyfinSettingsDto>({
    path: "/api/v1/jellyfin/settings",
    method: "PUT",
    query: { confirmNewInstance },
    body: request
  });
}

export async function jellyfinTestConnection(serviceType: string, serverId: string): Promise<JellyfinConnectionTestResponse> {
  return apiRequest<JellyfinConnectionTestResponse>({
    path: "/api/v1/jellyfin/test-connection",
    method: "POST",
    query: { serviceType, serverId }
  });
}

export async function jellyfinSyncLibrary(serviceType: string, serverId: string): Promise<JellyfinLibrarySyncResponse> {
  return apiRequest<JellyfinLibrarySyncResponse>({
    path: "/api/v1/jellyfin/library/sync",
    method: "POST",
    query: { serviceType, serverId }
  });
}

export async function embyListServers(): Promise<EmbyServerDto[]> {
  return apiRequest<EmbyServerDto[]>({
    path: "/api/v1/emby/servers"
  });
}

export async function embyGetSettings(serviceType: string, serverId: string): Promise<EmbySettingsDto> {
  return apiRequest<EmbySettingsDto>({
    path: "/api/v1/emby/settings",
    query: { serviceType, serverId }
  });
}

export async function embyUpsertSettings(
  request: UpdateEmbySettingsRequest,
  confirmNewInstance = false
): Promise<EmbySettingsDto> {
  return apiRequest<EmbySettingsDto>({
    path: "/api/v1/emby/settings",
    method: "PUT",
    query: { confirmNewInstance },
    body: request
  });
}

export async function embyTestConnection(serviceType: string, serverId: string): Promise<EmbyConnectionTestResponse> {
  return apiRequest<EmbyConnectionTestResponse>({
    path: "/api/v1/emby/test-connection",
    method: "POST",
    query: { serviceType, serverId }
  });
}

export async function embySyncLibrary(serviceType: string, serverId: string): Promise<EmbyLibrarySyncResponse> {
  return apiRequest<EmbyLibrarySyncResponse>({
    path: "/api/v1/emby/library/sync",
    method: "POST",
    query: { serviceType, serverId }
  });
}
