// Empty string = same-origin (use with Vite proxy in dev)
export const apiBaseUrl = (import.meta.env.VITE_API_BASE_URL ?? '').replace(/\/$/, '')
const API_BASE_URL = apiBaseUrl || ''

export const AUTH_TOKEN_KEY = 'tindarr_access_token'
export const USER_ID_KEY = 'tindarr_user_id'

function getAuthHeaders(): Record<string, string> {
  const token = typeof localStorage !== 'undefined' ? localStorage.getItem(AUTH_TOKEN_KEY) : null
  if (!token) return {}
  return { Authorization: `Bearer ${token}` }
}

export function clearSessionStorage(): void {
  try {
    localStorage.removeItem(AUTH_TOKEN_KEY)
    localStorage.removeItem(USER_ID_KEY)
  } catch {
    // ignore
  }
}

export interface HealthResponse {
  status: string
}

export interface InfoResponse {
  name: string
  version: string
}

export interface UserPreferences {
  preferredGenres: number[]
  excludedGenres: number[]
  minRating: number
  maxRating: number
  minYear?: number
  maxYear?: number
  includeAdult: boolean
  sortBy: string
  language: string
  region?: string
  originalLanguage?: string
}

/** Response from GET /api/v1/preferences (backend UserPreferencesDto). */
export interface UserPreferencesDto {
  includeAdult: boolean
  minReleaseYear: number | null
  maxReleaseYear: number | null
  minRating: number | null
  maxRating: number | null
  preferredGenres: number[]
  excludedGenres: number[]
  preferredOriginalLanguages: string[]
  excludedOriginalLanguages: string[]
  preferredRegions: string[]
  excludedRegions: string[]
  sortBy: string
  updatedAtUtc: string
}

function mapPreferencesDtoToUi(dto: UserPreferencesDto): UserPreferences {
  return {
    preferredGenres: dto.preferredGenres ?? [],
    excludedGenres: dto.excludedGenres ?? [],
    minRating: dto.minRating ?? 0,
    maxRating: dto.maxRating ?? 10,
    minYear: dto.minReleaseYear ?? undefined,
    maxYear: dto.maxReleaseYear ?? undefined,
    includeAdult: dto.includeAdult,
    sortBy: dto.sortBy || 'popularity.desc',
    language: 'en-US',
    region: dto.preferredRegions?.[0],
    originalLanguage: dto.preferredOriginalLanguages?.[0],
  }
}

export interface User {
  id: string
  username: string
  email: string
  createdAt: string
  isAdmin: boolean
  /** True if user can superlike (Admin or Curator). Mirrors backend InteractionsController. */
  canSuperlike: boolean
  /** True when logged in as room guest (no account). Used to hide Cast in room for guests. */
  isGuest: boolean
  preferences: UserPreferences
}

export interface MovieInteraction {
  id: string
  userId: string
  movieId: number
  type: 'Like' | 'Nope' | 'Skip'
  createdAt: string
}

export interface CreateUserRequest {
  username: string
  email: string
}

/** Backend expects UserId, DisplayName, Password (see Contracts.Auth.RegisterRequest). */
export interface RegisterRequest {
  userId: string
  displayName: string
  password: string
}

export interface SetupStatusResponse {
  setupComplete: boolean
}

/** Request body for POST /api/v1/setup/admin (create initial admin). */
export interface SetupAdminRequest {
  password: string
}

/** Response from GET /api/v1/setup/suggested-urls (LAN/WAN auto-detection). */
export interface SuggestedUrlsResponse {
  port: number | null
  suggestedLanHostPort: string | null
  suggestedWanHostPort: string | null
}

export interface CheckReachabilityRequest {
  baseUrl: string
}
export interface CheckReachabilityResponse {
  reachable: boolean
  message: string | null
}

export interface SetupCompleteRequest {
  runLibrarySync: boolean
  runTmdbBuild: boolean
  /** Required; true by default. Fetch-all-details runs on setup complete. */
  runFetchAllDetails: boolean
  runFetchAllImages?: boolean
}

export interface SetupCompleteResponse {
  message: string
}

export interface AuthResponse {
  accessToken: string
  expiresAtUtc: string
  userId: string
  displayName: string
  roles: string[]
  /** True when registration succeeded but user must wait for admin approval before logging in. */
  pendingApproval?: boolean
}

export interface MeResponse {
  userId: string
  displayName: string
  roles: string[]
}

/** Backend expects UserId, Password (see Contracts.Auth.LoginRequest). */
export interface LoginRequest {
  userId: string
  password: string
}

/** Backend POST /api/v1/auth/guest (Contracts.Auth.GuestLoginRequest). */
export interface GuestLoginRequest {
  roomId: string
  displayName?: string | null
}

export interface LoginResponse {
  id: string
  username: string
  email: string
  isAdmin: boolean
  needPassword?: boolean
}

export interface SetPasswordRequest {
  username: string
  currentPassword?: string
  newPassword: string
}

export interface RecordInteractionRequest {
  userId: string
  movieId: number
  type: 'Like' | 'Nope' | 'Skip'
}

/** Scope for swipe deck / interactions (serviceType + serverId). */
export interface ServiceScopeOptionDto {
  serviceType: string
  serverId: string
  displayName: string
}

/** Backend swipe card (from /api/v1/tmdb/discover or /api/v1/swipedeck). */
export interface SwipeCardDto {
  tmdbId: number
  title: string
  overview: string | null
  posterUrl: string | null
  backdropUrl: string | null
  releaseYear: number | null
  rating: number | null
}

/** Backend discover/swipedeck response. */
export interface SwipeDeckResponse {
  serviceType: string
  serverId: string
  items: SwipeCardDto[]
}

/** Backend GET /api/v1/tmdb/movies/{tmdbId} (Contracts.Movies.MovieDetailsDto). */
export interface MovieDetailsDto {
  tmdbId: number
  title: string
  overview: string | null
  posterUrl: string | null
  backdropUrl: string | null
  releaseDate: string | null
  releaseYear: number | null
  mpaaRating: string | null
  rating: number | null
  voteCount: number | null
  genres: string[]
  regions: string[]
  originalLanguage: string | null
  runtimeMinutes: number | null
}

/** Backend GET /api/v1/search/movies (Contracts.Search.SearchMovieResultDto). */
export interface SearchMovieResultDto {
  tmdbId: number
  title: string
  releaseYear: number | null
  posterUrl: string | null
  backdropUrl: string | null
}

/** Backend POST /api/v1/interactions body. */
export interface SwipeRequest {
  tmdbId: number
  action: 'Like' | 'Nope' | 'Skip' | 'Superlike'
  serviceType: string
  serverId: string
}

export interface InteractionDto {
  tmdbId: number
  action: 'Like' | 'Nope' | 'Skip' | 'Superlike'
  createdAtUtc: string
}

export interface InteractionListResponse {
  serviceType: string
  serverId: string
  items: InteractionDto[]
}

/** Backend POST /api/v1/interactions/undo response. */
export interface UndoResponse {
  undone: boolean
  tmdbId: number | null
  action: 'Like' | 'Nope' | 'Skip' | 'Superlike' | null
  createdAtUtc: string | null
}

/** Backend GET /api/v1/matches (Contracts.Matching.MatchDto). */
export interface MatchDto {
  tmdbId: number
  matchedWithDisplayNames: string[]
}

/** Backend GET /api/v1/matches response (Contracts.Matching.MatchesResponse). */
export interface MatchesResponse {
  serviceType: string
  serverId: string
  items: MatchDto[]
}

// --- Rooms ---
export interface RoomMemberDto {
  userId: string
  joinedAtUtc: string
}
export interface CreateRoomRequest {
  serviceType: string
  serverId: string
  roomName?: string | null
}
export interface CreateRoomResponse {
  roomId: string
  ownerUserId: string
  serviceType: string
  serverId: string
  members: RoomMemberDto[]
}
export interface RoomListItemDto {
  roomId: string
  ownerUserId: string
  serviceType: string
  serverId: string
  isClosed: boolean
  createdAtUtc: string
  lastActivityAtUtc: string
  memberCount: number
}
export interface RoomStateResponse {
  roomId: string
  ownerUserId: string
  serviceType: string
  serverId: string
  isClosed: boolean
  createdAtUtc: string
  lastActivityAtUtc: string
  members: RoomMemberDto[]
}
export interface RoomJoinUrlResponse {
  url: string
  lanUrl?: string | null
  wanUrl?: string | null
}
export interface RoomMatchesResponse {
  roomId: string
  serviceType: string
  serverId: string
  tmdbIds: number[]
}
export interface RoomSwipeRequest {
  tmdbId: number
  action: 'Like' | 'Nope' | 'Skip' | 'Superlike'
}
export interface RoomSwipeResponse {
  tmdbId: number
  action: string
  createdAtUtc: string
}
export interface SwipeDeckResponseRoom {
  serviceType: string
  serverId: string
  items: SwipeCardDto[]
}

/** @deprecated Use SwipeCardDto / SwipeDeckResponse and getDiscoverCards. */
export interface BackendDiscoverMovie {
  id: number
  title?: string
  overview?: string
  poster_path?: string | null
  backdrop_path?: string | null
  vote_average: number
  original_language?: string
  release_date?: string
}

/** @deprecated Use SwipeDeckResponse. */
export interface BackendDiscoverResponse {
  page: number
  results: BackendDiscoverMovie[]
  total_pages: number
  total_results: number
}

class ApiClient {
  private baseUrl: string

  constructor(baseUrl: string) {
    this.baseUrl = baseUrl
  }

  private async errorMessageFromResponse(response: Response): Promise<string> {
    const text = await response.text().catch(() => '')
    if (!text) return response.statusText
    try {
      const j = JSON.parse(text) as { message?: string }
      if (typeof j?.message === 'string' && j.message.trim()) return j.message
    } catch {
      // not JSON or no message field
    }
    return text.length > 200 ? `${text.slice(0, 200)}…` : text
  }

  private async request<T>(endpoint: string): Promise<T> {
    const response = await fetch(`${this.baseUrl}${endpoint}`, {
      headers: getAuthHeaders(),
    })

    if (response.status === 401) {
      clearSessionStorage()
      throw new Error('Unauthorized')
    }
    if (!response.ok) {
      const msg = await this.errorMessageFromResponse(response)
      throw new Error(msg)
    }

    return response.json() as Promise<T>
  }

  private async post<T>(endpoint: string, data: unknown): Promise<T> {
    const response = await fetch(`${this.baseUrl}${endpoint}`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        ...getAuthHeaders(),
      },
      body: JSON.stringify(data),
    })

    if (response.status === 401) {
      clearSessionStorage()
      throw new Error('Unauthorized')
    }
    if (!response.ok) {
      const msg = await this.errorMessageFromResponse(response)
      throw new Error(msg)
    }

    if (response.status === 204 || response.headers.get('content-length') === '0') {
      return {} as T
    }

    return response.json() as Promise<T>
  }

  private async delete(endpoint: string): Promise<void> {
    const response = await fetch(`${this.baseUrl}${endpoint}`, {
      method: 'DELETE',
      headers: getAuthHeaders(),
    })

    if (response.status === 401) {
      clearSessionStorage()
      throw new Error('Unauthorized')
    }
    if (!response.ok) {
      const msg = await this.errorMessageFromResponse(response)
      throw new Error(msg)
    }
  }

  private async put<T>(endpoint: string, data: unknown): Promise<T> {
    const response = await fetch(`${this.baseUrl}${endpoint}`, {
      method: 'PUT',
      headers: {
        'Content-Type': 'application/json',
        ...getAuthHeaders(),
      },
      body: JSON.stringify(data),
    })

    if (response.status === 401) {
      clearSessionStorage()
      throw new Error('Unauthorized')
    }
    if (!response.ok) {
      const errorText = await response.text().catch(() => 'Unknown error')
      throw new Error(`API request failed: ${response.status} - ${errorText}`)
    }

    if (response.status === 204 || response.headers.get('content-length') === '0') {
      return {} as T
    }

    return response.json() as Promise<T>
  }

  async getHealth(): Promise<HealthResponse> {
    return this.request<HealthResponse>('/api/v1/health')
  }

  async getInfo(): Promise<InfoResponse> {
    return this.request<InfoResponse>('/api/v1/info')
  }

  async getSetupStatus(): Promise<SetupStatusResponse> {
    return this.request<SetupStatusResponse>('/api/v1/setup/status')
  }

  /** Create initial admin user (no users must exist). Returns auth response. */
  async createInitialAdmin(request: SetupAdminRequest): Promise<AuthResponse> {
    return this.post<AuthResponse>('/api/v1/setup/admin', request)
  }

  /** Admin: get suggested LAN/WAN host:port (from NIC / ipify etc.). */
  async getSuggestedUrls(): Promise<SuggestedUrlsResponse> {
    return this.request<SuggestedUrlsResponse>('/api/v1/setup/suggested-urls')
  }

  /** Admin: check if a base URL is reachable (GET). Used by setup wizard "Try default". */
  async checkReachability(request: CheckReachabilityRequest): Promise<CheckReachabilityResponse> {
    return this.post<CheckReachabilityResponse>('/api/v1/setup/check-reachability', request)
  }

  /** Admin: trigger DB populate (fetch all details and/or images for TMDB cache). */
  async setupComplete(request: SetupCompleteRequest): Promise<SetupCompleteResponse> {
    return this.post<SetupCompleteResponse>('/api/v1/setup/complete', request)
  }

  async createUser(request: CreateUserRequest): Promise<User> {
    return this.post<User>('/api/v1/users', request)
  }

  async register(request: RegisterRequest): Promise<AuthResponse> {
    return this.post<AuthResponse>('/api/v1/auth/register', request)
  }

  async login(request: LoginRequest): Promise<AuthResponse> {
    return this.post<AuthResponse>('/api/v1/auth/login', request)
  }

  async guestLogin(request: GuestLoginRequest): Promise<AuthResponse> {
    return this.post<AuthResponse>('/api/v1/auth/guest', request)
  }

  async getMe(): Promise<MeResponse> {
    return this.request<MeResponse>('/api/v1/auth/me')
  }

  async setPassword(request: SetPasswordRequest): Promise<{ success: boolean }> {
    return this.post<{ success: boolean }>('/api/v1/auth/set-password', request)
  }

  async getUser(userId: string): Promise<User> {
    return this.request<User>(`/api/v1/users/${userId}`)
  }

  async getUserByUsername(username: string): Promise<User> {
    return this.request<User>(`/api/v1/users/username/${username}`)
  }

  /** GET /api/v1/preferences — current user from JWT. */
  async getPreferences(): Promise<UserPreferences> {
    const dto = await this.request<UserPreferencesDto>('/api/v1/preferences')
    return mapPreferencesDtoToUi(dto)
  }

  /** PUT /api/v1/preferences — current user from JWT. */
  async updateUserPreferences(preferences: UserPreferences): Promise<void> {
    const body = {
      includeAdult: preferences.includeAdult,
      minReleaseYear: preferences.minYear ?? null,
      maxReleaseYear: preferences.maxYear ?? null,
      minRating: preferences.minRating,
      maxRating: preferences.maxRating,
      preferredGenres: preferences.preferredGenres,
      excludedGenres: preferences.excludedGenres,
      preferredOriginalLanguages: preferences.originalLanguage ? [preferences.originalLanguage] : [],
      excludedOriginalLanguages: [] as string[],
      preferredRegions: preferences.region ? [preferences.region] : [],
      excludedRegions: [] as string[],
      sortBy: preferences.sortBy,
    }
    await this.put<UserPreferencesDto>('/api/v1/preferences', body)
  }

  /** List configured scopes (TMDB + Plex/Jellyfin/Emby servers) for swipe deck. */
  async getScopes(): Promise<ServiceScopeOptionDto[]> {
    return this.request<ServiceScopeOptionDto[]>('/api/v1/scopes')
  }

  /** Get swipe deck cards from TMDB discover (scope required). User from JWT. */
  async getDiscoverCards(
    serviceType: string,
    serverId: string,
    limit: number = 25
  ): Promise<SwipeDeckResponse> {
    const params = new URLSearchParams({
      serviceType,
      serverId,
      limit: String(Math.min(50, Math.max(1, limit))),
    })
    return this.request<SwipeDeckResponse>(`/api/v1/tmdb/discover?${params}`)
  }

  /** Get TMDB movie details by id (for likes/matches display). */
  async getMovieDetails(tmdbId: number): Promise<MovieDetailsDto> {
    return this.request<MovieDetailsDto>(`/api/v1/tmdb/movies/${tmdbId}`)
  }

  /** Search movies by title. Uses Radarr passthrough if configured, else local cache + TMDB API. */
  async searchMovies(query: string): Promise<SearchMovieResultDto[]> {
    const q = (query ?? '').trim()
    if (q === '') return []
    const params = new URLSearchParams({ q })
    return this.request<SearchMovieResultDto[]>(`/api/v1/search/movies?${params}`)
  }

  /** Record a swipe (Like/Nope/Skip/Superlike). User from JWT. */
  async recordSwipe(
    serviceType: string,
    serverId: string,
    tmdbId: number,
    action: 'Like' | 'Nope' | 'Skip' | 'Superlike'
  ): Promise<void> {
    const request: SwipeRequest = { tmdbId, action, serviceType, serverId }
    return this.post<void>('/api/v1/interactions', request)
  }

  /** Undo last swipe for the current user in the given scope. Returns undone + tmdbId/action if undone. */
  async undoLastSwipe(serviceType: string, serverId: string): Promise<UndoResponse> {
    const params = new URLSearchParams({ serviceType, serverId })
    return this.post<UndoResponse>(`/api/v1/interactions/undo?${params}`, {})
  }

  /** List interactions for a scope, optionally filtered by action. */
  async listInteractions(
    serviceType: string,
    serverId: string,
    action?: 'Like' | 'Nope' | 'Skip' | 'Superlike',
    limit: number = 200
  ): Promise<InteractionListResponse> {
    const params = new URLSearchParams({ serviceType, serverId, limit: String(limit) })
    if (action !== undefined) params.set('action', action)
    return this.request<InteractionListResponse>(`/api/v1/interactions?${params}`)
  }

  /** List matched movies (TMDB scope: consensus; media-server scope: your likes) with who else liked each. */
  async listMatches(
    serviceType: string,
    serverId: string,
    minUsers: number = 2,
    interactionLimit: number = 20_000
  ): Promise<MatchesResponse> {
    const params = new URLSearchParams({
      serviceType,
      serverId,
      minUsers: String(minUsers),
      interactionLimit: String(interactionLimit),
    })
    return this.request<MatchesResponse>(`/api/v1/matches?${params}`)
  }

  // --- Rooms ---
  /** Lists rooms: open rooms only for registered users, all alive rooms for admins. */
  async listRooms(): Promise<RoomListItemDto[]> {
    return this.request<RoomListItemDto[]>('/api/v1/rooms')
  }
  async createRoom(request: CreateRoomRequest): Promise<CreateRoomResponse> {
    return this.post<CreateRoomResponse>('/api/v1/rooms', request)
  }
  async joinRoom(roomId: string): Promise<RoomStateResponse> {
    return this.post<RoomStateResponse>(`/api/v1/rooms/${encodeURIComponent(roomId)}/join`, {})
  }
  async getRoom(roomId: string): Promise<RoomStateResponse> {
    return this.request<RoomStateResponse>(`/api/v1/rooms/${encodeURIComponent(roomId)}`)
  }
  async closeRoom(roomId: string): Promise<RoomStateResponse> {
    return this.post<RoomStateResponse>(`/api/v1/rooms/${encodeURIComponent(roomId)}/close`, {})
  }
  async getRoomJoinUrl(roomId: string): Promise<RoomJoinUrlResponse> {
    return this.request<RoomJoinUrlResponse>(`/api/v1/rooms/${encodeURIComponent(roomId)}/join-url`)
  }
  async roomSwipe(roomId: string, request: RoomSwipeRequest): Promise<RoomSwipeResponse> {
    return this.post<RoomSwipeResponse>(`/api/v1/rooms/${encodeURIComponent(roomId)}/swipe`, request)
  }
  async getRoomSwipeDeck(roomId: string, limit = 25): Promise<SwipeDeckResponseRoom> {
    const params = new URLSearchParams({ limit: String(Math.min(50, Math.max(10, limit))) })
    return this.request<SwipeDeckResponseRoom>(
      `/api/v1/rooms/${encodeURIComponent(roomId)}/swipedeck?${params}`
    )
  }
  async getRoomMatches(roomId: string): Promise<RoomMatchesResponse> {
    return this.request<RoomMatchesResponse>(`/api/v1/rooms/${encodeURIComponent(roomId)}/matches`)
  }

  // --- Casting (rooms) ---
  async listCastDevices(): Promise<CastDeviceDto[]> {
    return this.request<CastDeviceDto[]>('/api/v1/casting/devices')
  }
  async getRoomQrCastUrl(roomId: string, variant?: 'lan' | 'wan'): Promise<CastMediaUrlDto> {
    const q = variant ? `?variant=${variant}` : ''
    return this.request<CastMediaUrlDto>(
      `/api/v1/casting/rooms/${encodeURIComponent(roomId)}/qr/cast-url${q}`
    )
  }
  async castRoomQr(
    roomId: string,
    deviceId: string,
    variant?: 'lan' | 'wan'
  ): Promise<void> {
    await this.post<void>(`/api/v1/casting/rooms/${encodeURIComponent(roomId)}/qr`, {
      deviceId,
      variant: variant ?? null,
    })
  }

  /** Cast a movie to a device (media-server scope only). POST /api/v1/casting/movie */
  async castMovie(
    deviceId: string,
    serviceType: string,
    serverId: string,
    tmdbId: number,
    title?: string | null
  ): Promise<void> {
    await this.post<void>('/api/v1/casting/movie', {
      deviceId,
      serviceType,
      serverId,
      tmdbId,
      title: title ?? null,
    })
  }

  async recordInteraction(request: RecordInteractionRequest): Promise<void> {
    return this.post<void>('/api/v1/interactions', request)
  }

  async getUserInteractions(userId: string): Promise<MovieInteraction[]> {
    return this.request<MovieInteraction[]>(`/api/v1/interactions/${userId}`)
  }

  async getUserLikedMovies(userId: string): Promise<{ movieIds: number[] }> {
    return this.request<{ movieIds: number[] }>(`/api/v1/interactions/${userId}/liked`)
  }

  async getUserNopedMovies(userId: string): Promise<{ movieIds: number[] }> {
    return this.request<{ movieIds: number[] }>(`/api/v1/interactions/${userId}/noped`)
  }

  /** @deprecated Use getDiscoverCards(serviceType, serverId, limit) with scope. */
  async discoverMovies(userId: string, page: number): Promise<BackendDiscoverResponse> {
    return this.request<BackendDiscoverResponse>(
      `/api/v1/discover?userId=${userId}&page=${page}`
    )
  }

  /** Admin stats (users, interactions, accepted movies). Requires admin auth. */
  async getAdminStats(): Promise<AdminStatsResponse> {
    return this.request<AdminStatsResponse>('/api/v1/admin/stats')
  }
  async getAdminUpdateCheck(): Promise<AdminUpdateCheckResponse> {
    return this.request<AdminUpdateCheckResponse>('/api/v1/admin/update')
  }
  async getRegistrationSettings(): Promise<RegistrationSettingsDto> {
    return this.request<RegistrationSettingsDto>('/api/v1/admin/registration')
  }
  async updateRegistrationSettings(request: UpdateRegistrationSettingsRequest): Promise<RegistrationSettingsDto> {
    return this.put<RegistrationSettingsDto>('/api/v1/admin/registration', request)
  }

  // --- Admin (Tindarr) ---
  async listAdminUsers(skip = 0, take = 100): Promise<AdminUserDto[]> {
    return this.request<AdminUserDto[]>(`/api/v1/admin/users?skip=${skip}&take=${take}`)
  }
  async getAdminUser(userId: string): Promise<AdminUserDto> {
    return this.request<AdminUserDto>(`/api/v1/admin/users/${encodeURIComponent(userId)}`)
  }
  async createAdminUser(request: AdminCreateUserRequest): Promise<AdminUserDto> {
    return this.post<AdminUserDto>('/api/v1/admin/users', request)
  }
  async updateAdminUser(userId: string, request: AdminUpdateUserRequest): Promise<void> {
    return this.put<void>(`/api/v1/admin/users/${encodeURIComponent(userId)}`, request)
  }
  async deleteAdminUser(userId: string): Promise<void> {
    return this.delete(`/api/v1/admin/users/${encodeURIComponent(userId)}`)
  }
  async setAdminUserRoles(userId: string, request: AdminSetUserRolesRequest): Promise<void> {
    return this.post<void>(`/api/v1/admin/users/${encodeURIComponent(userId)}/roles`, request)
  }
  async adminSetPassword(userId: string, request: AdminSetPasswordRequest): Promise<void> {
    return this.post<void>(`/api/v1/admin/users/${encodeURIComponent(userId)}/set-password`, request)
  }
  async getJoinAddressSettings(): Promise<JoinAddressSettingsDto> {
    return this.request<JoinAddressSettingsDto>('/api/v1/admin/join-address')
  }
  async updateJoinAddressSettings(request: UpdateJoinAddressSettingsRequest): Promise<JoinAddressSettingsDto> {
    return this.put<JoinAddressSettingsDto>('/api/v1/admin/join-address', request)
  }
  async getCastingSettings(): Promise<CastingSettingsDto> {
    return this.request<CastingSettingsDto>('/api/v1/admin/casting')
  }
  async updateCastingSettings(request: UpdateCastingSettingsRequest): Promise<CastingSettingsDto> {
    return this.put<CastingSettingsDto>('/api/v1/admin/casting', request)
  }
  async getAdvancedSettings(): Promise<AdvancedSettingsDto> {
    return this.request<AdvancedSettingsDto>('/api/v1/admin/advanced-settings')
  }
  async updateAdvancedSettings(request: UpdateAdvancedSettingsRequest): Promise<AdvancedSettingsDto> {
    return this.put<AdvancedSettingsDto>('/api/v1/admin/advanced-settings', request)
  }
  async getCastingDiagnostics(): Promise<CastingDiagnosticsDto> {
    return this.request<CastingDiagnosticsDto>('/api/v1/admin/casting/diagnostics')
  }
  async getConsoleOutput(maxLines = 500): Promise<ConsoleOutputDto> {
    return this.request<ConsoleOutputDto>(`/api/v1/admin/console?maxLines=${Math.max(1, Math.min(2000, maxLines))}`)
  }
  async getPopulateStatus(): Promise<PopulateStatusDto> {
    return this.request<PopulateStatusDto>('/api/v1/admin/db/populate-status')
  }
  async getAdminDbMovies(serviceType: string, serverId: string, skip = 0, take = 50): Promise<AdminDbMovieListResponse> {
    return this.request<AdminDbMovieListResponse>(
      `/api/v1/admin/db/movies?serviceType=${encodeURIComponent(serviceType)}&serverId=${encodeURIComponent(serverId)}&skip=${skip}&take=${take}`
    )
  }

  async searchAdminInteractions(request: {
    userId?: string
    serviceType?: string
    serverId?: string
    action?: 'Like' | 'Nope' | 'Skip' | 'Superlike'
    tmdbId?: number
    sinceUtc?: string
    limit?: number
  }): Promise<AdminInteractionSearchResponse> {
    const params = new URLSearchParams()
    if (request.userId) params.set('userId', request.userId)
    if (request.serviceType) params.set('serviceType', request.serviceType)
    if (request.serverId) params.set('serverId', request.serverId)
    if (request.action) params.set('action', request.action)
    if (request.tmdbId != null) params.set('tmdbId', String(request.tmdbId))
    if (request.sinceUtc) params.set('sinceUtc', request.sinceUtc)
    params.set('limit', String(Math.max(1, Math.min(5000, request.limit ?? 200))))
    return this.request<AdminInteractionSearchResponse>(`/api/v1/admin/interactions?${params}`)
  }

  async deleteAdminInteraction(id: number): Promise<void> {
    await this.delete(`/api/v1/admin/interactions/${id}`)
  }

  async deleteAdminInteractions(ids: number[]): Promise<void> {
    await this.post('/api/v1/admin/interactions/delete', { ids })
  }

  // Radarr (admin): all endpoints require query params serviceType=radarr & serverId
  private radarrParams(serviceType: string, serverId: string): string {
    return `serviceType=${encodeURIComponent(serviceType)}&serverId=${encodeURIComponent(serverId)}`
  }

  async getRadarrSettings(serviceType: string, serverId: string): Promise<RadarrSettingsDto> {
    return this.request<RadarrSettingsDto>(`/api/v1/radarr/settings?${this.radarrParams(serviceType, serverId)}`)
  }

  async putRadarrSettings(
    serviceType: string,
    serverId: string,
    request: UpdateRadarrSettingsRequest
  ): Promise<RadarrSettingsDto> {
    return this.put<RadarrSettingsDto>(`/api/v1/radarr/settings?${this.radarrParams(serviceType, serverId)}`, request)
  }

  async postRadarrTestConnection(serviceType: string, serverId: string): Promise<RadarrConnectionTestResponse> {
    const response = await fetch(`${this.baseUrl}/api/v1/radarr/test-connection?${this.radarrParams(serviceType, serverId)}`, {
      method: 'POST',
      headers: getAuthHeaders(),
    })
    if (response.status === 401) {
      clearSessionStorage()
      throw new Error('Unauthorized')
    }
    if (!response.ok) {
      const text = await response.text().catch(() => 'Unknown error')
      throw new Error(`Test connection failed: ${response.status} ${text}`)
    }
    return response.json() as Promise<RadarrConnectionTestResponse>
  }

  async getRadarrQualityProfiles(serviceType: string, serverId: string): Promise<RadarrQualityProfileDto[]> {
    return this.request<RadarrQualityProfileDto[]>(
      `/api/v1/radarr/quality-profiles?${this.radarrParams(serviceType, serverId)}`
    )
  }

  async getRadarrRootFolders(serviceType: string, serverId: string): Promise<RadarrRootFolderDto[]> {
    return this.request<RadarrRootFolderDto[]>(
      `/api/v1/radarr/root-folders?${this.radarrParams(serviceType, serverId)}`
    )
  }

  async postRadarrAutoAddAcceptedMovies(serviceType: string, serverId: string): Promise<RadarrAutoAddResponse> {
    const response = await fetch(
      `${this.baseUrl}/api/v1/radarr/auto-add-accepted-movies?${this.radarrParams(serviceType, serverId)}`,
      { method: 'POST', headers: getAuthHeaders() }
    )
    if (response.status === 401) {
      clearSessionStorage()
      throw new Error('Unauthorized')
    }
    if (!response.ok) throw new Error(`Auto-add failed: ${response.statusText}`)
    return response.json() as Promise<RadarrAutoAddResponse>
  }

  async postRadarrSyncLibrary(serviceType: string, serverId: string): Promise<RadarrLibrarySyncResponse> {
    const response = await fetch(
      `${this.baseUrl}/api/v1/radarr/library/sync?${this.radarrParams(serviceType, serverId)}`,
      { method: 'POST', headers: getAuthHeaders() }
    )
    if (response.status === 401) {
      clearSessionStorage()
      throw new Error('Unauthorized')
    }
    if (!response.ok) {
      const text = await response.text().catch(() => 'Unknown error')
      throw new Error(`Sync failed: ${response.status} ${text}`)
    }
    return response.json() as Promise<RadarrLibrarySyncResponse>
  }

  // Admin matching settings (per scope, e.g. radarr + serverId)
  async getMatchSettings(serviceType: string, serverId: string): Promise<MatchSettingsDto> {
    return this.request<MatchSettingsDto>(
      `/api/v1/admin/matching?serviceType=${encodeURIComponent(serviceType)}&serverId=${encodeURIComponent(serverId)}`
    )
  }

  async putMatchSettings(
    serviceType: string,
    serverId: string,
    request: UpdateMatchSettingsRequest
  ): Promise<MatchSettingsDto> {
    return this.put<MatchSettingsDto>(
      `/api/v1/admin/matching?serviceType=${encodeURIComponent(serviceType)}&serverId=${encodeURIComponent(serverId)}`,
      request
    )
  }

  // TMDB (admin): cache settings and build
  async getTmdbCacheSettings(): Promise<TmdbCacheSettingsDto> {
    return this.request<TmdbCacheSettingsDto>('/api/v1/tmdb/cache/settings')
  }

  async putTmdbCacheSettings(request: UpdateTmdbCacheSettingsRequest): Promise<TmdbCacheSettingsDto> {
    return this.put<TmdbCacheSettingsDto>('/api/v1/tmdb/cache/settings', request)
  }

  async getTmdbBuildStatus(): Promise<TmdbBuildStatusDto> {
    return this.request<TmdbBuildStatusDto>('/api/v1/tmdb/build/status')
  }

  async postTmdbBuildStart(request: StartTmdbBuildRequest): Promise<TmdbBuildStatusDto> {
    return this.post<TmdbBuildStatusDto>('/api/v1/tmdb/build/start', request)
  }

  async postTmdbBuildCancel(reason?: string): Promise<TmdbBuildStatusDto> {
    const url = reason != null ? `/api/v1/tmdb/build/cancel?reason=${encodeURIComponent(reason)}` : '/api/v1/tmdb/build/cancel'
    return this.post<TmdbBuildStatusDto>(url, {})
  }

  async getTmdbBackupDownload(): Promise<Blob> {
    const response = await fetch(`${this.baseUrl}/api/v1/tmdb/backup/download`, {
      method: 'GET',
      headers: getAuthHeaders(),
    })
    if (response.status === 401) {
      clearSessionStorage()
      throw new Error('Unauthorized')
    }
    if (!response.ok) {
      const text = await response.text().catch(() => 'Unknown error')
      throw new Error(text || `Download failed: ${response.status}`)
    }
    return response.blob()
  }

  async postTmdbRestore(file: File): Promise<TmdbRestoreResultDto> {
    const formData = new FormData()
    formData.append('file', file)
    const response = await fetch(`${this.baseUrl}/api/v1/tmdb/restore`, {
      method: 'POST',
      headers: getAuthHeaders(),
      body: formData,
    })
    if (response.status === 401) {
      clearSessionStorage()
      throw new Error('Unauthorized')
    }
    if (!response.ok) {
      const text = await response.text().catch(() => 'Unknown error')
      throw new Error(text || `Restore failed: ${response.status}`)
    }
    return response.json()
  }

  // --- Admin backup & restore (master + per-service) ---
  async getMasterBackupDownload(): Promise<Blob> {
    const response = await fetch(`${this.baseUrl}/api/v1/admin/backup/master`, {
      method: 'GET',
      headers: getAuthHeaders(),
    })
    if (response.status === 401) {
      clearSessionStorage()
      throw new Error('Unauthorized')
    }
    if (!response.ok) {
      const text = await response.text().catch(() => 'Unknown error')
      throw new Error(text || `Download failed: ${response.status}`)
    }
    return response.blob()
  }
  async getMainBackupDownload(): Promise<Blob> {
    const response = await fetch(`${this.baseUrl}/api/v1/admin/backup/main`, {
      method: 'GET',
      headers: getAuthHeaders(),
    })
    if (response.status === 401) {
      clearSessionStorage()
      throw new Error('Unauthorized')
    }
    if (response.status === 404) throw new Error('Main database not found')
    if (!response.ok) {
      const text = await response.text().catch(() => 'Unknown error')
      throw new Error(text || `Download failed: ${response.status}`)
    }
    return response.blob()
  }
  async getPlexBackupDownload(): Promise<Blob> {
    const response = await fetch(`${this.baseUrl}/api/v1/admin/backup/plex`, {
      method: 'GET',
      headers: getAuthHeaders(),
    })
    if (response.status === 401) {
      clearSessionStorage()
      throw new Error('Unauthorized')
    }
    if (response.status === 404) throw new Error('Plex cache database not found')
    if (!response.ok) {
      const text = await response.text().catch(() => 'Unknown error')
      throw new Error(text || `Download failed: ${response.status}`)
    }
    return response.blob()
  }
  async postPlexRestore(file: File): Promise<{ message?: string }> {
    const formData = new FormData()
    formData.append('file', file)
    const response = await fetch(`${this.baseUrl}/api/v1/admin/backup/plex/restore`, {
      method: 'POST',
      headers: getAuthHeaders(),
      body: formData,
    })
    if (response.status === 401) {
      clearSessionStorage()
      throw new Error('Unauthorized')
    }
    if (!response.ok) {
      const text = await response.text().catch(() => 'Unknown error')
      throw new Error(text || `Restore failed: ${response.status}`)
    }
    return response.json()
  }
  async getJellyfinBackupDownload(): Promise<Blob> {
    const response = await fetch(`${this.baseUrl}/api/v1/admin/backup/jellyfin`, {
      method: 'GET',
      headers: getAuthHeaders(),
    })
    if (response.status === 401) {
      clearSessionStorage()
      throw new Error('Unauthorized')
    }
    if (response.status === 404) throw new Error('Jellyfin cache database not found')
    if (!response.ok) {
      const text = await response.text().catch(() => 'Unknown error')
      throw new Error(text || `Download failed: ${response.status}`)
    }
    return response.blob()
  }
  async postJellyfinRestore(file: File): Promise<{ message?: string }> {
    const formData = new FormData()
    formData.append('file', file)
    const response = await fetch(`${this.baseUrl}/api/v1/admin/backup/jellyfin/restore`, {
      method: 'POST',
      headers: getAuthHeaders(),
      body: formData,
    })
    if (response.status === 401) {
      clearSessionStorage()
      throw new Error('Unauthorized')
    }
    if (!response.ok) {
      const text = await response.text().catch(() => 'Unknown error')
      throw new Error(text || `Restore failed: ${response.status}`)
    }
    return response.json()
  }
  async getEmbyBackupDownload(): Promise<Blob> {
    const response = await fetch(`${this.baseUrl}/api/v1/admin/backup/emby`, {
      method: 'GET',
      headers: getAuthHeaders(),
    })
    if (response.status === 401) {
      clearSessionStorage()
      throw new Error('Unauthorized')
    }
    if (response.status === 404) throw new Error('Emby cache database not found')
    if (!response.ok) {
      const text = await response.text().catch(() => 'Unknown error')
      throw new Error(text || `Download failed: ${response.status}`)
    }
    return response.blob()
  }
  async postEmbyRestore(file: File): Promise<{ message?: string }> {
    const formData = new FormData()
    formData.append('file', file)
    const response = await fetch(`${this.baseUrl}/api/v1/admin/backup/emby/restore`, {
      method: 'POST',
      headers: getAuthHeaders(),
      body: formData,
    })
    if (response.status === 401) {
      clearSessionStorage()
      throw new Error('Unauthorized')
    }
    if (!response.ok) {
      const text = await response.text().catch(() => 'Unknown error')
      throw new Error(text || `Restore failed: ${response.status}`)
    }
    return response.json()
  }

  // --- Plex (admin) ---
  async getPlexAuthStatus(): Promise<PlexAuthStatusResponse> {
    return this.request<PlexAuthStatusResponse>('/api/v1/plex/auth/status')
  }
  async createPlexPin(): Promise<PlexPinCreateResponse> {
    return this.post<PlexPinCreateResponse>('/api/v1/plex/pin', {})
  }
  async verifyPlexPin(pinId: number): Promise<PlexPinStatusResponse> {
    return this.post<PlexPinStatusResponse>(`/api/v1/plex/pins/${pinId}/verify`, {})
  }
  async listPlexServers(): Promise<PlexServerDto[]> {
    return this.request<PlexServerDto[]>('/api/v1/plex/servers')
  }
  async syncPlexServers(): Promise<PlexServerDto[]> {
    return this.post<PlexServerDto[]>('/api/v1/plex/servers/sync', {})
  }
  async deletePlexServer(serverId: string): Promise<void> {
    const response = await fetch(`${this.baseUrl}/api/v1/plex/servers/${encodeURIComponent(serverId)}`, {
      method: 'DELETE',
      headers: getAuthHeaders(),
    })
    if (response.status === 401) {
      clearSessionStorage()
      throw new Error('Unauthorized')
    }
    if (!response.ok) {
      const text = await response.text().catch(() => 'Unknown error')
      throw new Error(`Delete failed: ${response.status} ${text}`)
    }
  }
  async startPlexLibrarySync(serviceType: string, serverId: string): Promise<PlexLibrarySyncStatusDto> {
    const params = `serviceType=${encodeURIComponent(serviceType)}&serverId=${encodeURIComponent(serverId)}`
    return this.post<PlexLibrarySyncStatusDto>(`/api/v1/plex/library/sync/async?${params}`, {})
  }
  async getPlexLibrarySyncStatus(serviceType: string, serverId: string): Promise<PlexLibrarySyncStatusDto> {
    const params = `serviceType=${encodeURIComponent(serviceType)}&serverId=${encodeURIComponent(serverId)}`
    return this.request<PlexLibrarySyncStatusDto>(`/api/v1/plex/library/sync/status?${params}`)
  }

  /**
   * Subscribe to live Plex library sync status (SSE). Call the returned function to unsubscribe.
   */
  subscribePlexLibrarySyncStatus(
    serviceType: string,
    serverId: string,
    onStatus: (status: PlexLibrarySyncStatusDto) => void
  ): () => void {
    const params = `serviceType=${encodeURIComponent(serviceType)}&serverId=${encodeURIComponent(serverId)}`
    const url = `${this.baseUrl}/api/v1/plex/library/sync/status/stream?${params}`
    const controller = new AbortController()
    let buffer = ''
    fetch(url, { headers: getAuthHeaders(), signal: controller.signal })
      .then((res) => {
        if (!res.ok || !res.body) return
        const reader = res.body.getReader()
        const decoder = new TextDecoder()
        const processChunk = (): Promise<void> =>
          reader.read().then(({ done, value }) => {
            if (done) return
            buffer += decoder.decode(value, { stream: true })
            const blocks = buffer.split('\n\n')
            buffer = blocks.pop() ?? ''
            for (const block of blocks) {
              let event = ''
              let data = ''
              for (const line of block.split('\n')) {
                if (line.startsWith('event: ')) event = line.slice(7).trim()
                else if (line.startsWith('data: ')) data = line.slice(6).trim()
              }
              if (event === 'status' && data) {
                try {
                  onStatus(JSON.parse(data) as PlexLibrarySyncStatusDto)
                } catch {
                  // ignore parse errors
                }
              }
            }
            return processChunk()
          })
        return processChunk()
      })
      .catch(() => {})
    return () => controller.abort()
  }

  // --- Jellyfin (admin) ---
  private jellyfinEmbyParams(serviceType: string, serverId: string): string {
    return `serviceType=${encodeURIComponent(serviceType)}&serverId=${encodeURIComponent(serverId)}`
  }
  async listJellyfinServers(): Promise<JellyfinServerDto[]> {
    return this.request<JellyfinServerDto[]>('/api/v1/jellyfin/servers')
  }
  async getJellyfinSettings(serviceType: string, serverId: string): Promise<JellyfinSettingsDto> {
    return this.request<JellyfinSettingsDto>(`/api/v1/jellyfin/settings?${this.jellyfinEmbyParams(serviceType, serverId)}`)
  }
  async putJellyfinSettings(
    request: UpdateJellyfinSettingsRequest,
    confirmNewInstance = false
  ): Promise<JellyfinSettingsDto> {
    const q = confirmNewInstance ? '?confirmNewInstance=true' : ''
    return this.put<JellyfinSettingsDto>(`/api/v1/jellyfin/settings${q}`, request)
  }
  async postJellyfinTestConnection(serviceType: string, serverId: string): Promise<JellyfinConnectionTestResponse> {
    const response = await fetch(
      `${this.baseUrl}/api/v1/jellyfin/test-connection?${this.jellyfinEmbyParams(serviceType, serverId)}`,
      { method: 'POST', headers: getAuthHeaders() }
    )
    if (response.status === 401) {
      clearSessionStorage()
      throw new Error('Unauthorized')
    }
    if (!response.ok) {
      const text = await response.text().catch(() => 'Unknown error')
      throw new Error(`Test connection failed: ${response.status} ${text}`)
    }
    return response.json() as Promise<JellyfinConnectionTestResponse>
  }
  async postJellyfinSyncLibrary(serviceType: string, serverId: string): Promise<JellyfinLibrarySyncResponse> {
    const response = await fetch(
      `${this.baseUrl}/api/v1/jellyfin/library/sync?${this.jellyfinEmbyParams(serviceType, serverId)}`,
      { method: 'POST', headers: getAuthHeaders() }
    )
    if (response.status === 401) {
      clearSessionStorage()
      throw new Error('Unauthorized')
    }
    if (!response.ok) {
      const text = await response.text().catch(() => 'Unknown error')
      throw new Error(`Sync failed: ${response.status} ${text}`)
    }
    return response.json() as Promise<JellyfinLibrarySyncResponse>
  }
  async deleteJellyfinServer(serverId: string): Promise<void> {
    const response = await fetch(`${this.baseUrl}/api/v1/jellyfin/servers/${encodeURIComponent(serverId)}`, {
      method: 'DELETE',
      headers: getAuthHeaders(),
    })
    if (response.status === 401) {
      clearSessionStorage()
      throw new Error('Unauthorized')
    }
    if (!response.ok) {
      const text = await response.text().catch(() => 'Unknown error')
      throw new Error(`Delete failed: ${response.status} ${text}`)
    }
  }

  // --- Emby (admin) ---
  async listEmbyServers(): Promise<EmbyServerDto[]> {
    return this.request<EmbyServerDto[]>('/api/v1/emby/servers')
  }
  async getEmbySettings(serviceType: string, serverId: string): Promise<EmbySettingsDto> {
    return this.request<EmbySettingsDto>(`/api/v1/emby/settings?${this.jellyfinEmbyParams(serviceType, serverId)}`)
  }
  async putEmbySettings(
    request: UpdateEmbySettingsRequest,
    confirmNewInstance = false
  ): Promise<EmbySettingsDto> {
    const q = confirmNewInstance ? '?confirmNewInstance=true' : ''
    return this.put<EmbySettingsDto>(`/api/v1/emby/settings${q}`, request)
  }
  async postEmbyTestConnection(serviceType: string, serverId: string): Promise<EmbyConnectionTestResponse> {
    const response = await fetch(
      `${this.baseUrl}/api/v1/emby/test-connection?${this.jellyfinEmbyParams(serviceType, serverId)}`,
      { method: 'POST', headers: getAuthHeaders() }
    )
    if (response.status === 401) {
      clearSessionStorage()
      throw new Error('Unauthorized')
    }
    if (!response.ok) {
      const text = await response.text().catch(() => 'Unknown error')
      throw new Error(`Test connection failed: ${response.status} ${text}`)
    }
    return response.json() as Promise<EmbyConnectionTestResponse>
  }
  async postEmbySyncLibrary(serviceType: string, serverId: string): Promise<EmbyLibrarySyncResponse> {
    const response = await fetch(
      `${this.baseUrl}/api/v1/emby/library/sync?${this.jellyfinEmbyParams(serviceType, serverId)}`,
      { method: 'POST', headers: getAuthHeaders() }
    )
    if (response.status === 401) {
      clearSessionStorage()
      throw new Error('Unauthorized')
    }
    if (!response.ok) {
      const text = await response.text().catch(() => 'Unknown error')
      throw new Error(`Sync failed: ${response.status} ${text}`)
    }
    return response.json() as Promise<EmbyLibrarySyncResponse>
  }
  async deleteEmbyServer(serverId: string): Promise<void> {
    const response = await fetch(`${this.baseUrl}/api/v1/emby/servers/${encodeURIComponent(serverId)}`, {
      method: 'DELETE',
      headers: getAuthHeaders(),
    })
    if (response.status === 401) {
      clearSessionStorage()
      throw new Error('Unauthorized')
    }
    if (!response.ok) {
      const text = await response.text().catch(() => 'Unknown error')
      throw new Error(`Delete failed: ${response.status} ${text}`)
    }
  }
}

/** Admin stats payload (when endpoint exists). */
export interface AdminStatsResponse {
  totalUsers: number
  regularUsers: number
  totalInteractions: number
  totalAcceptedMovies: number
  users: Array<{
    id: string
    username: string
    email: string
    createdAt: string
    interactionCount: number
    likedCount: number
    nopedCount: number
    skippedCount: number
    likedMovies: number[]
    isAdmin: boolean
  }>
  acceptedMovies: Array<{
    id: string
    movieId: number
    tmdbId?: number
    title?: string
    totalUsersLiked: number
    userCount: number
    createdAt: string
    addedToRadarr?: boolean
  }>
}

/** Admin update check (GitHub releases). */
export interface AdminUpdateCheckResponse {
  currentVersion: string
  latestVersion: string | null
  updateAvailable: boolean
  checkedAtUtc: string
  latestReleaseUrl: string | null
  latestReleaseName: string | null
  publishedAtUtc: string | null
  isPreRelease: boolean | null
  releaseNotes: string | null
  error: string | null
}

// --- Admin (Tindarr) DTOs ---
export interface RegistrationSettingsDto {
  allowOpenRegistration: boolean
  requireAdminApprovalForNewUsers: boolean
  defaultRole: string
}
export interface UpdateRegistrationSettingsRequest {
  allowOpenRegistration: boolean
  requireAdminApprovalForNewUsers: boolean
  defaultRole: string | null
}
export interface AdminUserDto {
  userId: string
  displayName: string
  createdAtUtc: string
  roles: string[]
  hasPassword: boolean
}

export interface AdminInteractionDto {
  id: number
  userId: string
  serviceType: string
  serverId: string
  tmdbId: number
  action: 'Like' | 'Nope' | 'Skip' | 'Superlike'
  createdAtUtc: string
}

export interface AdminInteractionSearchResponse {
  items: AdminInteractionDto[]
}
export interface AdminCreateUserRequest {
  userId: string
  displayName: string
  password: string
  roles?: string[] | null
}
export interface AdminUpdateUserRequest {
  displayName: string
}
export interface AdminSetUserRolesRequest {
  roles: string[]
}
export interface AdminSetPasswordRequest {
  newPassword: string
}
export interface JoinAddressSettingsDto {
  lanHostPort: string | null
  wanHostPort: string | null
  roomLifetimeMinutes: number | null
  guestSessionLifetimeMinutes: number | null
  updatedAtUtc: string
}
export interface UpdateJoinAddressSettingsRequest {
  lanHostPort?: string | null
  wanHostPort?: string | null
  roomLifetimeMinutes?: number | null
  guestSessionLifetimeMinutes?: number | null
}
export interface CastingSettingsDto {
  preferredSubtitleSource: string | null
  preferredSubtitleLanguage: string | null
  preferredSubtitleTrackSource: string | null
  subtitleFallback: string | null
  subtitleLanguageFallback: string | null
  subtitleTrackSourceFallback: string | null
  preferredAudioStyle: string | null
  preferredAudioLanguage: string | null
  preferredAudioTrackKind: string | null
  audioFallback: string | null
  audioLanguageFallback: string | null
  audioTrackKindFallback: string | null
  updatedAtUtc: string
}
export interface UpdateCastingSettingsRequest {
  preferredSubtitleSource?: string | null
  preferredSubtitleLanguage?: string | null
  preferredSubtitleTrackSource?: string | null
  subtitleFallback?: string | null
  subtitleLanguageFallback?: string | null
  subtitleTrackSourceFallback?: string | null
  preferredAudioStyle?: string | null
  preferredAudioLanguage?: string | null
  preferredAudioTrackKind?: string | null
  audioFallback?: string | null
  audioLanguageFallback?: string | null
  audioTrackKindFallback?: string | null
}
export interface AdvancedSettingsApiRateLimitDto {
  enabled: boolean
  permitLimit: number
  windowMinutes: number
}
export interface AdvancedSettingsCleanupDto {
  enabled: boolean
  intervalMinutes: number
  purgeGuestUsers: boolean
  guestUserMaxAgeHours: number
}
export interface AdvancedSettingsNotificationsEventsDto {
  likes: boolean
  matches: boolean
  roomCreated: boolean
  login: boolean
  userCreated: boolean
  authFailures: boolean
}
export interface AdvancedSettingsNotificationsDto {
  enabled: boolean
  webhookUrls: string[]
  events: AdvancedSettingsNotificationsEventsDto
}
export interface AdvancedSettingsDto {
  apiRateLimit: AdvancedSettingsApiRateLimitDto
  apiRateLimitDefaults: AdvancedSettingsApiRateLimitDto
  cleanup: AdvancedSettingsCleanupDto
  cleanupDefaults: AdvancedSettingsCleanupDto
  notifications: AdvancedSettingsNotificationsDto
  notificationsDefaults: AdvancedSettingsNotificationsDto
  tmdb: { hasTmdbApiKey: boolean; hasTmdbReadAccessToken: boolean }
  display: { dateTimeDisplayMode: string; timeZoneId: string; dateOrder: string }
  displayDefaults: { dateTimeDisplayMode: string; timeZoneId: string; dateOrder: string }
}
export interface ConsoleOutputDto {
  lines: string[]
}
export interface UpdateAdvancedSettingsRequest {
  apiRateLimitEnabled?: boolean | null
  apiRateLimitPermitLimit?: number | null
  apiRateLimitWindowMinutes?: number | null
  cleanupEnabled?: boolean | null
  cleanupIntervalMinutes?: number | null
  cleanupPurgeGuestUsers?: boolean | null
  cleanupGuestUserMaxAgeHours?: number | null
  notificationsSet?: boolean | null
  notificationsEnabled?: boolean | null
  notificationsWebhookUrls?: string[] | null
  notificationsEventLikes?: boolean | null
  notificationsEventMatches?: boolean | null
  notificationsEventRoomCreated?: boolean | null
  notificationsEventLogin?: boolean | null
  notificationsEventUserCreated?: boolean | null
  notificationsEventAuthFailures?: boolean | null
  tmdbApiKeySet?: boolean | null
  tmdbApiKey?: string | null
  tmdbReadAccessTokenSet?: boolean | null
  tmdbReadAccessToken?: string | null
  dateTimeDisplayMode?: string | null
  timeZoneId?: string | null
  dateOrder?: string | null
}
export interface CastingSessionDto {
  sessionId: string
  deviceId: string
  contentTitle: string
  contentSubtitle: string
  sessionState: string
  contentType: string
  startedAtUtc: string
  expiresAtUtc: string
  contentRuntimeSeconds: number
}
export interface CastingEventDto {
  eventId: number
  occurredAtUtc: string
  eventType: string
  message: string
  deviceId: string | null
  errorDetails: string | null
}
export interface CastingDiagnosticsDto {
  activeSessions: CastingSessionDto[]
  recentEvents: CastingEventDto[]
}
export interface CastDeviceDto {
  id: string
  name: string
  address?: string | null
  port: number
}
export interface CastMediaUrlDto {
  url: string
  contentType: string
  title: string
  subTitle?: string | null
  sessionId?: string | null
}
export interface TmdbStoredMovieAdminDto {
  tmdbId: number
  title: string
  releaseYear: number | null
  posterPath: string | null
  backdropPath: string | null
  detailsFetchedAtUtc: string | null
  updatedAtUtc: string | null
  posterCached: boolean
  backdropCached: boolean
}
export interface AdminDbMovieListResponse {
  items: TmdbStoredMovieAdminDto[]
  skip: number
  take: number
  nextSkip: number
  hasMore: boolean
  totalCount: number
}
export interface PopulateStatusDto {
  state: string
  detailsTotal: number
  detailsDone: number
  imagesTotal: number
  imagesDone: number
  lastMessage: string | null
}

// Radarr API (admin): DTOs match backend Contracts.Radarr
export interface RadarrSettingsDto {
  serviceType: string
  serverId: string
  configured: boolean
  baseUrl: string | null
  qualityProfileId: number | null
  rootFolderPath: string | null
  tagLabel: string | null
  autoAddEnabled: boolean
  autoAddIntervalMinutes: number | null
  hasApiKey: boolean
  lastLibrarySyncUtc: string | null
  updatedAtUtc: string | null
}
export interface UpdateRadarrSettingsRequest {
  baseUrl: string
  apiKey: string | null
  qualityProfileId: number | null
  rootFolderPath: string | null
  tagLabel: string | null
  autoAddEnabled: boolean
  autoAddIntervalMinutes: number | null
}
export interface RadarrConnectionTestResponse {
  ok: boolean
  message: string | null
}
export interface RadarrQualityProfileDto {
  id: number
  name: string
}
export interface RadarrRootFolderDto {
  id: number
  path: string
  freeSpaceBytes: number | null
}
export interface RadarrAutoAddResponse {
  attempted: number
  added: number
  skippedExisting: number
  failed: number
  message: string | null
}
export interface RadarrLibrarySyncResponse {
  serviceType: string
  serverId: string
  count: number
  syncedAtUtc: string
}

export interface MatchSettingsDto {
  serviceType: string
  serverId: string
  minUsers: number | null
  minUserPercent: number | null
  updatedAtUtc: string
}
export interface UpdateMatchSettingsRequest {
  minUsers: number | null
  minUserPercent: number | null
}

// TMDB (admin): cache settings and build
export interface TmdbCacheSettingsDto {
  maxRows: number
  currentRows: number
  maxMovies: number
  currentMovies: number
  imageCacheMaxMb: number
  imageCacheBytes: number
  posterMode: string
  prewarmOriginalLanguage: string | null
  prewarmRegion: string | null
}
export interface UpdateTmdbCacheSettingsRequest {
  maxRows: number
  maxMovies: number
  imageCacheMaxMb: number
  posterMode: string
  prewarmOriginalLanguage: string | null
  prewarmRegion: string | null
}
export interface TmdbBuildStatusDto {
  state: string
  startedAtUtc: string | null
  finishedAtUtc: string | null
  rateLimitOverride: boolean
  currentUserId: string | null
  usersProcessed: number
  usersTotal: number
  moviesDiscovered: number
  detailsFetched: number
  imagesFetched: number
  lastMessage: string | null
  lastError: string | null
}
export interface StartTmdbBuildRequest {
  rateLimitOverride: boolean
  usersBatchSize?: number
  discoverLimitPerUser?: number
  prefetchImages?: boolean
}
export interface TmdbImportResultDto {
  inserted: number
  updated: number
  skipped: number
  notImportedReasons: string[]
}
export interface TmdbRestoreResultDto {
  inserted: number
  updated: number
  skipped: number
  imagesRestored: number
  notImportedReasons: string[]
}

// --- Plex (admin) DTOs ---
export interface PlexAuthStatusResponse {
  hasClientIdentifier: boolean
  hasAuthToken: boolean
}
export interface PlexPinCreateResponse {
  pinId: number
  code: string
  expiresAtUtc: string | null
  authUrl: string
}
export interface PlexPinStatusResponse {
  pinId: number
  code: string
  expiresAtUtc: string | null
  authorized: boolean
}
export interface PlexServerDto {
  serverId: string
  name: string
  baseUrl: string | null
  version: string | null
  platform: string | null
  owned: boolean | null
  online: boolean | null
  lastLibrarySyncUtc: string | null
  updatedAtUtc: string
  movieCount: number
}
export interface PlexLibrarySyncStatusDto {
  serviceType: string
  serverId: string
  state: string
  totalSections: number
  processedSections: number
  totalItems: number
  processedItems: number
  tmdbIdsFound: number
  startedAtUtc: string | null
  finishedAtUtc: string | null
  message: string | null
  updatedAtUtc: string
}

// --- Jellyfin (admin) DTOs ---
export interface JellyfinServerDto {
  serverId: string
  name: string
  baseUrl: string | null
  version: string | null
  lastLibrarySyncUtc: string | null
  updatedAtUtc: string
  movieCount: number
}
export interface JellyfinSettingsDto {
  serviceType: string
  serverId: string
  configured: boolean
  baseUrl: string | null
  hasApiKey: boolean
  serverName: string | null
  serverVersion: string | null
  lastLibrarySyncUtc: string | null
  updatedAtUtc: string | null
}
export interface UpdateJellyfinSettingsRequest {
  baseUrl: string
  apiKey: string
}
export interface JellyfinConnectionTestResponse {
  ok: boolean
  message: string | null
}
export interface JellyfinLibrarySyncResponse {
  serviceType: string
  serverId: string
  count: number
  syncedAtUtc: string
}

// --- Emby (admin) DTOs ---
export interface EmbyServerDto {
  serverId: string
  name: string
  baseUrl: string | null
  version: string | null
  lastLibrarySyncUtc: string | null
  updatedAtUtc: string
  movieCount: number
}
export interface EmbySettingsDto {
  serviceType: string
  serverId: string
  configured: boolean
  baseUrl: string | null
  hasApiKey: boolean
  serverName: string | null
  serverVersion: string | null
  lastLibrarySyncUtc: string | null
  updatedAtUtc: string | null
}
export interface UpdateEmbySettingsRequest {
  baseUrl: string
  apiKey: string
}
export interface EmbyConnectionTestResponse {
  ok: boolean
  message: string | null
}
export interface EmbyLibrarySyncResponse {
  serviceType: string
  serverId: string
  count: number
  syncedAtUtc: string
}

export const apiClient = new ApiClient(API_BASE_URL)
