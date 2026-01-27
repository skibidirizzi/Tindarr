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

export type MeResponse = {
  userId: string;
  displayName: string;
  roles: string[];
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
  preferredRegions: string[];
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
  preferredRegions: string[];
  sortBy: string;
};

export type UndoResponse = {
  undone: boolean;
  tmdbId: number | null;
  action: "Like" | "Nope" | "Skip" | "Superlike" | null;
  createdAtUtc: string | null;
};

