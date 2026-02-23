import { useEffect, useState } from 'react'
import { useAuth } from '../contexts/AuthContext'
import { useNavigate } from 'react-router-dom'

interface DatabaseStats {
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

interface MovieDetails {
  id: number
  title: string
  overview: string
  poster_path: string | null
  backdrop_path: string | null
  release_date: string
  vote_average: number
  genres: Array<{ id: number; name: string }>
  release_dates?: {
    results: Array<{
      iso_3166_1: string
      release_dates: Array<{
        certification: string
        iso_639_1: string
        release_date: string
        type: number
      }>
    }>
  }
}

interface RadarrSettings {
  apiUrl: string
  apiKey: string
  defaultQualityProfileId: number
  defaultRootFolderId: number
  autoAddMovies: boolean
  enabled: boolean
  autoAddIntervalSeconds?: number
}

interface QualityProfile {
  id: number
  name: string
}

interface RootFolder {
  id: number
  path: string
  freeSpace: number
  totalSpace: number
}

export default function AdminConsole() {
  const { user } = useAuth()
  const navigate = useNavigate()
  const [stats, setStats] = useState<DatabaseStats | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [activeTab, setActiveTab] = useState<'overview' | 'users' | 'accepted' | 'radarr'>('overview')
  const [selectedMovie, setSelectedMovie] = useState<MovieDetails | null>(null)
  const [loadingMovie, setLoadingMovie] = useState(false)
  const [moviePosters, setMoviePosters] = useState<Record<number, string>>({})
  
  // Radarr settings state
  const [radarrSettings, setRadarrSettings] = useState<RadarrSettings>({
    apiUrl: '',
    apiKey: '',
    defaultQualityProfileId: 0,
    defaultRootFolderId: 0,
    autoAddMovies: false,
    enabled: false,
    autoAddIntervalSeconds: 300
  })
  const [qualityProfiles, setQualityProfiles] = useState<QualityProfile[]>([])
  const [rootFolders, setRootFolders] = useState<RootFolder[]>([])
  const [savingSettings, setSavingSettings] = useState(false)
  const [testingConnection, setTestingConnection] = useState(false)
  const [settingsMessage, setSettingsMessage] = useState<{ type: 'success' | 'error'; text: string } | null>(null)
  const [addingMovieId, setAddingMovieId] = useState<string | null>(null)
  const [userActionMessage, setUserActionMessage] = useState<{ type: 'success' | 'error'; text: string } | null>(null)
  const [hasValidatedConnection, setHasValidatedConnection] = useState(false)

  // Fetch poster for a movie
  const fetchPosterPath = async (movieId: number) => {
    if (moviePosters[movieId]) return // Already cached
    
    try {
      const response = await fetch(`http://localhost:6565/api/v1/movies/${movieId}`)
      if (response.ok) {
        const data = await response.json()
        if (data.poster_path) {
          setMoviePosters(prev => ({ ...prev, [movieId]: data.poster_path }))
        }
      }
    } catch (err) {
      console.error(`Failed to fetch poster for movie ${movieId}:`, err)
    }
  }

  // Fetch all posters when accepted movies tab is active
  useEffect(() => {
    if (activeTab === 'accepted' && stats?.acceptedMovies) {
      stats.acceptedMovies.forEach(movie => {
        fetchPosterPath(movie.movieId)
      })
    }
  }, [activeTab, stats?.acceptedMovies])

  // Fetch all posters when users tab is active
  useEffect(() => {
    if (activeTab === 'users' && stats?.users) {
      const allLikedMovies = stats.users.flatMap(u => u.likedMovies)
      const uniqueMovies = [...new Set(allLikedMovies)]
      uniqueMovies.forEach(movieId => {
        fetchPosterPath(movieId)
      })
    }
  }, [activeTab, stats?.users])

  // Check if user is admin
  useEffect(() => {
    if (!user || user.username !== 'admin') {
      navigate('/')
    }
  }, [user, navigate])

  // Fetch database stats
  useEffect(() => {
    if (user?.username !== 'admin') return

    const fetchStats = async () => {
      try {
        setLoading(true)
        const response = await fetch('http://localhost:6565/api/v1/admin/stats')
        if (!response.ok) throw new Error('Failed to fetch stats')
        const data: DatabaseStats = await response.json()
        setStats(data)
        setError(null)
      } catch (err) {
        setError(err instanceof Error ? err.message : 'Failed to fetch database stats')
      } finally {
        setLoading(false)
      }
    }

    fetchStats()
  }, [user])

  const fetchMovieDetails = async (movieId: number) => {
    setLoadingMovie(true)
    try {
      const response = await fetch(
        `http://localhost:6565/api/v1/movies/${movieId}`
      )
      if (!response.ok) throw new Error('Failed to fetch movie')
      const data: MovieDetails = await response.json()
      setSelectedMovie(data)
    } catch (err) {
      console.error('Failed to fetch movie details:', err)
    } finally {
      setLoadingMovie(false)
    }
  }

  // Fetch Radarr settings when tab is active
  useEffect(() => {
    if (activeTab === 'radarr' || activeTab === 'accepted') {
      fetchRadarrSettings()
    }
  }, [activeTab])

  const fetchRadarrSettings = async () => {
    try {
      const response = await fetch('http://localhost:6565/api/v1/radarr/settings')
      if (response.ok) {
        const data = await response.json()
        // Backend no longer returns the API key; preserve security by not populating it
        // Keep existing apiKey in state if one exists, otherwise blank it
        setRadarrSettings(prev => ({
          ...prev,
          apiUrl: data.apiUrl || '',
          apiKey: prev.apiKey || '', // preserve in-memory key
          defaultQualityProfileId: data.defaultQualityProfileId ?? 0,
          defaultRootFolderId: data.defaultRootFolderId ?? 0,
          autoAddMovies: !!data.autoAddMovies,
          enabled: !!data.enabled,
          autoAddIntervalSeconds: data.autoAddIntervalSeconds ?? 300
        }))
        // Only reset validation if we don't have settings configured
        setHasValidatedConnection(data.hasApiKey && data.defaultQualityProfileId > 0 && data.defaultRootFolderId > 0)
      }
    } catch (err) {
      console.error('Failed to fetch Radarr settings:', err)
    }
  }

  const fetchQualityProfiles = async () => {
    try {
      const response = await fetch('http://localhost:6565/api/v1/radarr/quality-profiles')
      if (response.ok) {
        const data = await response.json()
        setQualityProfiles(data)
        if (data.length > 0 && radarrSettings.defaultQualityProfileId === 0) {
          setRadarrSettings(prev => ({ ...prev, defaultQualityProfileId: data[0].id }))
        }
      }
    } catch (err) {
      console.error('Failed to fetch quality profiles:', err)
    }
  }

  const fetchRootFolders = async () => {
    try {
      const response = await fetch('http://localhost:6565/api/v1/radarr/root-folders')
      if (response.ok) {
        const data = await response.json()
        setRootFolders(data)
        if (data.length > 0 && radarrSettings.defaultRootFolderId === 0) {
          setRadarrSettings(prev => ({ ...prev, defaultRootFolderId: data[0].id }))
        }
      }
    } catch (err) {
      console.error('Failed to fetch root folders:', err)
    }
  }

  const handleTestConnection = async () => {
    setTestingConnection(true)
    setSettingsMessage(null)
    try {
      // First, save the current settings
      const saveResponse = await fetch('http://localhost:6565/api/v1/radarr/settings', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(radarrSettings)
      })
      const saveData = await saveResponse.json()
      if (!saveData.success) {
        setSettingsMessage({ type: 'error', text: saveData.error || 'Failed to save settings before testing' })
        setTestingConnection(false)
        return
      }

      // Reload saved settings
      await fetchRadarrSettings()

      // Now test the connection with saved credentials
      const response = await fetch('http://localhost:6565/api/v1/radarr/test-connection', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          apiUrl: radarrSettings.apiUrl,
          apiKey: radarrSettings.apiKey
        })
      })
      const data = await response.json()
      if (data.success) {
        setSettingsMessage({ type: 'success', text: 'Settings saved and connection successful' })
        // Fetch profiles and folders if connection is successful
        await fetchQualityProfiles()
        await fetchRootFolders()
        setHasValidatedConnection(true)
      } else {
        setSettingsMessage({ type: 'error', text: data.error || 'Connection failed' })
        setHasValidatedConnection(false)
      }
    } catch (err) {
      setSettingsMessage({ type: 'error', text: 'Failed to test connection' })
      setHasValidatedConnection(false)
    } finally {
      setTestingConnection(false)
    }
  }

  const handleSaveSettings = async () => {
    setSavingSettings(true)
    setSettingsMessage(null)
    try {
      const response = await fetch('http://localhost:6565/api/v1/radarr/settings', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(radarrSettings)
      })
      const data = await response.json()
      if (data.success) {
        setSettingsMessage({ type: 'success', text: data.message })
        // After successful save, settings are now persisted and validated
        if (radarrSettings.defaultQualityProfileId > 0 && radarrSettings.defaultRootFolderId > 0) {
          setHasValidatedConnection(true)
        }
      } else {
        setSettingsMessage({ type: 'error', text: data.error || 'Failed to save settings' })
      }
    } catch (err) {
      setSettingsMessage({ type: 'error', text: 'Failed to save settings' })
    } finally {
      setSavingSettings(false)
    }
  }

  const handleAddMovieToRadarr = async (acceptedMovieId: string, qualityProfileId: number, rootFolderId: number) => {
    setAddingMovieId(acceptedMovieId)
    try {
      const response = await fetch('http://localhost:6565/api/v1/radarr/add-movie', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          acceptedMovieId,
          qualityProfileId,
          rootFolderId
        })
      })
      const data = await response.json()
      if (data.success) {
        setSettingsMessage({ type: 'success', text: data.message })
        // Refresh stats to show updated status
        const statsResponse = await fetch('http://localhost:6565/api/v1/admin/stats')
        if (statsResponse.ok) {
          const statsData: DatabaseStats = await statsResponse.json()
          setStats(statsData)
        }
      } else {
        setSettingsMessage({ type: 'error', text: data.error || 'Failed to add movie' })
      }
    } catch (err) {
      setSettingsMessage({ type: 'error', text: 'Failed to add movie to Radarr' })
    } finally {
      setAddingMovieId(null)
    }
  }

  const handleAutoAddMovies = async () => {
    setAddingMovieId('auto')
    try {
      const response = await fetch('http://localhost:6565/api/v1/radarr/auto-add-accepted-movies', {
        method: 'POST'
      })
      const data = await response.json()
      if (data.success) {
        setSettingsMessage({ type: 'success', text: `Added ${data.addedCount} of ${data.totalCount} movies` })
        // Refresh stats
        const statsResponse = await fetch('http://localhost:6565/api/v1/admin/stats')
        if (statsResponse.ok) {
          const statsData: DatabaseStats = await statsResponse.json()
          setStats(statsData)
        }
      } else {
        setSettingsMessage({ type: 'error', text: data.error || 'Auto-add failed' })
      }
    } catch (err) {
      setSettingsMessage({ type: 'error', text: 'Failed to auto-add movies' })
    } finally {
      setAddingMovieId(null)
    }
  }

  const handleResetUserPassword = async (username: string) => {
    const adminPassword = window.prompt('Enter admin password to reset this user password:')
    if (!adminPassword) return
    try {
      const response = await fetch('http://localhost:6565/api/v1/admin/clear-password', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ username, adminPassword })
      })
      if (response.ok) {
        setUserActionMessage({ type: 'success', text: `Password reset for ${username}. Next login will require setting a new password.` })
      } else {
        const data = await response.json().catch(() => ({}))
        setUserActionMessage({ type: 'error', text: data.error || 'Failed to reset password' })
      }
    } catch (err) {
      setUserActionMessage({ type: 'error', text: 'Failed to reset password' })
    }
  }

  if (user?.username !== 'admin') {
    return (
      <div className="flex h-screen items-center justify-center">
        <div className="text-center">
          <p className="text-2xl text-red-400">Access Denied</p>
          <p className="mt-2 text-gray-400">Admin access required</p>
        </div>
      </div>
    )
  }

  if (loading) {
    return (
      <div className="flex h-screen items-center justify-center">
        <div className="text-center">
          <div className="mb-4 inline-block h-12 w-12 animate-spin rounded-full border-4 border-pink-500 border-t-transparent"></div>
          <p className="text-xl text-gray-300">Loading stats...</p>
        </div>
      </div>
    )
  }

  if (error) {
    return (
      <div className="flex h-screen items-center justify-center">
        <div className="text-center">
          <p className="text-2xl text-red-400">Error</p>
          <p className="mt-2 text-gray-400">{error}</p>
        </div>
      </div>
    )
  }

  return (
    <div className="min-h-screen bg-gradient-to-br from-gray-900 via-gray-800 to-gray-900 p-8">
      <div className="mx-auto max-w-6xl">
        {/* Header */}
        <div className="mb-8">
          <button
            onClick={() => navigate('/')}
            className="mb-4 flex items-center gap-2 text-gray-400 hover:text-white transition-colors"
          >
            <svg className="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M10 19l-7-7m0 0l7-7m-7 7h18" />
            </svg>
            <span>Back to App</span>
          </button>
          <h1 className="text-4xl font-bold text-white">Admin Console</h1>
          <p className="mt-2 text-gray-400">Database Management & Statistics</p>
        </div>

        {/* Navigation Tabs */}
        <div className="mb-8 flex gap-4 border-b border-gray-700 overflow-x-auto">
          <button
            onClick={() => setActiveTab('overview')}
            className={`px-4 py-2 font-semibold transition-colors whitespace-nowrap ${
              activeTab === 'overview'
                ? 'border-b-2 border-pink-500 text-pink-500'
                : 'text-gray-400 hover:text-gray-200'
            }`}
          >
            Overview
          </button>
          <button
            onClick={() => setActiveTab('users')}
            className={`px-4 py-2 font-semibold transition-colors whitespace-nowrap ${
              activeTab === 'users'
                ? 'border-b-2 border-pink-500 text-pink-500'
                : 'text-gray-400 hover:text-gray-200'
            }`}
          >
            Users
          </button>
          <button
            onClick={() => setActiveTab('accepted')}
            className={`px-4 py-2 font-semibold transition-colors whitespace-nowrap ${
              activeTab === 'accepted'
                ? 'border-b-2 border-pink-500 text-pink-500'
                : 'text-gray-400 hover:text-gray-200'
            }`}
          >
            Accepted Movies
          </button>
          <button
            onClick={() => setActiveTab('radarr')}
            className={`px-4 py-2 font-semibold transition-colors whitespace-nowrap ${
              activeTab === 'radarr'
                ? 'border-b-2 border-pink-500 text-pink-500'
                : 'text-gray-400 hover:text-gray-200'
            }`}
          >
            Radarr Settings
          </button>
        </div>

        {/* Overview Tab */}
        {activeTab === 'overview' && stats && (
          <div className="space-y-6">
            <div className="grid grid-cols-1 gap-6 md:grid-cols-3">
              <div className="rounded-lg bg-gray-800 p-6">
                <p className="text-sm text-gray-400">Total Users</p>
                <p className="mt-2 text-4xl font-bold text-pink-500">{stats.totalUsers}</p>
              </div>
              <div className="rounded-lg bg-gray-800 p-6">
                <p className="text-sm text-gray-400">Total Interactions</p>
                <p className="mt-2 text-4xl font-bold text-pink-500">{stats.totalInteractions}</p>
              </div>
              <div className="rounded-lg bg-gray-800 p-6">
                <p className="text-sm text-gray-400">Consensus Movies</p>
                <p className="mt-2 text-4xl font-bold text-pink-500">{stats.totalAcceptedMovies}</p>
              </div>
            </div>
          </div>
        )}

        {/* Radarr Tab */}
        {activeTab === 'radarr' && stats && (
          <div className="space-y-6">
            <div className="rounded-lg bg-gray-800 p-6">
              <h3 className="text-xl font-bold text-white mb-4">Radarr Integration</h3>
              <div className="space-y-4">
                <div>
                  <label className="block text-sm font-semibold text-white mb-2">Radarr API URL</label>
                  <input
                    type="text"
                    placeholder="http://localhost:7878"
                    value={radarrSettings.apiUrl}
                    onChange={(e) => setRadarrSettings({ ...radarrSettings, apiUrl: e.target.value })}
                    className="w-full rounded bg-gray-700 px-3 py-2 text-white placeholder-gray-500 focus:outline-none focus:ring-2 focus:ring-pink-500"
                  />
                </div>
                <div>
                  <label className="block text-sm font-semibold text-white mb-2">Radarr API Key</label>
                  <input
                    type="password"
                    placeholder="Your Radarr API Key"
                    value={radarrSettings.apiKey}
                    onChange={(e) => setRadarrSettings({ ...radarrSettings, apiKey: e.target.value })}
                    className="w-full rounded bg-gray-700 px-3 py-2 text-white placeholder-gray-500 focus:outline-none focus:ring-2 focus:ring-pink-500"
                  />
                </div>
                <button
                  onClick={handleTestConnection}
                  disabled={testingConnection || !radarrSettings.apiUrl || !radarrSettings.apiKey}
                  className="w-full rounded bg-blue-600 px-4 py-2 font-semibold text-white hover:bg-blue-700 disabled:bg-gray-600 transition-colors"
                >
                  {testingConnection ? 'Testing...' : 'Test Connection'}
                </button>
              </div>

              {hasValidatedConnection && qualityProfiles.length > 0 && (
                <div className="mt-4">
                  <label className="block text-sm font-semibold text-white mb-2">Default Quality Profile</label>
                  <select
                    value={radarrSettings.defaultQualityProfileId}
                    onChange={(e) => setRadarrSettings({ ...radarrSettings, defaultQualityProfileId: parseInt(e.target.value) })}
                    className="w-full rounded bg-gray-700 px-3 py-2 text-white focus:outline-none focus:ring-2 focus:ring-pink-500"
                  >
                    <option value={0}>Select Quality Profile</option>
                    {qualityProfiles.map(profile => (
                      <option key={profile.id} value={profile.id}>{profile.name}</option>
                    ))}
                  </select>
                </div>
              )}

              {hasValidatedConnection && rootFolders.length > 0 && (
                <div className="mt-4">
                  <label className="block text-sm font-semibold text-white mb-2">Default Root Folder</label>
                  <select
                    value={radarrSettings.defaultRootFolderId}
                    onChange={(e) => setRadarrSettings({ ...radarrSettings, defaultRootFolderId: parseInt(e.target.value) })}
                    className="w-full rounded bg-gray-700 px-3 py-2 text-white focus:outline-none focus:ring-2 focus:ring-pink-500"
                  >
                    <option value={0}>Select Root Folder</option>
                    {rootFolders.map(folder => (
                      <option key={folder.id} value={folder.id}>{folder.path}</option>
                    ))}
                  </select>
                </div>
              )}

              <div className="mt-4 flex items-center justify-between">
                <div>
                  <p className="text-white font-semibold">Enable Auto-add</p>
                  <p className="text-sm text-gray-400 mt-1">Automatically add consensus movies to Radarr</p>
                </div>
                <button
                    onClick={() => {
                      const nextAuto = !radarrSettings.autoAddMovies
                      setRadarrSettings({
                        ...radarrSettings,
                        autoAddMovies: nextAuto,
                        // Ensure Radarr integration is considered enabled when auto-add is on
                        enabled: radarrSettings.enabled || nextAuto
                      })
                    }}
                  className={`relative inline-flex h-8 w-14 items-center rounded-full transition-colors ${
                    radarrSettings.autoAddMovies ? 'bg-pink-500' : 'bg-gray-600'
                  }`}
                >
                  <span
                    className={`inline-block h-6 w-6 transform rounded-full bg-white transition-transform ${
                      radarrSettings.autoAddMovies ? 'translate-x-7' : 'translate-x-1'
                    }`}
                  />
                </button>
              </div>

              <button
                onClick={handleSaveSettings}
                disabled={savingSettings || !hasValidatedConnection}
                className="mt-6 w-full rounded bg-pink-600 px-4 py-2 font-semibold text-white hover:bg-pink-700 disabled:bg-gray-600 transition-colors"
              >
                {savingSettings ? 'Saving...' : 'Save Settings'}
              </button>

              {settingsMessage && (
                <div className={`mt-4 rounded px-4 py-3 ${
                  settingsMessage.type === 'success'
                    ? 'bg-green-500/10 border border-green-500/30 text-green-400'
                    : 'bg-red-500/10 border border-red-500/30 text-red-400'
                }`}>
                  {settingsMessage.text}
                </div>
              )}

              <div className="mt-6 rounded-lg bg-gray-900/50 p-4 border border-gray-800">
                <h4 className="text-white font-semibold mb-3">Auto-add Interval</h4>
                <label className="block text-sm font-semibold text-white mb-2">Add interval (seconds)</label>
                <input
                  type="number"
                  min={30}
                  step={30}
                  value={radarrSettings.autoAddIntervalSeconds ?? 300}
                  onChange={(e) =>
                    setRadarrSettings({
                      ...radarrSettings,
                      autoAddIntervalSeconds: Math.max(30, Number(e.target.value) || 300)
                    })
                  }
                  className="w-full rounded bg-gray-700 px-3 py-2 text-white placeholder-gray-500 focus:outline-none focus:ring-2 focus:ring-pink-500"
                />
                <p className="mt-2 text-xs text-gray-400">Minimum 30 seconds. Controls how often Tindarr auto-adds accepted movies in the background.</p>
              </div>
            </div>
          </div>
        )}

        {/* Users Tab */}
        {activeTab === 'users' && stats && (
          <div className="space-y-6">
            {/* Summary Stats */}
            <div className="grid grid-cols-1 gap-4 md:grid-cols-3">
              <div className="rounded-lg bg-gray-800 p-4">
                <p className="text-sm text-gray-400">Total Users</p>
                <p className="mt-1 text-3xl font-bold text-white">{stats.totalUsers}</p>
              </div>
              <div className="rounded-lg bg-gray-800 p-4">
                <p className="text-sm text-gray-400">Regular Users</p>
                <p className="mt-1 text-3xl font-bold text-blue-400">{stats.regularUsers}</p>
              </div>
              <div className="rounded-lg bg-gray-800 p-4">
                <p className="text-sm text-gray-400">Admin Users</p>
                <p className="mt-1 text-3xl font-bold text-yellow-400">{stats.totalUsers - stats.regularUsers}</p>
              </div>
            </div>

            {/* Users Table */}
            <div className="rounded-lg bg-gray-800 overflow-hidden">
              <table className="w-full text-left text-gray-300">
                <thead className="bg-gray-900 text-gray-200">
                  <tr>
                    <th className="px-6 py-3">Username</th>
                    <th className="px-6 py-3">Email</th>
                    <th className="px-6 py-3">Liked</th>
                    <th className="px-6 py-3">Noped</th>
                    <th className="px-6 py-3">Skipped</th>
                    <th className="px-6 py-3">Created</th>
                    <th className="px-6 py-3">Actions</th>
                  </tr>
                </thead>
                <tbody>
                  {stats.users.map((u) => (
                    <tr key={u.id} className="border-t border-gray-700 hover:bg-gray-750">
                      <td className="px-6 py-3 font-semibold text-white">
                        {u.username}
                        {u.isAdmin && <span className="ml-2 text-xs text-yellow-400">(ADMIN)</span>}
                      </td>
                      <td className="px-6 py-3 text-sm">{u.email}</td>
                      <td className="px-6 py-3">
                        <span className="inline-block rounded-full bg-green-500/20 px-3 py-1 text-sm text-green-400">
                          {u.likedCount}
                        </span>
                      </td>
                      <td className="px-6 py-3">
                        <span className="inline-block rounded-full bg-red-500/20 px-3 py-1 text-sm text-red-400">
                          {u.nopedCount}
                        </span>
                      </td>
                      <td className="px-6 py-3">
                        <span className="inline-block rounded-full bg-gray-500/20 px-3 py-1 text-sm text-gray-400">
                          {u.skippedCount}
                        </span>
                      </td>
                      <td className="px-6 py-3 text-sm text-gray-500">
                        {new Date(u.createdAt).toLocaleDateString()}
                      </td>
                      <td className="px-6 py-3 text-sm">
                        {u.isAdmin ? (
                          <span className="text-gray-500">—</span>
                        ) : (
                          <button
                            onClick={() => handleResetUserPassword(u.username)}
                            className="rounded bg-red-600 px-3 py-1 text-xs font-semibold text-white hover:bg-red-700 transition-colors"
                          >
                            Reset Password
                          </button>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
              {userActionMessage && (
                <div className={`px-6 py-4 ${userActionMessage.type === 'success' ? 'text-green-400' : 'text-red-400'} bg-gray-900 border-t border-gray-700`}>
                  {userActionMessage.text}
                </div>
              )}
            </div>

            {/* Liked Movies by User */}
            <div className="space-y-4">
              <h3 className="text-xl font-bold text-white">Liked Movies by User</h3>
              {stats.users.map((u) => (
                <div key={u.id} className="rounded-lg bg-gray-800 p-4">
                  <div className="flex items-center justify-between mb-3">
                    <h4 className="font-semibold text-white">
                      {u.username}
                      {u.isAdmin && <span className="ml-2 text-xs text-yellow-400">(ADMIN)</span>}
                    </h4>
                    <span className="text-sm text-gray-400">{u.likedCount} liked</span>
                  </div>
                  {u.likedMovies.length === 0 ? (
                    <p className="text-sm text-gray-500">No liked movies yet</p>
                  ) : (
                    <div className="flex flex-wrap gap-3">
                      {u.likedMovies.map((movieId) => (
                        <button
                          key={movieId}
                          onClick={() => fetchMovieDetails(movieId)}
                          className="transition-transform hover:scale-105 focus:outline-none"
                        >
                          <img
                            src={
                              moviePosters[movieId]
                                ? `https://image.tmdb.org/t/p/w92${moviePosters[movieId]}`
                                : 'data:image/svg+xml,%3Csvg xmlns="http://www.w3.org/2000/svg" width="92" height="138"%3E%3Crect width="92" height="138" fill="%23374151"/%3E%3Ctext x="50%25" y="50%25" fill="%239CA3AF" text-anchor="middle" dy=".3em" font-size="12"%3ELoading...%3C/text%3E%3C/svg%3E'
                            }
                            alt={`Movie ${movieId}`}
                            className="h-32 w-22 rounded object-cover cursor-pointer bg-gray-700 shadow-lg"
                            onError={(e) => {
                              e.currentTarget.src = 'data:image/svg+xml,%3Csvg xmlns="http://www.w3.org/2000/svg" width="92" height="138"%3E%3Crect width="92" height="138" fill="%23374151"/%3E%3Ctext x="50%25" y="50%25" fill="%239CA3AF" text-anchor="middle" dy=".3em" font-size="12"%3ENo Image%3C/text%3E%3C/svg%3E'
                            }}
                            loading="lazy"
                          />
                        </button>
                      ))}
                    </div>
                  )}
                </div>
              ))}
            </div>
          </div>
        )}

        {/* Accepted Movies Tab */}
        {activeTab === 'accepted' && stats && (
          <div className="rounded-lg bg-gray-800 overflow-hidden">
            {stats.acceptedMovies.length === 0 ? (
              <div className="p-8 text-center text-gray-400">No consensus movies yet</div>
            ) : (
              <table className="w-full text-left text-gray-300">
                <thead className="bg-gray-900 text-gray-200">
                  <tr>
                    <th className="px-6 py-3">Movie</th>
                    <th className="px-6 py-3">Movie ID</th>
                    <th className="px-6 py-3">Users Who Liked</th>
                    <th className="px-6 py-3">Consensus</th>
                    <th className="px-6 py-3">Date Added</th>
                    <th className="px-6 py-3">Actions</th>
                  </tr>
                </thead>
                <tbody>
                  {stats.acceptedMovies.map((movie) => (
                    <tr key={movie.id} className="border-t border-gray-700 hover:bg-gray-750">
                      <td className="px-6 py-3">
                        <button
                          onClick={() => fetchMovieDetails(movie.movieId)}
                          className="transition-transform hover:scale-105 focus:outline-none"
                        >
                          <img
                            src={
                              moviePosters[movie.movieId]
                                ? `https://image.tmdb.org/t/p/w92${moviePosters[movie.movieId]}`
                                : 'data:image/svg+xml,%3Csvg xmlns="http://www.w3.org/2000/svg" width="92" height="138"%3E%3Crect width="92" height="138" fill="%23374151"/%3E%3Ctext x="50%25" y="50%25" fill="%239CA3AF" text-anchor="middle" dy=".3em" font-size="12"%3ELoading...%3C/text%3E%3C/svg%3E'
                            }
                            alt={`Movie ${movie.movieId}`}
                            className="h-20 w-14 rounded object-cover cursor-pointer bg-gray-700"
                            onError={(e) => {
                              e.currentTarget.src = 'data:image/svg+xml,%3Csvg xmlns="http://www.w3.org/2000/svg" width="92" height="138"%3E%3Crect width="92" height="138" fill="%23374151"/%3E%3Ctext x="50%25" y="50%25" fill="%239CA3AF" text-anchor="middle" dy=".3em" font-size="12"%3ENo Image%3C/text%3E%3C/svg%3E'
                            }}
                            loading="lazy"
                          />
                        </button>
                      </td>
                      <td className="px-6 py-3 font-semibold text-white">{movie.movieId}</td>
                      <td className="px-6 py-3">
                        <span className="inline-block rounded-full bg-blue-500/20 px-3 py-1 text-sm text-blue-400">
                          {movie.userCount}
                        </span>
                      </td>
                      <td className="px-6 py-3">
                        {movie.totalUsersLiked === movie.userCount ? (
                          <span className="inline-block rounded-full bg-green-500/20 px-3 py-1 text-sm text-green-400">
                            All {movie.totalUsersLiked}
                          </span>
                        ) : (
                          <span className="text-gray-500">{movie.userCount}/{movie.totalUsersLiked}</span>
                        )}
                      </td>
                      <td className="px-6 py-3 text-sm text-gray-500">
                        {new Date(movie.createdAt).toLocaleDateString()}
                      </td>
                      <td className="px-6 py-3">
                        {movie.addedToRadarr ? (
                          <span className="inline-block rounded-full bg-green-500/20 px-3 py-1 text-sm text-green-400">✓ Added to Radarr</span>
                        ) : (
                          <button
                            onClick={() => handleAddMovieToRadarr(
                              movie.id,
                              radarrSettings.defaultQualityProfileId,
                              radarrSettings.defaultRootFolderId
                            )}
                            disabled={addingMovieId === movie.id || !radarrSettings.apiUrl || !radarrSettings.defaultQualityProfileId || !radarrSettings.defaultRootFolderId}
                            className="flex items-center gap-2 rounded bg-pink-600 px-3 py-1.5 text-sm font-semibold text-white hover:bg-pink-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
                          >
                            {addingMovieId === movie.id ? (
                              <>
                                <div className="h-4 w-4 animate-spin rounded-full border-2 border-white border-t-transparent"></div>
                                <span>Adding...</span>
                              </>
                            ) : (
                              <>
                                <svg className="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 4v16m8-8H4" />
                                </svg>
                                <span>Add to Radarr</span>
                              </>
                            )}
                          </button>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </div>
        )}
      </div>

      {/* Movie Details Modal */}
      {selectedMovie && (
        <div
          className="fixed inset-0 z-50 flex items-center justify-center bg-black/80 p-4"
          onClick={() => setSelectedMovie(null)}
        >
          <div
            className="relative max-w-4xl w-full max-h-[90vh] overflow-y-auto rounded-lg bg-gray-900 shadow-2xl"
            onClick={(e) => e.stopPropagation()}
          >
            {/* Close Button */}
            <button
              onClick={() => setSelectedMovie(null)}
              className="absolute top-4 right-4 z-10 rounded-full bg-black/50 p-2 text-white hover:bg-black/70 transition-colors"
            >
              <svg className="h-6 w-6" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
              </svg>
            </button>

            {/* Backdrop */}
            {selectedMovie.backdrop_path && (
              <div className="relative h-64 w-full overflow-hidden">
                <img
                  src={`https://image.tmdb.org/t/p/w1280${selectedMovie.backdrop_path}`}
                  alt={selectedMovie.title}
                  className="w-full h-full object-cover"
                />
                <div className="absolute inset-0 bg-gradient-to-t from-gray-900 to-transparent"></div>
              </div>
            )}

            <div className="p-6">
              <div className="flex gap-6">
                {/* Poster */}
                {selectedMovie.poster_path && (
                  <img
                    src={`https://image.tmdb.org/t/p/w342${selectedMovie.poster_path}`}
                    alt={selectedMovie.title}
                    className="w-48 rounded-lg shadow-lg"
                  />
                )}

                {/* Details */}
                <div className="flex-1">
                  <h2 className="text-3xl font-bold text-white mb-2">{selectedMovie.title}</h2>
                  
                  <div className="flex items-center gap-4 mb-4">
                    <span className="text-yellow-400 font-semibold">
                      ⭐ {selectedMovie.vote_average.toFixed(1)}
                    </span>
                    <span className="text-gray-400">{selectedMovie.release_date}</span>
                    {(() => {
                      const usCertification = selectedMovie.release_dates?.results
                        ?.find(r => r.iso_3166_1 === 'US')
                        ?.release_dates?.find(rd => rd.certification)
                        ?.certification
                      if (usCertification) {
                        return (
                          <span className="rounded bg-gray-700 px-2 py-1 text-xs font-semibold text-white border border-gray-600">
                            {usCertification}
                          </span>
                        )
                      }
                      return null
                    })()}
                  </div>

                  {selectedMovie.genres.length > 0 && (
                    <div className="flex flex-wrap gap-2 mb-4">
                      {selectedMovie.genres.map((genre) => (
                        <span
                          key={genre.id}
                          className="rounded-full bg-pink-500/20 px-3 py-1 text-sm text-pink-300"
                        >
                          {genre.name}
                        </span>
                      ))}
                    </div>
                  )}

                  <p className="text-gray-300 leading-relaxed">{selectedMovie.overview}</p>
                </div>
              </div>
            </div>
          </div>
        </div>
      )}

      {/* Loading Overlay */}
      {loadingMovie && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50">
          <div className="inline-block h-12 w-12 animate-spin rounded-full border-4 border-pink-500 border-t-transparent"></div>
        </div>
      )}
    </div>
  )
}
