const TMDB_BASE_URL = 'https://api.themoviedb.org/3'
const READ_ACCESS_TOKEN = import.meta.env.VITE_TMDB_READ_ACCESS_TOKEN

export interface TMDBMovie {
  adult: boolean
  backdrop_path: string | null
  genre_ids: number[]
  id: number
  original_language: string
  original_title: string
  overview: string
  popularity: number
  poster_path: string | null
  release_date: string
  title: string
  video: boolean
  vote_average: number
  vote_count: number
}

export interface TMDBDiscoverResponse {
  page: number
  results: TMDBMovie[]
  total_pages: number
  total_results: number
}

export interface DiscoverMovieParams {
  language?: string
  page?: number
  region?: string
  sort_by?:
    | 'popularity.asc'
    | 'popularity.desc'
    | 'release_date.asc'
    | 'release_date.desc'
    | 'revenue.asc'
    | 'revenue.desc'
    | 'primary_release_date.asc'
    | 'primary_release_date.desc'
    | 'original_title.asc'
    | 'original_title.desc'
    | 'vote_average.asc'
    | 'vote_average.desc'
    | 'vote_count.asc'
    | 'vote_count.desc'
  certification_country?: string
  certification?: string
  'certification.lte'?: string
  'certification.gte'?: string
  include_adult?: boolean
  include_video?: boolean
  primary_release_year?: number
  'primary_release_date.gte'?: string
  'primary_release_date.lte'?: string
  'release_date.gte'?: string
  'release_date.lte'?: string
  with_release_type?: number
  year?: number
  'vote_count.gte'?: number
  'vote_count.lte'?: number
  'vote_average.gte'?: number
  'vote_average.lte'?: number
  with_cast?: string
  with_crew?: string
  with_people?: string
  with_companies?: string
  with_genres?: string
  without_genres?: string
  with_keywords?: string
  without_keywords?: string
  'with_runtime.gte'?: number
  'with_runtime.lte'?: number
  with_original_language?: string
  with_watch_providers?: string
  watch_region?: string
  with_watch_monetization_types?: string
}

class TMDBClient {
  private baseUrl: string
  private readToken: string

  constructor() {
    this.baseUrl = TMDB_BASE_URL
    this.readToken = READ_ACCESS_TOKEN ?? ''
  }

  private buildQueryString(
    params: Record<string, string | number | boolean | undefined>
  ): string {
    const searchParams = new URLSearchParams()
    Object.entries(params).forEach(([key, value]) => {
      if (value !== undefined && value !== null) {
        searchParams.append(key, String(value))
      }
    })
    return searchParams.toString()
  }

  private async requestWithToken<T>(
    endpoint: string,
    params?: Record<string, string | number | boolean | undefined>
  ): Promise<T> {
    const queryString = params ? `?${this.buildQueryString(params)}` : ''
    const url = `${this.baseUrl}${endpoint}${queryString}`

    const response = await fetch(url, {
      headers: {
        Authorization: `Bearer ${this.readToken}`,
        'Content-Type': 'application/json',
      },
    })

    if (!response.ok) {
      const error = await response.json().catch(() => ({}))
      throw new Error(
        `TMDB API error: ${response.status} - ${
          (error as { status_message?: string }).status_message || response.statusText
        }`
      )
    }

    return response.json() as Promise<T>
  }

  async discoverMovies(params: DiscoverMovieParams = {}): Promise<TMDBDiscoverResponse> {
    return this.requestWithToken<TMDBDiscoverResponse>('/discover/movie', params as Record<string, string | number | boolean | undefined>)
  }

  getPosterUrl(
    path: string | null,
    size: 'w92' | 'w154' | 'w185' | 'w342' | 'w500' | 'w780' | 'original' = 'w500'
  ): string | null {
    if (!path) return null
    return `https://image.tmdb.org/t/p/${size}${path}`
  }

  getBackdropUrl(
    path: string | null,
    size: 'w300' | 'w780' | 'w1280' | 'original' = 'w1280'
  ): string | null {
    if (!path) return null
    return `https://image.tmdb.org/t/p/${size}${path}`
  }
}

export const tmdbClient = new TMDBClient()
