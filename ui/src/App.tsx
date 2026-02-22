import { useEffect, useRef, useState } from 'react'
import { Routes, Route, useNavigate, Link, Outlet } from 'react-router-dom'
import SwipeDeck from './components/SwipeDeck'
import Login from './components/Login'
import LoginSplashOverlay from './components/LoginSplashOverlay'
import MatchesModal from './components/MatchesModal'
import PreferencesModal from './components/PreferencesModal'
import AdminConsole from './pages/AdminConsole'
import MyLikes from './pages/MyLikes'
import Rooms from './pages/Rooms'
import Room from './pages/Room'
import { useAuth } from './contexts/AuthContext'
import { apiClient, InfoResponse } from './lib/api'

function AppLayout() {
  const { user, loading: authLoading, logout } = useAuth()
  const navigate = useNavigate()
  const [appInfo, setAppInfo] = useState<InfoResponse | null>(null)
  const [showPreferences, setShowPreferences] = useState(false)
  const [showMatches, setShowMatches] = useState(false)
  const [menuOpen, setMenuOpen] = useState(false)
  const [deckRefreshKey, setDeckRefreshKey] = useState(0)
  const menuRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    const fetchData = async () => {
      try {
        const info = await apiClient.getInfo()
        setAppInfo(info)
      } catch (error) {
        console.error('Failed to fetch API data:', error)
      }
    }

    fetchData()
  }, [])

  useEffect(() => {
    function handleClickOutside(event: MouseEvent) {
      if (menuRef.current && !menuRef.current.contains(event.target as Node)) {
        setMenuOpen(false)
      }
    }
    if (menuOpen) {
      document.addEventListener('mousedown', handleClickOutside)
      return () => document.removeEventListener('mousedown', handleClickOutside)
    }
  }, [menuOpen])

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

  const menuItems = [
    { id: 'swipe', label: 'Swipe to add', icon: '👆', href: '/', action: () => { navigate('/'); setMenuOpen(false) } },
    { id: 'rooms', label: 'Rooms', icon: '📺', href: '/rooms', action: () => { setMenuOpen(false) } },
    { id: 'matches', label: 'Matches', icon: '💕', action: () => { setShowMatches(true); setMenuOpen(false) } },
    { id: 'likes', label: 'My Likes', icon: '❤️', href: '/likes', action: () => { navigate('/likes'); setMenuOpen(false) } },
    { id: 'preferences', label: 'Preferences', icon: '⚙️', action: () => { setShowPreferences(true); setMenuOpen(false) } },
    ...(user.isAdmin ? [{ id: 'admin', label: 'Admin', icon: '🔧', href: '/admin', action: () => { navigate('/admin'); setMenuOpen(false) } }] : []),
    { id: 'logout', label: 'Logout', icon: '👋', action: () => { logout(); setMenuOpen(false) } },
  ]

  return (
    <div className="min-h-screen bg-gradient-to-br from-slate-900 via-purple-900 to-slate-900">
      <div className="container mx-auto px-4 py-8">
        <header className="mb-8 flex items-center justify-center">
          <div className="relative" ref={menuRef}>
            <button
              type="button"
              onClick={() => setMenuOpen((o) => !o)}
              className="flex items-center gap-2 rounded-2xl border border-pink-500/40 bg-slate-800/90 px-5 py-3 shadow-lg shadow-purple-900/30 ring-2 ring-pink-400/20 transition-all hover:border-pink-400/60 hover:bg-slate-700/90 hover:shadow-pink-500/20 focus:outline-none focus:ring-2 focus:ring-pink-400"
              aria-expanded={menuOpen}
              aria-haspopup="true"
            >
              <span className="bg-gradient-to-r from-pink-400 to-purple-400 bg-clip-text text-xl font-bold text-transparent">
                {appInfo?.name || 'Tindarr'}
              </span>
              <span className="text-gray-400">
                v{appInfo?.version || '...'}
              </span>
              <span
                className={`inline-block text-pink-400 transition-transform ${menuOpen ? 'rotate-180' : ''}`}
                aria-hidden
              >
                ▼
              </span>
            </button>

            {menuOpen && (
              <div
                className="absolute left-1/2 top-full z-50 mt-2 w-56 -translate-x-1/2 overflow-hidden rounded-2xl border border-pink-500/30 bg-slate-800/95 shadow-xl shadow-black/40 backdrop-blur-sm"
                role="menu"
              >
                <ul className="max-h-[70vh] overflow-y-auto py-2">
                  {menuItems.map((item) => (
                    <li key={item.id} role="none">
                      {'href' in item && item.href ? (
                        <Link
                          to={item.href}
                          role="menuitem"
                          onClick={item.action}
                          className="flex w-full items-center gap-3 px-4 py-2.5 text-left text-gray-200 transition-colors hover:bg-pink-500/20 hover:text-white"
                        >
                          <span className="text-lg opacity-80">{item.icon}</span>
                          <span className="font-medium">{item.label}</span>
                        </Link>
                      ) : (
                        <button
                          type="button"
                          role="menuitem"
                          onClick={item.action ?? (() => setMenuOpen(false))}
                          className="flex w-full items-center gap-3 px-4 py-2.5 text-left text-gray-200 transition-colors hover:bg-pink-500/20 hover:text-white"
                        >
                          <span className="text-lg opacity-80">{item.icon}</span>
                          <span className="font-medium">{item.label}</span>
                        </button>
                      )}
                    </li>
                  ))}
                </ul>
              </div>
            )}
          </div>
        </header>

        <main className="pb-8">
          <SwipeDeck key={deckRefreshKey} />
        </main>

        <footer className="text-center">
          <p className="text-sm text-gray-400">
            👈 Swipe left to pass • Swipe right to like 👉
          </p>
          <p className="mt-2 text-xs text-gray-500">or use the buttons below</p>
        </footer>
      </div>

      <MatchesModal
        isOpen={showMatches}
        onClose={() => setShowMatches(false)}
      />
      <PreferencesModal
        isOpen={showPreferences}
        onClose={() => setShowPreferences(false)}
        onAfterSave={() => setDeckRefreshKey((k) => k + 1)}
      />
    </div>
  )
}

function App() {
  const { user, loading: authLoading } = useAuth()
  const prevUserRef = useRef<typeof user>(null)
  const splashTimeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null)
  const [showPostLoginSplash, setShowPostLoginSplash] = useState(false)

  useEffect(() => {
    if (user && prevUserRef.current === null) {
      setShowPostLoginSplash(true)
      prevUserRef.current = user
      splashTimeoutRef.current = setTimeout(() => {
        setShowPostLoginSplash(false)
        splashTimeoutRef.current = null
      }, 2000)
    }
    if (!user) {
      prevUserRef.current = null
      if (splashTimeoutRef.current) {
        clearTimeout(splashTimeoutRef.current)
        splashTimeoutRef.current = null
      }
      setShowPostLoginSplash(false)
    }
  }, [user])

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
    <>
      <Routes>
        <Route path="/rooms/:roomId" element={<Room />} />
        <Route path="/rooms" element={<Rooms />} />
        <Route path="/likes" element={<MyLikes />} />
        <Route path="/admin" element={<AdminConsole />} />
        <Route path="/" element={<Outlet />}>
          <Route index element={<AppLayout />} />
        </Route>
      </Routes>
      {showPostLoginSplash && <LoginSplashOverlay />}
    </>
  )
}

export default App
