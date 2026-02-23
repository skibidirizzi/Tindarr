const API_BASE_URL = import.meta.env.VITE_API_BASE_URL || 'http://localhost:6565'

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

export interface User {
  id: string
  username: string
  email: string
  createdAt: string
  isAdmin: boolean
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

export interface RegisterRequest {
  username: string
  password: string
  email?: string
}

export interface LoginRequest {
  username: string
  password: string
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

  private async request<T>(endpoint: string): Promise<T> {
    const response = await fetch(`${this.baseUrl}${endpoint}`)
    
    if (!response.ok) {
      throw new Error(`API request failed: ${response.statusText}`)
    }
    
    return response.json() as Promise<T>
  }

  private async post<T>(endpoint: string, data: any): Promise<T> {
    const response = await fetch(`${this.baseUrl}${endpoint}`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify(data),
    })
    
    if (!response.ok) {
      throw new Error(`API request failed: ${response.statusText}`)
    }
    
    return response.json() as Promise<T>
  }

  private async put<T>(endpoint: string, data: any): Promise<T> {
    const response = await fetch(`${this.baseUrl}${endpoint}`, {
      method: 'PUT',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify(data),
    })
    
    if (!response.ok) {
      const errorText = await response.text().catch(() => 'Unknown error')
      throw new Error(`API request failed: ${response.status} - ${errorText}`)
    }
    
    // Handle 204 No Content
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

  // User Management
  async createUser(request: CreateUserRequest): Promise<User> {
    return this.post<User>('/api/v1/users', request)
  }

  async register(request: RegisterRequest): Promise<{ id: string; username: string; email: string; isAdmin: boolean }> {
    return this.post('/api/v1/auth/register', request)
  }

  async login(request: LoginRequest): Promise<LoginResponse> {
    return this.post('/api/v1/auth/login', request)
  }

  async setPassword(request: SetPasswordRequest): Promise<{ success: boolean }> {
    return this.post('/api/v1/auth/set-password', request)
  }

  async getUser(userId: string): Promise<User> {
    return this.request<User>(`/api/v1/users/${userId}`)
  }

  async getUserByUsername(username: string): Promise<User> {
    return this.request<User>(`/api/v1/users/username/${username}`)
  }

  async updateUserPreferences(userId: string, preferences: UserPreferences): Promise<void> {
    return this.put<void>(`/api/v1/users/${userId}/preferences`, preferences)
  }

  // Movie Interactions
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

  // Backend TMDB Discover proxy (server-side filtering)
  async discoverMovies(userId: string, page: number): Promise<BackendDiscoverResponse> {
    return this.request<BackendDiscoverResponse>(`/api/v1/discover?userId=${userId}&page=${page}`)
  }
}

export const apiClient = new ApiClient(API_BASE_URL)
