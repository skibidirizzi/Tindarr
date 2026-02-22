import { useEffect, useState } from 'react'
import { Routes, Route } from 'react-router-dom'
import SwipeDeck from './components/SwipeDeck'
import Login from './components/Login'
import PreferencesModal from './components/PreferencesModal'
import AdminConsole from './pages/AdminConsole'
import { useAuth } from './contexts/AuthContext'
import { apiClient, InfoResponse } from './lib/api'

function AppLayout() {
  const { user, loading: authLoading, logout } = useAuth()
  const [appInfo, setAppInfo] = useState<InfoResponse | null>(null)
  const [healthStatus, setHealthStatus] = useState<string>('')
  const [showPreferences, setShowPreferences] = useState(false)

  useEffect(() => {
    const fetchData = async () => {
      try {
        const health = await apiClient.getHealth()
        setHealthStatus(health.status)
        
        const info = await apiClient.getInfo()
        setAppInfo(info)
      } catch (error) {
        console.error('Failed to fetch API data:', error)
      }
    }

    fetchData()
  }, [])

  if (authLoading) {
    return (
      <div className="flex min-h-screen items-center justify-center bg-gradient-to-br from-slate-900 via-purple-900 to-slate-900">
        <div className="text-center">
          <div className="mb-4 inline-block h-12 w-12 animate-spin rounded-full border-4 border-pink-500 border-t-transparent"></div>
          <p className="text-xl text-gray-300">Loading...</p>
        </div>
      </div>
    )
  }

  if (!user) {
    return <Login />
  }

  return (
    <div className="min-h-screen bg-gradient-to-br from-slate-900 via-purple-900 to-slate-900">
      <div className="container mx-auto px-4 py-8">
        <header className="mb-8 flex items-center justify-between">
          <div className="text-center flex-1">
            <h1 className="mb-2 bg-gradient-to-r from-pink-400 to-purple-400 bg-clip-text text-4xl font-bold text-transparent">
              {appInfo?.name || 'Tindarr'}
            </h1>
            <p className="text-sm text-gray-400">
              v{appInfo?.version || '...'} | API: <span className={healthStatus === 'ok' ? 'text-green-400' : 'text-red-400'}>
                {healthStatus || 'checking...'}
              </span>
            </p>
          </div>
          <div className="flex items-center gap-4">
            {user.isAdmin && (
              <a href="/admin" className="rounded-lg bg-slate-700 px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-slate-600">
                🔧 Admin
              </a>
            )}
            <button
              onClick={() => setShowPreferences(true)}
              className="rounded-lg bg-slate-700 px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-slate-600"
              title="Preferences"
            >
              ⚙️ Settings
            </button>
            <div className="text-right">
              <p className="text-sm text-gray-300">👤 {user.username}</p>
              <button
                onClick={logout}
                className="text-xs text-gray-400 hover:text-pink-400 transition-colors"
              >
                Logout
              </button>
            </div>
          </div>
        </header>

        <main className="pb-8">
          <SwipeDeck />
        </main>

        <footer className="text-center">
          <p className="text-sm text-gray-400">👈 Swipe left to pass • Swipe right to like 👉</p>
          <p className="mt-2 text-xs text-gray-500">or use the buttons below</p>
        </footer>
      </div>

      <PreferencesModal isOpen={showPreferences} onClose={() => setShowPreferences(false)} />
    </div>
  )
}

function App() {
  const { user, loading: authLoading } = useAuth()

  if (authLoading) {
    return (
      <div className="flex min-h-screen items-center justify-center bg-gradient-to-br from-slate-900 via-purple-900 to-slate-900">
        <div className="text-center">
          <div className="mb-4 inline-block h-12 w-12 animate-spin rounded-full border-4 border-pink-500 border-t-transparent"></div>
          <p className="text-xl text-gray-300">Loading...</p>
        </div>
      </div>
    )
  }

  if (!user) {
    return <Login />
  }

  return (
    <Routes>
      <Route path="/" element={<AppLayout />} />
      <Route path="/admin" element={<AdminConsole />} />
    </Routes>
  )
}

export default App
