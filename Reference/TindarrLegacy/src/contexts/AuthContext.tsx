import { createContext, useContext, useState, useEffect, ReactNode } from 'react'
import { apiClient, User, UserPreferences } from '../lib/api'

interface AuthContextType {
  user: User | null
  loading: boolean
  login: (username: string, password: string) => Promise<{ needPassword?: boolean }>
  logout: () => void
  updatePreferences: (preferences: UserPreferences) => Promise<void>
}

const AuthContext = createContext<AuthContextType | undefined>(undefined)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<User | null>(null)
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    // Check for stored user session
    const storedUserId = localStorage.getItem('tindarr_user_id')
    if (storedUserId) {
      apiClient
        .getUser(storedUserId)
        .then(setUser)
        .catch(() => {
          localStorage.removeItem('tindarr_user_id')
        })
        .finally(() => setLoading(false))
    } else {
      setLoading(false)
    }
  }, [])

  const login = async (username: string, password: string) => {
    try {
      // Attempt login first
      let loginResp = await apiClient.login({ username, password })
      if (loginResp.needPassword) {
        return { needPassword: true }
      }
      setUser({
        id: loginResp.id,
        username: loginResp.username,
        email: loginResp.email,
        isAdmin: loginResp.isAdmin,
        createdAt: new Date().toISOString(),
        preferences: {
          preferredGenres: [],
          excludedGenres: [],
          minRating: 0,
          maxRating: 10,
          includeAdult: false,
          sortBy: 'popularity.desc',
          language: 'en-US',
        },
      })
      localStorage.setItem('tindarr_user_id', loginResp.id)
      return { needPassword: false }
    } catch (error) {
      // If login failed, attempt registration (if allowed)
      try {
        const reg = await apiClient.register({ username, password, email: `${username}@tindarr.local` })
        setUser({
          id: reg.id,
          username: reg.username,
          email: reg.email,
          isAdmin: reg.isAdmin,
          createdAt: new Date().toISOString(),
          preferences: {
            preferredGenres: [],
            excludedGenres: [],
            minRating: 0,
            maxRating: 10,
            includeAdult: false,
            sortBy: 'popularity.desc',
            language: 'en-US',
          },
        })
        localStorage.setItem('tindarr_user_id', reg.id)
        return { needPassword: false }
      } catch (inner) {
        console.error('Login failed:', inner)
        throw inner
      }
    }
  }

  const logout = () => {
    setUser(null)
    localStorage.removeItem('tindarr_user_id')
  }

  const updatePreferences = async (preferences: UserPreferences) => {
    if (!user) throw new Error('No user logged in')

    await apiClient.updateUserPreferences(user.id, preferences)
    setUser({ ...user, preferences })
  }

  return (
    <AuthContext.Provider value={{ user, loading, login, logout, updatePreferences }}>
      {children}
    </AuthContext.Provider>
  )
}

export function useAuth() {
  const context = useContext(AuthContext)
  if (context === undefined) {
    throw new Error('useAuth must be used within an AuthProvider')
  }
  return context
}
