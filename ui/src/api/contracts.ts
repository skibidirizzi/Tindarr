export type AuthResponse = {
  accessToken: string;
  expiresAtUtc: string;
  userId: string;
  displayName: string;
  roles: string[];
};

export type LoginRequest = {
  userId: string;
  password: string;
};

export type RegisterRequest = {
  userId: string;
  displayName: string;
  password: string;
};

export type GuestLoginRequest = {
  roomId: string;
  displayName?: string | null;
};

export type MeResponse = {
  userId: string;
  displayName: string;
  roles: string[];
};

export type InfoResponse = {
  name: string;
  version: string;
  environment: string;
  utcNow: string;
};

export type AdminUpdateCheckResponse = {
  currentVersion: string;
  latestVersion: string | null;
  updateAvailable: boolean;
  checkedAtUtc: string;
  latestReleaseUrl: string | null;
  latestReleaseName: string | null;
  publishedAtUtc: string | null;
  isPreRelease: boolean | null;
  releaseNotes: string | null;
  error: string | null;
};

export type UserDto = {
  userId: string;
  displayName: string;
  createdAtUtc: string;
  roles: string[];
  hasPassword: boolean;
};

export type JoinAddressSettingsDto = {
  lanHostPort: string | null;
  wanHostPort: string | null;
  roomLifetimeMinutes: number | null;
  guestSessionLifetimeMinutes: number | null;
  updatedAtUtc: string;
};

export type CastingSettingsDto = {
  preferredSubtitleSource: string | null;
  preferredSubtitleLanguage: string | null;
  preferredSubtitleTrackSource: string | null;
  subtitleFallback: string | null;
  subtitleLanguageFallback: string | null;
  subtitleTrackSourceFallback: string | null;
  preferredAudioStyle: string | null;
  preferredAudioLanguage: string | null;
  preferredAudioTrackKind: string | null;
  audioFallback: string | null;
  audioLanguageFallback: string | null;
  audioTrackKindFallback: string | null;
  updatedAtUtc: string;
};

export type MatchSettingsDto = {
	serviceType: string;
	serverId: string;
	minUsers: number | null;
	minUserPercent: number | null;
	updatedAtUtc: string;
};

export type ServiceScopeOptionDto = {
  serviceType: string;
  serverId: string;
  displayName: string;
};

export type UpdateJoinAddressSettingsRequest = {
  lanHostPort: string | null;
  wanHostPort: string | null;
  roomLifetimeMinutes: number | null;
  guestSessionLifetimeMinutes: number | null;
};

export type UpdateCastingSettingsRequest = {
  preferredSubtitleSource: string | null;
  preferredSubtitleLanguage: string | null;
  preferredSubtitleTrackSource: string | null;
  subtitleFallback: string | null;
  subtitleLanguageFallback: string | null;
  subtitleTrackSourceFallback: string | null;
  preferredAudioStyle: string | null;
  preferredAudioLanguage: string | null;
  audioFallback: string | null;
  audioLanguageFallback: string | null;
  preferredAudioTrackKind: string | null;
  audioTrackKindFallback: string | null;
};

export type UpdateMatchSettingsRequest = {
	minUsers: number | null;
	minUserPercent: number | null;
};

export type CreateUserRequest = {
  userId: string;
  displayName: string;
  password: string;
  roles?: string[] | null;
};

export type UpdateUserRequest = {
  displayName: string;
};

export type SetUserRolesRequest = {
  roles: string[];
};

export type AdminSetPasswordRequest = {
  newPassword: string;
};

export type UserPreferencesDto = {
  includeAdult: boolean;
  minReleaseYear: number | null;
  maxReleaseYear: number | null;
  minRating: number | null;
  maxRating: number | null;
  preferredGenres: number[];
  excludedGenres: number[];
  preferredOriginalLanguages: string[];
  excludedOriginalLanguages: string[];
  preferredRegions: string[];
  excludedRegions: string[];
  sortBy: string;
  updatedAtUtc: string;
};

export type UpdateUserPreferencesRequest = {
  includeAdult: boolean;
  minReleaseYear: number | null;
  maxReleaseYear: number | null;
  minRating: number | null;
  maxRating: number | null;
  preferredGenres: number[];
  excludedGenres: number[];
  preferredOriginalLanguages: string[];
  excludedOriginalLanguages: string[];
  preferredRegions: string[];
  excludedRegions: string[];
  sortBy: string;
};

export type UndoResponse = {
  undone: boolean;
  tmdbId: number | null;
  action: "Like" | "Nope" | "Skip" | "Superlike" | null;
  createdAtUtc: string | null;
};

export type InteractionDto = {
  tmdbId: number;
  action: "Like" | "Nope" | "Skip" | "Superlike";
  createdAtUtc: string;
};

export type InteractionListResponse = {
  serviceType: string;
  serverId: string;
  items: InteractionDto[];
};

export type MatchDto = {
  tmdbId: number;
};

export type MatchesResponse = {
  serviceType: string;
  serverId: string;
  items: MatchDto[];
};

export type RoomMemberDto = {
  userId: string;
  joinedAtUtc: string;
};

export type CreateRoomRequest = {
  serviceType: string;
  serverId: string;
};

export type CreateRoomResponse = {
  roomId: string;
  ownerUserId: string;
  serviceType: string;
  serverId: string;
  members: RoomMemberDto[];
};

export type JoinRoomResponse = {
  roomId: string;
  ownerUserId: string;
  serviceType: string;
  serverId: string;
  members: RoomMemberDto[];
};

export type RoomStateResponse = {
  roomId: string;
  ownerUserId: string;
  serviceType: string;
  serverId: string;
  isClosed: boolean;
  createdAtUtc: string;
  lastActivityAtUtc: string;
  members: RoomMemberDto[];
};

export type RoomJoinUrlResponse = {
  url: string;
};

export type RoomMatchesResponse = {
  roomId: string;
  serviceType: string;
  serverId: string;
  tmdbIds: number[];
};

export type CastDeviceDto = {
  id: string;
  name: string;
  address: string | null;
  port: number;
};

export type CastMovieRequest = {
  deviceId: string;
  serviceType: string;
  serverId: string;
  tmdbId: number;
  title?: string | null;
};

export type CastMediaUrlDto = {
  url: string;
  contentType: string;
  title: string;
  subTitle: string | null;
};

export type GetMovieCastUrlRequest = {
  serviceType: string;
  serverId: string;
  tmdbId: number;
  title?: string | null;
};

export type MovieDetailsDto = {
  tmdbId: number;
  title: string;
  overview: string | null;
  posterUrl: string | null;
  backdropUrl: string | null;
  releaseDate: string | null;
  releaseYear: number | null;
  mpaaRating: string | null;
  rating: number | null;
  voteCount: number | null;
  genres: string[];
  regions: string[];
  originalLanguage: string | null;
  runtimeMinutes: number | null;
};

export type TmdbCacheSettingsDto = {
  maxRows: number;
  currentRows: number;
  maxMovies: number;
  currentMovies: number;
  imageCacheMaxMb: number;
  imageCacheBytes: number;
  posterMode: string;
  prewarmOriginalLanguage: string | null;
  prewarmRegion: string | null;
};

export type UpdateTmdbCacheSettingsRequest = {
  maxRows: number;
  maxMovies: number;
  imageCacheMaxMb: number;
  posterMode: string;
  prewarmOriginalLanguage: string | null;
  prewarmRegion: string | null;
};

export type StartTmdbBuildRequest = {
  rateLimitOverride: boolean;
  usersBatchSize: number;
  discoverLimitPerUser: number;
  prefetchImages: boolean;
};

export type TmdbBuildStatusDto = {
  state: string;
  startedAtUtc: string | null;
  finishedAtUtc: string | null;
  rateLimitOverride: boolean;
  currentUserId: string | null;
  usersProcessed: number;
  usersTotal: number;
  moviesDiscovered: number;
  detailsFetched: number;
  imagesFetched: number;
  lastMessage: string | null;
  lastError: string | null;
};

export type TmdbStoredMovieAdminDto = {
  tmdbId: number;
  title: string;
  releaseYear: number | null;
  posterPath: string | null;
  backdropPath: string | null;
  detailsFetchedAtUtc: string | null;
  updatedAtUtc: string | null;
  posterCached: boolean;
  backdropCached: boolean;
};

export type TmdbStoredMovieAdminListResponse = {
  items: TmdbStoredMovieAdminDto[];
  skip: number;
  take: number;
  nextSkip: number;
  hasMore: boolean;
};

export type AdminDbMovieListResponse = {
  items: TmdbStoredMovieAdminDto[];
  skip: number;
  take: number;
  nextSkip: number;
  hasMore: boolean;
  totalCount: number;
};

export type TmdbFetchMovieImagesResultDto = {
  tmdbId: number;
  posterFetched: boolean;
  backdropFetched: boolean;
  message: string | null;
};

export type AdminInteractionDto = {
  userId: string;
  serviceType: string;
  serverId: string;
  tmdbId: number;
  action: "Like" | "Nope" | "Skip" | "Superlike";
  createdAtUtc: string;
};

export type AdminInteractionSearchResponse = {
  items: AdminInteractionDto[];
};

export type PlexAuthStatusResponse = {
  hasClientIdentifier: boolean;
  hasAuthToken: boolean;
};

export type PlexPinCreateResponse = {
  pinId: number;
  code: string;
  expiresAtUtc: string;
  authUrl: string;
};

export type PlexPinStatusResponse = {
  pinId: number;
  code: string;
  expiresAtUtc: string;
  authorized: boolean;
};

export type PlexServerDto = {
  serverId: string;
  name: string;
  baseUrl: string;
  version: string | null;
  platform: string | null;
  owned: boolean;
  online: boolean;
  lastLibrarySyncUtc: string | null;
  updatedAtUtc: string;
};

export type PlexLibrarySyncStatusDto = {
  serviceType: string;
  serverId: string;
  state: string;
  totalSections: number;
  processedSections: number;
  totalItems: number;
  processedItems: number;
  tmdbIdsFound: number;
  startedAtUtc: string | null;
  finishedAtUtc: string | null;
  message: string | null;
  updatedAtUtc: string;
};

export type PlexLibraryMissingDetailsItemDto = {
  tmdbId: number;
  title: string;
};

export type PlexLibraryMissingDetailsResponse = {
  serviceType: string;
  serverId: string;
  count: number;
  items: PlexLibraryMissingDetailsItemDto[];
};

export type RadarrSettingsDto = {
  serviceType: string;
  serverId: string;
  configured: boolean;
  baseUrl: string | null;
  qualityProfileId: number | null;
  rootFolderPath: string | null;
  tagLabel: string | null;
  autoAddEnabled: boolean;
  autoAddIntervalMinutes: number | null;
  hasApiKey: boolean;
  lastLibrarySyncUtc: string | null;
  updatedAtUtc: string | null;
};

export type UpdateRadarrSettingsRequest = {
  baseUrl: string;
  apiKey: string | null;
  qualityProfileId: number | null;
  rootFolderPath: string | null;
  tagLabel: string | null;
  autoAddEnabled: boolean;
  autoAddIntervalMinutes: number | null;
};

export type RadarrQualityProfileDto = {
  id: number;
  name: string;
};

export type RadarrRootFolderDto = {
  id: number;
  path: string;
  freeSpaceBytes: number | null;
};

export type RadarrConnectionTestResponse = {
  ok: boolean;
  message: string | null;
};

export type RadarrLibrarySyncResponse = {
  serviceType: string;
  serverId: string;
  count: number;
  syncedAtUtc: string;
};

export type JellyfinServerDto = {
  serverId: string;
  name: string;
  baseUrl: string | null;
  version: string | null;
  lastLibrarySyncUtc: string | null;
  updatedAtUtc: string;
};

export type JellyfinSettingsDto = {
  serviceType: string;
  serverId: string;
  configured: boolean;
  baseUrl: string | null;
  hasApiKey: boolean;
  serverName: string | null;
  serverVersion: string | null;
  lastLibrarySyncUtc: string | null;
  updatedAtUtc: string | null;
};

export type UpdateJellyfinSettingsRequest = {
  baseUrl: string;
  apiKey: string;
};

export type JellyfinConnectionTestResponse = {
  ok: boolean;
  message: string | null;
};

export type JellyfinLibrarySyncResponse = {
  serviceType: string;
  serverId: string;
  count: number;
  syncedAtUtc: string;
};

export type EmbyServerDto = {
  serverId: string;
  name: string;
  baseUrl: string | null;
  version: string | null;
  lastLibrarySyncUtc: string | null;
  updatedAtUtc: string;
};

export type EmbySettingsDto = {
  serviceType: string;
  serverId: string;
  configured: boolean;
  baseUrl: string | null;
  hasApiKey: boolean;
  serverName: string | null;
  serverVersion: string | null;
  lastLibrarySyncUtc: string | null;
  updatedAtUtc: string | null;
};

export type UpdateEmbySettingsRequest = {
  baseUrl: string;
  apiKey: string;
};

export type EmbyConnectionTestResponse = {
  ok: boolean;
  message: string | null;
};

export type EmbyLibrarySyncResponse = {
  serviceType: string;
  serverId: string;
  count: number;
  syncedAtUtc: string;
};

