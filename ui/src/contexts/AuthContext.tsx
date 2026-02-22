import {
  createContext,
  useContext,
  useState,
  useEffect,
  type ReactNode,
} from 'react'
import {
  apiClient,
  clearSessionStorage,
  AUTH_TOKEN_KEY,
  USER_ID_KEY,
  type User,
  type UserPreferences,
} from '../lib/api'

interface AuthContextType {
  user: User | null
  loading: boolean
  login: (
    username: string,
    password: string
  ) => Promise<{ needPassword?: boolean }>
  register: (userId: string, displayName: string, password: string) => Promise<void>
  logout: () => void
  updatePreferences: (preferences: UserPreferences) => Promise<void>
}

const AuthContext = createContext<AuthContextType | undefined>(undefined)

const defaultPreferences: UserPreferences = {
  preferredGenres: [],
  excludedGenres: [],
  minRating: 0,
  maxRating: 10,
  includeAdult: false,
  sortBy: 'popularity.desc',
  language: 'en-US',
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<User | null>(null)
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    const token = localStorage.getItem(AUTH_TOKEN_KEY)
    if (!token) {
      setLoading(false)
      return
    }
    Promise.all([apiClient.getMe(), apiClient.getPreferences()])
      .then(([me, preferences]) => {
        setUser({
          id: me.userId,
          username: me.displayName,
          email: '',
          isAdmin: me.roles.includes('Admin'),
          canSuperlike: me.roles.includes('Admin') || me.roles.includes('Curator'),
          createdAt: new Date().toISOString(),
          preferences,
        })
      })
      .catch(() => {
        clearSessionStorage()
      })
      .finally(() => setLoading(false))
  }, [])

  const login = async (username: string, password: string) => {
    const userId = username.trim().toLowerCase()
    try {
      const loginResp = await apiClient.login({ userId, password })
      setUserFromAuthResponse(loginResp)
      localStorage.setItem(AUTH_TOKEN_KEY, loginResp.accessToken)
      localStorage.setItem(USER_ID_KEY, loginResp.userId)
      const preferences = await apiClient.getPreferences()
      setUser((u) => (u ? { ...u, preferences } : null))
      return { needPassword: false }
    } catch (error) {
      try {
        const reg = await apiClient.register({
          userId,
          displayName: username.trim(),
          password,
        })
        setUserFromAuthResponse(reg)
        localStorage.setItem(AUTH_TOKEN_KEY, reg.accessToken)
        localStorage.setItem(USER_ID_KEY, reg.userId)
        const preferences = await apiClient.getPreferences()
        setUser((u) => (u ? { ...u, preferences } : null))
        return { needPassword: false }
      } catch (inner) {
        console.error('Login failed:', inner)
        throw inner
      }
    }
  }

  const setUserFromAuthResponse = (resp: {
    userId: string
    displayName: string
    roles: string[]
  }) => {
    setUser({
      id: resp.userId,
      username: resp.displayName,
      email: '',
      isAdmin: resp.roles.includes('Admin'),
      canSuperlike: resp.roles.includes('Admin') || resp.roles.includes('Curator'),
      createdAt: new Date().toISOString(),
      preferences: defaultPreferences,
    })
  }

  const register = async (
    userId: string,
    displayName: string,
    password: string
  ) => {
    const resp = await apiClient.register({
      userId: userId.trim().toLowerCase(),
      displayName: displayName.trim() || userId.trim(),
      password,
    })
    setUserFromAuthResponse(resp)
    localStorage.setItem(AUTH_TOKEN_KEY, resp.accessToken)
    localStorage.setItem(USER_ID_KEY, resp.userId)
    const preferences = await apiClient.getPreferences()
    setUser((u) => (u ? { ...u, preferences } : null))
  }

  const logout = () => {
    setUser(null)
    clearSessionStorage()
  }

  const updatePreferences = async (preferences: UserPreferences) => {
    if (!user) throw new Error('No user logged in')

    await apiClient.updateUserPreferences(preferences)
    setUser({ ...user, preferences })
  }

  return (
    <AuthContext.Provider
      value={{ user, loading, login, register, logout, updatePreferences }}
    >
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
