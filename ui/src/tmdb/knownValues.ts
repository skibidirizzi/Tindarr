export type TmdbOption<T extends string = string> = {
  value: T;
  label: string;
};

export const TMDB_SORT_BY: readonly TmdbOption[] = [
  { value: "popularity.desc", label: "Popularity (desc)" },
  { value: "popularity.asc", label: "Popularity (asc)" },
  { value: "release_date.desc", label: "Release date (desc)" },
  { value: "release_date.asc", label: "Release date (asc)" },
  { value: "primary_release_date.desc", label: "Primary release date (desc)" },
  { value: "primary_release_date.asc", label: "Primary release date (asc)" },
  { value: "revenue.desc", label: "Revenue (desc)" },
  { value: "revenue.asc", label: "Revenue (asc)" },
  { value: "vote_average.desc", label: "Vote average (desc)" },
  { value: "vote_average.asc", label: "Vote average (asc)" },
  { value: "vote_count.desc", label: "Vote count (desc)" },
  { value: "vote_count.asc", label: "Vote count (asc)" },
  { value: "original_title.asc", label: "Original title (A→Z)" },
  { value: "original_title.desc", label: "Original title (Z→A)" }
];

export type TmdbGenre = { id: number; name: string };

// Common TMDB movie genre IDs. (Stable enough to treat as “known-good” defaults.)
export const TMDB_MOVIE_GENRES: readonly TmdbGenre[] = [
  { id: 28, name: "Action" },
  { id: 12, name: "Adventure" },
  { id: 16, name: "Animation" },
  { id: 35, name: "Comedy" },
  { id: 80, name: "Crime" },
  { id: 99, name: "Documentary" },
  { id: 18, name: "Drama" },
  { id: 10751, name: "Family" },
  { id: 14, name: "Fantasy" },
  { id: 36, name: "History" },
  { id: 27, name: "Horror" },
  { id: 10402, name: "Music" },
  { id: 9648, name: "Mystery" },
  { id: 10749, name: "Romance" },
  { id: 878, name: "Science Fiction" },
  { id: 10770, name: "TV Movie" },
  { id: 53, name: "Thriller" },
  { id: 10752, name: "War" },
  { id: 37, name: "Western" }
];

// A small “good default” set for original_language (ISO 639-1).
export const TMDB_LANGUAGES: readonly TmdbOption[] = [
  { value: "en", label: "English" },
  { value: "es", label: "Spanish" },
  { value: "fr", label: "French" },
  { value: "de", label: "German" },
  { value: "it", label: "Italian" },
  { value: "pt", label: "Portuguese" },
  { value: "ru", label: "Russian" },
  { value: "ja", label: "Japanese" },
  { value: "ko", label: "Korean" },
  { value: "zh", label: "Chinese" },
  { value: "hi", label: "Hindi" }
];

// A small “good default” set for region (ISO 3166-1).
export const TMDB_REGIONS: readonly TmdbOption[] = [
  { value: "US", label: "United States" },
  { value: "GB", label: "United Kingdom" },
  { value: "CA", label: "Canada" },
  { value: "AU", label: "Australia" },
  { value: "DE", label: "Germany" },
  { value: "FR", label: "France" },
  { value: "ES", label: "Spain" },
  { value: "IT", label: "Italy" },
  { value: "JP", label: "Japan" },
  { value: "KR", label: "Korea" },
  { value: "BR", label: "Brazil" },
  { value: "IN", label: "India" }
];

