import { useState, useEffect, useRef } from 'react'
import { apiClient } from '../lib/api'
import type {
  AuthResponse,
  SuggestedUrlsResponse,
  JoinAddressSettingsDto,
  PlexPinCreateResponse,
  PlexServerDto,
  JellyfinServerDto,
  EmbyServerDto,
  RadarrQualityProfileDto,
  RadarrRootFolderDto,
} from '../lib/api'
import { useAuth } from '../contexts/AuthContext'

const TMDB_SETTINGS_URL = 'https://www.themoviedb.org/settings/api'
const TMDB_GUIDE_URL = 'https://duckkota.gitlab.io/guides/tmdb/'

const DEFAULT_RADARR_URL = 'http://localhost:7878'
const DEFAULT_JELLYFIN_URL = 'http://localhost:8096'
const DEFAULT_EMBY_URL = 'http://localhost:8096'
/** Radarr Settings → General (API key under Security): base URL + /settings/general */
function radarrSettingsGeneralUrl(baseUrl: string): string {
  const base = (baseUrl || '').trim().replace(/\/$/, '')
  return base ? `${base}/settings/general` : '#'
}
const JELLYFIN_API_KEY_URL = 'https://jellyfin.org/docs/general/administration/api.html'
/** Emby API keys page: use server base URL + /web/index.html#!/apikeys */
function embyApiKeysUrl(baseUrl: string): string {
  const base = (baseUrl || '').trim().replace(/\/$/, '')
  return base ? `${base}/web/index.html#!/apikeys` : '#'
}

const STEPS = [
  { id: 'admin', title: 'Create Admin' },
  { id: 'tmdb', title: 'TMDB API' },
  { id: 'media', title: 'Media Servers' },
  { id: 'radarr', title: 'Radarr' },
  { id: 'addresses', title: 'LAN / WAN Addresses' },
  { id: 'complete', title: 'Finish' },
] as const

export default function SetupWizard({
  joinRoomId,
  onGuestJoin,
  onAdminCreated,
  onFinish,
}: {
  joinRoomId?: string | null
  onGuestJoin?: () => Promise<void>
  onAdminCreated: (response: AuthResponse) => Promise<void>
  onFinish: () => void
}) {
  const { user } = useAuth()
  const [stepIndex, setStepIndex] = useState(0)
  const [adminPassword, setAdminPassword] = useState('')
  const [adminConfirm, setAdminConfirm] = useState('')
  const [adminError, setAdminError] = useState<string | null>(null)
  const [adminLoading, setAdminLoading] = useState(false)

  const [tmdbApiKey, setTmdbApiKey] = useState('')
  const [tmdbReadToken, setTmdbReadToken] = useState('')
  const [tmdbSaving, setTmdbSaving] = useState(false)
  const [tmdbError, setTmdbError] = useState<string | null>(null)

  const [, setSuggestedUrls] = useState<SuggestedUrlsResponse | null>(null)
  const [joinAddress, setJoinAddress] = useState<JoinAddressSettingsDto | null>(null)
  const [addressesLoading, setAddressesLoading] = useState(false)
  const [addressesSaving, setAddressesSaving] = useState(false)
  const [addressesError, setAddressesError] = useState<string | null>(null)

  const [completeRunLibrarySync, setCompleteRunLibrarySync] = useState(true)
  const [completeRunTmdbBuild, setCompleteRunTmdbBuild] = useState(true)
  const [completeSubmitting, setCompleteSubmitting] = useState(false)
  const [completeError, setCompleteError] = useState<string | null>(null)
  const [completeHeartbeatActive, setCompleteHeartbeatActive] = useState(false)
  const [completeCountdown, setCompleteCountdown] = useState(10)
  const completeCountdownRef = useRef<ReturnType<typeof setInterval> | null>(null)

  // Media servers step
  const [plexPin, setPlexPin] = useState<PlexPinCreateResponse | null>(null)
  const [plexServers, setPlexServers] = useState<PlexServerDto[]>([])
  const [plexMediaLoading, setPlexMediaLoading] = useState(false)
  const [plexMediaMessage, setPlexMediaMessage] = useState<string | null>(null)
  const plexPinPollRef = useRef<ReturnType<typeof setInterval> | null>(null)
  const [jellyfinServers, setJellyfinServers] = useState<JellyfinServerDto[]>([])
  const [jellyfinBaseUrl, setJellyfinBaseUrl] = useState('')
  const [jellyfinApiKey, setJellyfinApiKey] = useState('')
  const [jellyfinSaving, setJellyfinSaving] = useState(false)
  const [jellyfinMessage, setJellyfinMessage] = useState<string | null>(null)
  const [embyServers, setEmbyServers] = useState<EmbyServerDto[]>([])
  const [embyBaseUrl, setEmbyBaseUrl] = useState('')
  const [embyApiKey, setEmbyApiKey] = useState('')
  const [embySaving, setEmbySaving] = useState(false)
  const [embyMessage, setEmbyMessage] = useState<string | null>(null)

  // Radarr step (single instance, serverId 'default')
  const RADARR_SERVICE_TYPE = 'radarr'
  const RADARR_SERVER_ID = 'default'
  const [radarrBaseUrl, setRadarrBaseUrl] = useState('')
  const [radarrApiKey, setRadarrApiKey] = useState('')
  const [radarrConfigured, setRadarrConfigured] = useState(false)
  const [radarrSaving, setRadarrSaving] = useState(false)
  const [radarrMessage, setRadarrMessage] = useState<string | null>(null)
  const [radarrReachability, setRadarrReachability] = useState<{ reachable: boolean; message: string | null } | null>(null)
  const [radarrTryDefaultLoading, setRadarrTryDefaultLoading] = useState(false)
  const [radarrShowProfileForm, setRadarrShowProfileForm] = useState(false)
  const [radarrTagLabel, setRadarrTagLabel] = useState('')
  const [radarrQualityProfileId, setRadarrQualityProfileId] = useState(0)
  const [radarrRootFolderId, setRadarrRootFolderId] = useState(0)
  const [radarrQualityProfiles, setRadarrQualityProfiles] = useState<RadarrQualityProfileDto[]>([])
  const [radarrRootFolders, setRadarrRootFolders] = useState<RadarrRootFolderDto[]>([])
  const [radarrProfileSaving, setRadarrProfileSaving] = useState(false)
  const [radarrProfilesLoading, setRadarrProfilesLoading] = useState(false)
  const [jellyfinReachability, setJellyfinReachability] = useState<{ reachable: boolean; message: string | null } | null>(null)
  const [jellyfinTryDefaultLoading, setJellyfinTryDefaultLoading] = useState(false)
  const [embyReachability, setEmbyReachability] = useState<{ reachable: boolean; message: string | null } | null>(null)
  const [embyTryDefaultLoading, setEmbyTryDefaultLoading] = useState(false)

  const stepId = STEPS[stepIndex].id
  const isAuthenticated = !!user

  // Step 1: only show when not yet logged in
  useEffect(() => {
    if (stepId === 'admin' && isAuthenticated) {
      setStepIndex(1)
    }
  }, [stepId, isAuthenticated])

  // Load media servers when entering media step
  useEffect(() => {
    if (stepId !== 'media' || !isAuthenticated) return
    setPlexMediaMessage(null)
    setPlexMediaLoading(true)
    Promise.all([apiClient.getPlexAuthStatus(), apiClient.listPlexServers()])
      .then(([, servers]) => setPlexServers(servers))
      .catch(() => setPlexServers([]))
      .finally(() => setPlexMediaLoading(false))
    apiClient.listJellyfinServers().then(setJellyfinServers).catch(() => setJellyfinServers([]))
    apiClient.listEmbyServers().then(setEmbyServers).catch(() => setEmbyServers([]))
  }, [stepId, isAuthenticated])

  // Plex pin: poll until authorized, then sync servers
  useEffect(() => {
    if (!plexPin) {
      if (plexPinPollRef.current) {
        clearInterval(plexPinPollRef.current)
        plexPinPollRef.current = null
      }
      return
    }
    const poll = async () => {
      try {
        const status = await apiClient.verifyPlexPin(plexPin.pinId)
        if (status.authorized) {
          if (plexPinPollRef.current) {
            clearInterval(plexPinPollRef.current)
            plexPinPollRef.current = null
          }
          setPlexPin(null)
          setPlexMediaMessage('Plex authorized. Syncing servers…')
          setPlexMediaLoading(true)
          apiClient.syncPlexServers().then((servers) => {
            setPlexServers(servers)
            setPlexMediaMessage(null)
          }).catch(() => setPlexMediaMessage('Failed to refresh Plex servers')).finally(() => setPlexMediaLoading(false))
        }
      } catch {
        // ignore
      }
    }
    poll()
    const id = setInterval(poll, 2000)
    plexPinPollRef.current = id
    return () => {
      if (plexPinPollRef.current) {
        clearInterval(plexPinPollRef.current)
        plexPinPollRef.current = null
      }
    }
  }, [plexPin?.pinId])

  // Load Radarr settings when entering Radarr step
  useEffect(() => {
    if (stepId !== 'radarr' || !isAuthenticated) return
    setRadarrMessage(null)
    apiClient
      .getRadarrSettings(RADARR_SERVICE_TYPE, RADARR_SERVER_ID)
      .then((data) => {
        setRadarrBaseUrl(data.baseUrl ?? '')
        setRadarrConfigured(!!data.configured && !!data.hasApiKey)
      })
      .catch(() => { setRadarrBaseUrl(''); setRadarrConfigured(false) })
  }, [stepId, isAuthenticated])

  // Fetch suggested URLs and join address when entering addresses step (admin must be logged in)
  useEffect(() => {
    if (stepId !== 'addresses' || !isAuthenticated) return
    setAddressesLoading(true)
    setAddressesError(null)
    Promise.all([apiClient.getSuggestedUrls(), apiClient.getJoinAddressSettings()])
      .then(([suggested, join]) => {
        setSuggestedUrls(suggested)
        setJoinAddress(join)
        if (join && (!join.lanHostPort || !join.wanHostPort)) {
          setJoinAddress({
            ...join,
            lanHostPort: suggested.suggestedLanHostPort ?? join.lanHostPort ?? '',
            wanHostPort: suggested.suggestedWanHostPort ?? join.wanHostPort ?? '',
          })
        }
      })
      .catch(() => setAddressesError('Could not load suggested addresses.'))
      .finally(() => setAddressesLoading(false))
  }, [stepId, isAuthenticated])

  const handleCreateAdmin = async (e: React.FormEvent) => {
    e.preventDefault()
    setAdminError(null)
    if (!adminPassword.trim()) {
      setAdminError('Password is required.')
      return
    }
    if (adminPassword !== adminConfirm) {
      setAdminError('Passwords do not match.')
      return
    }
    setAdminLoading(true)
    try {
      const resp = await apiClient.createInitialAdmin({ password: adminPassword.trim() })
      await onAdminCreated(resp)
      setStepIndex(1)
    } catch (err) {
      setAdminError(err instanceof Error ? err.message : 'Failed to create admin.')
    } finally {
      setAdminLoading(false)
    }
  }

  const handleSaveTmdb = async (e: React.FormEvent) => {
    e.preventDefault()
    setTmdbError(null)
    setTmdbSaving(true)
    try {
      // Only set tmdbApiKeySet/tmdbReadAccessTokenSet when user entered a value.
      // Sending Set: true with null clears that credential in the DB.
      const apiKeyTrimmed = tmdbApiKey.trim()
      const tokenTrimmed = tmdbReadToken.trim()
      const request: Parameters<typeof apiClient.updateAdvancedSettings>[0] = {}
      if (apiKeyTrimmed !== '') {
        request.tmdbApiKeySet = true
        request.tmdbApiKey = apiKeyTrimmed
      }
      if (tokenTrimmed !== '') {
        request.tmdbReadAccessTokenSet = true
        request.tmdbReadAccessToken = tokenTrimmed
      }
      await apiClient.updateAdvancedSettings(request)
      setStepIndex(2)
    } catch (err) {
      setTmdbError(err instanceof Error ? err.message : 'Failed to save TMDB settings.')
    } finally {
      setTmdbSaving(false)
    }
  }

  const handlePlexCreatePin = async () => {
    setPlexMediaMessage(null)
    try {
      const pin = await apiClient.createPlexPin()
      setPlexPin(pin)
    } catch (err) {
      setPlexMediaMessage(err instanceof Error ? err.message : 'Failed to create Plex pin')
    }
  }

  const handleJellyfinAdd = async (e: React.FormEvent) => {
    e.preventDefault()
    const raw = jellyfinBaseUrl.trim()
    const baseUrl = raw.startsWith('http://') || raw.startsWith('https://') ? raw : `http://${raw}`
    const apiKey = jellyfinApiKey.trim()
    if (!raw || !apiKey) {
      setJellyfinMessage('URL and API Key are required')
      return
    }
    setJellyfinMessage(null)
    setJellyfinSaving(true)
    try {
      await apiClient.putJellyfinSettings({ baseUrl, apiKey }, true)
      setJellyfinBaseUrl('')
      setJellyfinApiKey('')
      const servers = await apiClient.listJellyfinServers()
      setJellyfinServers(servers)
      setJellyfinMessage(null)
    } catch (err) {
      setJellyfinMessage(err instanceof Error ? err.message : 'Failed to add server')
    } finally {
      setJellyfinSaving(false)
    }
  }

  const handleEmbyAdd = async (e: React.FormEvent) => {
    e.preventDefault()
    const raw = embyBaseUrl.trim()
    const baseUrl = raw.startsWith('http://') || raw.startsWith('https://') ? raw : `http://${raw}`
    const apiKey = embyApiKey.trim()
    if (!raw || !apiKey) {
      setEmbyMessage('URL and API Key are required')
      return
    }
    setEmbyMessage(null)
    setEmbySaving(true)
    try {
      await apiClient.putEmbySettings({ baseUrl, apiKey }, true)
      setEmbyBaseUrl('')
      setEmbyApiKey('')
      const servers = await apiClient.listEmbyServers()
      setEmbyServers(servers)
      setEmbyMessage(null)
    } catch (err) {
      setEmbyMessage(err instanceof Error ? err.message : 'Failed to add server')
    } finally {
      setEmbySaving(false)
    }
  }

  const handleRadarrTryDefault = async () => {
    setRadarrBaseUrl(DEFAULT_RADARR_URL)
    setRadarrReachability(null)
    setRadarrTryDefaultLoading(true)
    try {
      const res = await apiClient.checkReachability({ baseUrl: DEFAULT_RADARR_URL })
      setRadarrReachability({ reachable: res.reachable, message: res.message })
    } catch {
      setRadarrReachability({ reachable: false, message: 'Check failed.' })
    } finally {
      setRadarrTryDefaultLoading(false)
    }
  }

  const handleJellyfinTryDefault = async () => {
    setJellyfinBaseUrl(DEFAULT_JELLYFIN_URL)
    setJellyfinReachability(null)
    setJellyfinTryDefaultLoading(true)
    try {
      const res = await apiClient.checkReachability({ baseUrl: DEFAULT_JELLYFIN_URL })
      setJellyfinReachability({ reachable: res.reachable, message: res.message })
    } catch {
      setJellyfinReachability({ reachable: false, message: 'Check failed.' })
    } finally {
      setJellyfinTryDefaultLoading(false)
    }
  }

  const handleEmbyTryDefault = async () => {
    setEmbyBaseUrl(DEFAULT_EMBY_URL)
    setEmbyReachability(null)
    setEmbyTryDefaultLoading(true)
    try {
      const res = await apiClient.checkReachability({ baseUrl: DEFAULT_EMBY_URL })
      setEmbyReachability({ reachable: res.reachable, message: res.message })
    } catch {
      setEmbyReachability({ reachable: false, message: 'Check failed.' })
    } finally {
      setEmbyTryDefaultLoading(false)
    }
  }

  const handleRadarrSave = async (e: React.FormEvent) => {
    e.preventDefault()
    const raw = radarrBaseUrl.trim()
    const baseUrl = raw.startsWith('http://') || raw.startsWith('https://') ? raw : `http://${raw}`
    const apiKey = radarrApiKey.trim()
    if (!raw) {
      setRadarrMessage('Base URL is required')
      return
    }
    setRadarrMessage(null)
    setRadarrSaving(true)
    try {
      await apiClient.putRadarrSettings(RADARR_SERVICE_TYPE, RADARR_SERVER_ID, {
        baseUrl,
        apiKey: apiKey || null,
        qualityProfileId: null,
        rootFolderPath: null,
        tagLabel: null,
        autoAddEnabled: false,
        autoAddIntervalMinutes: null,
      })
      const result = await apiClient.postRadarrTestConnection(RADARR_SERVICE_TYPE, RADARR_SERVER_ID)
      if (result.ok) {
        setRadarrConfigured(true)
        setRadarrShowProfileForm(true)
        setRadarrMessage('Radarr connected. Set tag, profile, and root folder below (optional).')
        setRadarrProfilesLoading(true)
        try {
          const [profiles, folders] = await Promise.all([
            apiClient.getRadarrQualityProfiles(RADARR_SERVICE_TYPE, RADARR_SERVER_ID),
            apiClient.getRadarrRootFolders(RADARR_SERVICE_TYPE, RADARR_SERVER_ID),
          ])
          setRadarrQualityProfiles(profiles)
          setRadarrRootFolders(folders)
          if (profiles.length > 0 && radarrQualityProfileId === 0) setRadarrQualityProfileId(profiles[0].id)
          if (folders.length > 0 && radarrRootFolderId === 0) setRadarrRootFolderId(folders[0].id)
        } catch {
          setRadarrMessage('Radarr connected. Could not load profiles/root folders; set them in Admin later.')
        } finally {
          setRadarrProfilesLoading(false)
        }
      } else {
        setRadarrMessage(result.message ?? 'Connection failed.')
      }
    } catch (err) {
      setRadarrMessage(err instanceof Error ? err.message : 'Failed to save or test Radarr.')
    } finally {
      setRadarrSaving(false)
    }
  }

  const handleRadarrSaveProfile = async (e: React.FormEvent) => {
    e.preventDefault()
    if (radarrQualityProfileId <= 0 || radarrRootFolderId <= 0) {
      setRadarrMessage('Quality profile and root folder are required.')
      return
    }
    const raw = radarrBaseUrl.trim()
    const baseUrl = raw.startsWith('http://') || raw.startsWith('https://') ? raw : `http://${raw}`
    const apiKey = radarrApiKey.trim() || null
    const rootFolderPath = radarrRootFolders.find((f) => f.id === radarrRootFolderId)?.path ?? null
    if (!rootFolderPath) {
      setRadarrMessage('Root folder is required.')
      return
    }
    setRadarrProfileSaving(true)
    setRadarrMessage(null)
    try {
      await apiClient.putRadarrSettings(RADARR_SERVICE_TYPE, RADARR_SERVER_ID, {
        baseUrl,
        apiKey,
        qualityProfileId: radarrQualityProfileId,
        rootFolderPath,
        tagLabel: radarrTagLabel.trim() || null,
        autoAddEnabled: false,
        autoAddIntervalMinutes: null,
      })
      setRadarrMessage('Profile and root folder saved.')
    } catch (err) {
      setRadarrMessage(err instanceof Error ? err.message : 'Failed to save.')
    } finally {
      setRadarrProfileSaving(false)
    }
  }

  const handleSaveAddresses = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!joinAddress) return
    setAddressesError(null)
    setAddressesSaving(true)
    try {
      await apiClient.updateJoinAddressSettings({
        lanHostPort: joinAddress.lanHostPort || null,
        wanHostPort: joinAddress.wanHostPort || null,
        roomLifetimeMinutes: joinAddress.roomLifetimeMinutes ?? undefined,
        guestSessionLifetimeMinutes: joinAddress.guestSessionLifetimeMinutes ?? undefined,
      })
      setStepIndex(5)
    } catch (err) {
      setAddressesError(err instanceof Error ? err.message : 'Failed to save addresses.')
    } finally {
      setAddressesSaving(false)
    }
  }

  const handleComplete = async (e: React.FormEvent) => {
    e.preventDefault()
    setCompleteError(null)
    setCompleteSubmitting(true)
    try {
      await apiClient.setupComplete({
        runLibrarySync: completeRunLibrarySync,
        runTmdbBuild: completeRunTmdbBuild,
        runFetchAllDetails: true,
        runFetchAllImages: false,
      })
      setCompleteCountdown(10)
      setCompleteHeartbeatActive(true)
    } catch (err) {
      setCompleteError(err instanceof Error ? err.message : 'Failed to run setup tasks.')
    } finally {
      setCompleteSubmitting(false)
    }
  }

  useEffect(() => {
    if (!completeHeartbeatActive) return
    setCompleteCountdown(10)
    completeCountdownRef.current = setInterval(() => {
      setCompleteCountdown((prev) => {
        if (prev <= 1) {
          if (completeCountdownRef.current) clearInterval(completeCountdownRef.current)
          completeCountdownRef.current = null
          onFinish()
          return 0
        }
        return prev - 1
      })
    }, 1000)
    return () => {
      if (completeCountdownRef.current) clearInterval(completeCountdownRef.current)
    }
  }, [completeHeartbeatActive, onFinish])

  if (completeHeartbeatActive) {
    return (
      <div className="w-full max-w-lg rounded-2xl bg-white/10 p-8 backdrop-blur-lg">
        <div className="mb-6 flex flex-col items-center justify-center gap-6">
          <img
            src="/tindarr.png"
            alt=""
            className="h-[30rem] w-[30rem] object-contain"
            style={{
              animation: 'heartbeat-pulse 1s ease-in-out infinite',
            }}
          />
          {completeCountdown > 0 && (
            <p className="text-2xl font-semibold text-white tabular-nums">
              {completeCountdown}
            </p>
          )}
        </div>
        <style>{`
          @keyframes heartbeat-pulse {
            0%, 100% { transform: scale(1); }
            14%, 28% { transform: scale(1.1); }
            42%, 56% { transform: scale(1); }
            70%, 84% { transform: scale(1.08); }
          }
        `}</style>
      </div>
    )
  }

  return (
    <div className="w-full max-w-lg rounded-2xl bg-white/10 p-8 backdrop-blur-lg">
      <div className="mb-6 text-center">
        <h1 className="mb-1 bg-gradient-to-r from-pink-400 to-purple-400 bg-clip-text text-2xl font-bold text-transparent">
          Tindarr Setup
        </h1>
        <p className="text-sm text-gray-400">
          Step {stepIndex + 1} of {STEPS.length}: {STEPS[stepIndex].title}
        </p>
        <div className="mt-3 flex justify-center gap-1">
          {STEPS.map((s, i) => (
            <span
              key={s.id}
              className={`h-1.5 w-8 rounded-full ${
                i <= stepIndex ? 'bg-pink-500' : 'bg-white/20'
              }`}
              aria-hidden
            />
          ))}
        </div>
      </div>

      {stepId === 'admin' && (
        <form onSubmit={handleCreateAdmin} className="space-y-4">
          <p className="text-sm text-gray-300">
            Create the initial administrator account. You can add more users later from Admin.
          </p>
          <div>
            <label htmlFor="setup-admin-password" className="mb-1 block text-sm font-medium text-gray-200">
              Admin password
            </label>
            <input
              id="setup-admin-password"
              type="password"
              value={adminPassword}
              onChange={(e) => setAdminPassword(e.target.value)}
              placeholder="Choose a password"
              className="w-full rounded-lg border border-white/20 bg-white/5 px-4 py-3 text-white placeholder-gray-400 focus:border-pink-400 focus:outline-none focus:ring-2 focus:ring-pink-400/50"
              disabled={adminLoading}
              autoComplete="new-password"
            />
          </div>
          <div>
            <label htmlFor="setup-admin-confirm" className="mb-1 block text-sm font-medium text-gray-200">
              Confirm password
            </label>
            <input
              id="setup-admin-confirm"
              type="password"
              value={adminConfirm}
              onChange={(e) => setAdminConfirm(e.target.value)}
              placeholder="Confirm password"
              className="w-full rounded-lg border border-white/20 bg-white/5 px-4 py-3 text-white placeholder-gray-400 focus:border-pink-400 focus:outline-none focus:ring-2 focus:ring-pink-400/50"
              disabled={adminLoading}
              autoComplete="new-password"
            />
          </div>
          {adminError && (
            <div className="rounded-lg border border-red-500/20 bg-red-500/10 p-3 text-sm text-red-400">
              {adminError}
            </div>
          )}
          <button
            type="submit"
            disabled={adminLoading}
            className="w-full rounded-lg bg-gradient-to-r from-pink-500 to-purple-500 px-6 py-3 font-semibold text-white shadow-lg transition-transform hover:scale-[1.02] disabled:opacity-50 disabled:hover:scale-100"
          >
            {adminLoading ? 'Creating…' : 'Create Admin'}
          </button>
          {onGuestJoin && joinRoomId && (
            <div className="mt-4 flex flex-col items-center gap-2">
              <button
                type="button"
                disabled={adminLoading}
                onClick={async () => {
                  try {
                    await onGuestJoin()
                  } catch {
                    // best-effort; error surfaced via auth
                  }
                }}
                className="w-full rounded-lg border border-white/30 bg-white/10 px-6 py-3 font-medium text-white hover:bg-white/20 disabled:opacity-50"
              >
                Continue as guest
              </button>
              <p className="text-xs text-gray-400">Join this room without an account</p>
            </div>
          )}
        </form>
      )}

      {stepId === 'tmdb' && (
        <form onSubmit={handleSaveTmdb} className="space-y-4">
          <p className="text-sm text-gray-300">
            Tindarr uses TMDB for movie metadata and posters. You can add an API key or Read Access Token
            (recommended) below, or skip and configure later in Admin.
          </p>
          <div className="flex flex-wrap gap-2 text-sm">
            <a
              href={TMDB_SETTINGS_URL}
              target="_blank"
              rel="noopener noreferrer"
              className="text-pink-300 underline hover:text-pink-200"
            >
              Already have a TMDB account?
            </a>
            <span className="text-gray-500">|</span>
            <a
              href={TMDB_GUIDE_URL}
              target="_blank"
              rel="noopener noreferrer"
              className="text-pink-300 underline hover:text-pink-200"
            >
              Need an API key?
            </a>
          </div>
          <div>
            <label htmlFor="setup-tmdb-key" className="mb-1 block text-sm font-medium text-gray-200">
              TMDB API Key (optional)
            </label>
            <input
              id="setup-tmdb-key"
              type="password"
              value={tmdbApiKey}
              onChange={(e) => setTmdbApiKey(e.target.value)}
              placeholder="API Key (v3)"
              className="w-full rounded-lg border border-white/20 bg-white/5 px-4 py-3 text-white placeholder-gray-400 focus:border-pink-400 focus:outline-none focus:ring-2 focus:ring-pink-400/50"
              disabled={tmdbSaving}
              autoComplete="off"
            />
          </div>
          <div>
            <label htmlFor="setup-tmdb-token" className="mb-1 block text-sm font-medium text-gray-200">
              TMDB Read Access Token (optional, v4)
            </label>
            <input
              id="setup-tmdb-token"
              type="password"
              value={tmdbReadToken}
              onChange={(e) => setTmdbReadToken(e.target.value)}
              placeholder="Read Access Token"
              className="w-full rounded-lg border border-white/20 bg-white/5 px-4 py-3 text-white placeholder-gray-400 focus:border-pink-400 focus:outline-none focus:ring-2 focus:ring-pink-400/50"
              disabled={tmdbSaving}
              autoComplete="off"
            />
          </div>
          {tmdbError && (
            <div className="rounded-lg border border-red-500/20 bg-red-500/10 p-3 text-sm text-red-400">
              {tmdbError}
            </div>
          )}
          <div className="flex gap-2">
            <button
              type="button"
              onClick={() => setStepIndex(2)}
              className="rounded-lg border border-white/20 bg-white/5 px-4 py-2 text-gray-200 hover:bg-white/10"
            >
              Skip
            </button>
            <button
              type="submit"
              disabled={tmdbSaving}
              className="flex-1 rounded-lg bg-gradient-to-r from-pink-500 to-purple-500 px-6 py-3 font-semibold text-white shadow-lg transition-transform hover:scale-[1.02] disabled:opacity-50 disabled:hover:scale-100"
            >
              {tmdbSaving ? 'Saving…' : 'Save & Continue'}
            </button>
          </div>
        </form>
      )}

      {stepId === 'media' && (
        <div className="space-y-5 max-h-[60vh] overflow-y-auto">
          <p className="text-sm text-gray-300">
            Set up Plex, Jellyfin, and/or Emby. Each is optional; add any you use. You can add more later in Admin.
          </p>

          {/* Plex */}
          <div className="rounded-lg border border-white/10 bg-white/5 p-3">
            <h3 className="text-sm font-semibold text-white mb-2">Plex</h3>
            {plexMediaMessage && (
              <p className="text-sm text-amber-200 mb-2">{plexMediaMessage}</p>
            )}
            {plexPin ? (
              <div className="space-y-2 text-sm text-gray-300">
                <p>Code: <span className="font-mono text-white">{plexPin.code}</span></p>
                <a href={plexPin.authUrl} target="_blank" rel="noopener noreferrer" className="text-pink-300 underline hover:text-pink-200">
                  Open Plex to authorize
                </a>
                <p className="text-xs text-gray-500">Waiting for authorization…</p>
              </div>
            ) : (
              <>
                {plexServers.length > 0 && (
                  <p className="text-xs text-gray-400 mb-2">Added: {plexServers.map(s => s.name).join(', ')}</p>
                )}
                <button
                  type="button"
                  onClick={handlePlexCreatePin}
                  disabled={plexMediaLoading}
                  className="rounded bg-amber-600/80 px-3 py-1.5 text-sm font-medium text-white hover:bg-amber-600 disabled:opacity-50"
                >
                  {plexMediaLoading ? 'Loading…' : 'Link Plex account'}
                </button>
              </>
            )}
          </div>

          {/* Jellyfin */}
          <div className="rounded-lg border border-white/10 bg-white/5 p-3">
            <h3 className="text-sm font-semibold text-white mb-2">Jellyfin</h3>
            {jellyfinServers.length > 0 && (
              <p className="text-xs text-gray-400 mb-2">Added: {jellyfinServers.map(s => s.name || s.serverId).join(', ')}</p>
            )}
            <div className="flex flex-wrap items-center gap-2 mb-2">
              <button
                type="button"
                onClick={handleJellyfinTryDefault}
                disabled={jellyfinTryDefaultLoading}
                className="rounded border border-white/30 bg-white/10 px-2 py-1 text-xs font-medium text-gray-200 hover:bg-white/20 disabled:opacity-50"
              >
                {jellyfinTryDefaultLoading ? 'Checking…' : 'Try default'}
              </button>
              {jellyfinReachability !== null && (
                <>
                  {jellyfinReachability.reachable ? (
                    <span className="text-xs text-green-400">
                      Server reachable. Get API key: <a href={JELLYFIN_API_KEY_URL} target="_blank" rel="noopener noreferrer" className="text-pink-300 underline hover:text-pink-200">Jellyfin API docs</a> (Dashboard → API Keys).
                    </span>
                  ) : (
                    <span className="text-xs text-amber-400">{jellyfinReachability.message ?? 'Not reachable'}</span>
                  )}
                </>
              )}
            </div>
            <form onSubmit={handleJellyfinAdd} className="flex flex-wrap items-end gap-2">
              <input
                type="url"
                value={jellyfinBaseUrl}
                onChange={(e) => { setJellyfinBaseUrl(e.target.value); setJellyfinReachability(null) }}
                placeholder="http:// or https:// required (e.g. http://localhost:8096)"
                className="rounded border border-white/20 bg-white/5 px-2 py-1.5 text-sm text-white placeholder-gray-500 w-40"
                disabled={jellyfinSaving}
              />
              <input
                type="password"
                value={jellyfinApiKey}
                onChange={(e) => setJellyfinApiKey(e.target.value)}
                placeholder="API Key"
                className="rounded border border-white/20 bg-white/5 px-2 py-1.5 text-sm text-white placeholder-gray-500 w-36"
                disabled={jellyfinSaving}
              />
              <button type="submit" disabled={jellyfinSaving} className="rounded bg-purple-600/80 px-3 py-1.5 text-sm font-medium text-white hover:bg-purple-600 disabled:opacity-50">
                {jellyfinSaving ? 'Adding…' : 'Add server'}
              </button>
            </form>
            {jellyfinMessage && <p className="text-xs text-red-400 mt-1">{jellyfinMessage}</p>}
          </div>

          {/* Emby */}
          <div className="rounded-lg border border-white/10 bg-white/5 p-3">
            <h3 className="text-sm font-semibold text-white mb-2">Emby</h3>
            {embyServers.length > 0 && (
              <p className="text-xs text-gray-400 mb-2">Added: {embyServers.map(s => s.name || s.serverId).join(', ')}</p>
            )}
            <div className="flex flex-wrap items-center gap-2 mb-2">
              <button
                type="button"
                onClick={handleEmbyTryDefault}
                disabled={embyTryDefaultLoading}
                className="rounded border border-white/30 bg-white/10 px-2 py-1 text-xs font-medium text-gray-200 hover:bg-white/20 disabled:opacity-50"
              >
                {embyTryDefaultLoading ? 'Checking…' : 'Try default'}
              </button>
              {embyReachability !== null && (
                <>
                  {embyReachability.reachable ? (
                    <span className="text-xs text-green-400">
                      Server reachable. Get API key: <a href={embyApiKeysUrl(embyBaseUrl || DEFAULT_EMBY_URL)} target="_blank" rel="noopener noreferrer" className="text-pink-300 underline hover:text-pink-200">Emby API Keys</a> (opens your server).
                    </span>
                  ) : (
                    <span className="text-xs text-amber-400">{embyReachability.message ?? 'Not reachable'}</span>
                  )}
                </>
              )}
            </div>
            <form onSubmit={handleEmbyAdd} className="flex flex-wrap items-end gap-2">
              <input
                type="url"
                value={embyBaseUrl}
                onChange={(e) => { setEmbyBaseUrl(e.target.value); setEmbyReachability(null) }}
                placeholder="http:// or https:// required (e.g. http://localhost:8096)"
                className="rounded border border-white/20 bg-white/5 px-2 py-1.5 text-sm text-white placeholder-gray-500 w-40"
                disabled={embySaving}
              />
              <input
                type="password"
                value={embyApiKey}
                onChange={(e) => setEmbyApiKey(e.target.value)}
                placeholder="API Key"
                className="rounded border border-white/20 bg-white/5 px-2 py-1.5 text-sm text-white placeholder-gray-500 w-36"
                disabled={embySaving}
              />
              <button type="submit" disabled={embySaving} className="rounded bg-teal-600/80 px-3 py-1.5 text-sm font-medium text-white hover:bg-teal-600 disabled:opacity-50">
                {embySaving ? 'Adding…' : 'Add server'}
              </button>
            </form>
            {embyMessage && <p className="text-xs text-red-400 mt-1">{embyMessage}</p>}
          </div>

          <div className="flex gap-2 pt-2">
            <button
              type="button"
              onClick={() => setStepIndex(3)}
              className="flex-1 rounded-lg bg-gradient-to-r from-pink-500 to-purple-500 px-6 py-3 font-semibold text-white shadow-lg transition-transform hover:scale-[1.02]"
            >
              Continue
            </button>
          </div>
        </div>
      )}

      {stepId === 'radarr' && (
        <div className="space-y-5">
          <p className="text-sm text-gray-300">
            Optionally connect Radarr to auto-add accepted movies. You can set quality profile and root folder later in Admin.
          </p>
          <div className="rounded-lg border border-white/10 bg-white/5 p-4">
            <h3 className="text-sm font-semibold text-white mb-3">Radarr</h3>
            {radarrConfigured && (
              <p className="text-xs text-green-400/90 mb-2">Radarr is configured. Change URL/API key below to update.</p>
            )}
            <div className="flex flex-wrap items-center gap-2 mb-3">
              <button
                type="button"
                onClick={handleRadarrTryDefault}
                disabled={radarrTryDefaultLoading}
                className="rounded border border-white/30 bg-white/10 px-2 py-1 text-xs font-medium text-gray-200 hover:bg-white/20 disabled:opacity-50"
              >
                {radarrTryDefaultLoading ? 'Checking…' : 'Try default'}
              </button>
              {radarrReachability !== null && (
                <>
                  {radarrReachability.reachable ? (
                    <span className="text-xs text-green-400">
                      Server reachable. Get API key: <a href={radarrSettingsGeneralUrl(radarrBaseUrl || DEFAULT_RADARR_URL)} target="_blank" rel="noopener noreferrer" className="text-pink-300 underline hover:text-pink-200">Radarr Settings (General)</a> (API key under Security).
                    </span>
                  ) : (
                    <span className="text-xs text-amber-400">{radarrReachability.message ?? 'Not reachable'}</span>
                  )}
                </>
              )}
            </div>
            <form onSubmit={handleRadarrSave} className="space-y-3">
              <div>
                <label htmlFor="setup-radarr-url" className="mb-1 block text-xs font-medium text-gray-300">
                  Base URL
                </label>
                <input
                  id="setup-radarr-url"
                  type="url"
                  value={radarrBaseUrl}
                  onChange={(e) => { setRadarrBaseUrl(e.target.value); setRadarrReachability(null) }}
                  placeholder="http:// or https:// required (e.g. http://localhost:7878)"
                  className="w-full rounded-lg border border-white/20 bg-white/5 px-3 py-2 text-sm text-white placeholder-gray-500 focus:border-pink-400 focus:outline-none focus:ring-2 focus:ring-pink-400/50"
                  disabled={radarrSaving}
                />
              </div>
              <div>
                <label htmlFor="setup-radarr-apikey" className="mb-1 block text-xs font-medium text-gray-300">
                  API Key
                </label>
                <input
                  id="setup-radarr-apikey"
                  type="password"
                  value={radarrApiKey}
                  onChange={(e) => setRadarrApiKey(e.target.value)}
                  placeholder="Your Radarr API Key"
                  className="w-full rounded-lg border border-white/20 bg-white/5 px-3 py-2 text-sm text-white placeholder-gray-500 focus:border-pink-400 focus:outline-none focus:ring-2 focus:ring-pink-400/50"
                  disabled={radarrSaving}
                  autoComplete="off"
                />
              </div>
              {radarrMessage && (
                <p className={`text-sm ${radarrMessage.startsWith('Radarr') || radarrMessage.includes('connected') ? 'text-green-400' : 'text-red-400'}`}>
                  {radarrMessage}
                </p>
              )}
              <div className="flex gap-2">
                <button
                  type="submit"
                  disabled={radarrSaving}
                  className="rounded-lg bg-pink-600/90 px-4 py-2 text-sm font-semibold text-white hover:bg-pink-600 disabled:opacity-50"
                >
                  {radarrSaving ? 'Saving…' : 'Save & Test connection'}
                </button>
                <button
                  type="button"
                  onClick={() => setStepIndex(4)}
                  className="rounded-lg border border-white/20 bg-white/5 px-4 py-2 text-sm text-gray-200 hover:bg-white/10"
                >
                  Skip
                </button>
              </div>
            </form>

            {radarrShowProfileForm && (
              <form onSubmit={handleRadarrSaveProfile} className="mt-4 space-y-3 border-t border-white/10 pt-4">
                <p className="text-xs text-gray-400">Choose quality profile and root folder (required). Tag is optional.</p>
                <div>
                  <label htmlFor="setup-radarr-tag" className="mb-1 block text-xs font-medium text-gray-300">
                    Tag (optional)
                  </label>
                  <input
                    id="setup-radarr-tag"
                    type="text"
                    value={radarrTagLabel}
                    onChange={(e) => setRadarrTagLabel(e.target.value)}
                    placeholder="e.g. tindarr"
                    className="w-full rounded-lg border border-white/20 bg-white/5 px-3 py-2 text-sm text-white placeholder-gray-500 focus:border-pink-400 focus:outline-none focus:ring-2 focus:ring-pink-400/50"
                    disabled={radarrProfileSaving}
                  />
                </div>
                <div>
                  <label htmlFor="setup-radarr-profile" className="mb-1 block text-xs font-medium text-gray-300">
                    Quality profile <span className="text-pink-400">*</span>
                  </label>
                  <div className="relative">
                    <select
                      id="setup-radarr-profile"
                      value={radarrQualityProfileId}
                      onChange={(e) => setRadarrQualityProfileId(Number(e.target.value))}
                      className="w-full cursor-pointer appearance-none rounded-lg border border-white/20 bg-white/10 py-2.5 pl-3 pr-10 text-sm text-white shadow-inner transition-colors hover:border-pink-400/40 focus:border-pink-400 focus:outline-none focus:ring-2 focus:ring-pink-400/50 disabled:cursor-not-allowed disabled:opacity-50 [&>option]:bg-slate-800 [&>option]:text-white"
                      disabled={radarrProfileSaving || radarrProfilesLoading}
                      required
                    >
                    {radarrProfilesLoading ? (
                      <option value={0}>Loading…</option>
                    ) : radarrQualityProfiles.length === 0 ? (
                      <option value={0}>No profiles found</option>
                    ) : (
                      radarrQualityProfiles.map((p) => (
                        <option key={p.id} value={p.id}>
                          {p.name}
                        </option>
                      ))
                    )}
                  </select>
                    <span className="pointer-events-none absolute right-3 top-1/2 -translate-y-1/2 text-gray-400" aria-hidden>▼</span>
                  </div>
                </div>
                <div>
                  <label htmlFor="setup-radarr-root" className="mb-1 block text-xs font-medium text-gray-300">
                    Root folder <span className="text-pink-400">*</span>
                  </label>
                  <div className="relative">
                    <select
                      id="setup-radarr-root"
                      value={radarrRootFolderId}
                      onChange={(e) => setRadarrRootFolderId(Number(e.target.value))}
                      className="w-full cursor-pointer appearance-none rounded-lg border border-white/20 bg-white/10 py-2.5 pl-3 pr-10 text-sm text-white shadow-inner transition-colors hover:border-pink-400/40 focus:border-pink-400 focus:outline-none focus:ring-2 focus:ring-pink-400/50 disabled:cursor-not-allowed disabled:opacity-50 [&>option]:bg-slate-800 [&>option]:text-white"
                      disabled={radarrProfileSaving || radarrProfilesLoading}
                      required
                    >
                    {radarrProfilesLoading ? (
                      <option value={0}>Loading…</option>
                    ) : radarrRootFolders.length === 0 ? (
                      <option value={0}>No root folders found</option>
                    ) : (
                      radarrRootFolders.map((f) => (
                        <option key={f.id} value={f.id}>
                          {f.path}
                        </option>
                      ))
                    )}
                  </select>
                    <span className="pointer-events-none absolute right-3 top-1/2 -translate-y-1/2 text-gray-400" aria-hidden>▼</span>
                  </div>
                </div>
                <button
                  type="submit"
                  disabled={radarrProfileSaving || radarrProfilesLoading || radarrQualityProfileId <= 0 || radarrRootFolderId <= 0}
                  className="rounded-lg bg-pink-600/90 px-4 py-2 text-sm font-semibold text-white hover:bg-pink-600 disabled:opacity-50"
                >
                  {radarrProfileSaving ? 'Saving…' : 'Save profile & root folder'}
                </button>
              </form>
            )}
          </div>
          <div className="flex gap-2">
            <button
              type="button"
              onClick={() => setStepIndex(4)}
              className="flex-1 rounded-lg bg-gradient-to-r from-pink-500 to-purple-500 px-6 py-3 font-semibold text-white shadow-lg transition-transform hover:scale-[1.02]"
            >
              Continue
            </button>
          </div>
        </div>
      )}

      {stepId === 'addresses' && (
        <form onSubmit={handleSaveAddresses} className="space-y-4">
          <p className="text-sm text-gray-300">
            LAN and WAN addresses are used for room join links and casting. Values are auto-detected from this
            machine (NIC / ipconfig for LAN, ipify for WAN). Edit if needed.
          </p>
          {addressesLoading ? (
            <p className="text-gray-400">Detecting addresses…</p>
          ) : (
            <>
              <div>
                <label htmlFor="setup-lan" className="mb-1 block text-sm font-medium text-gray-200">
                  LAN (host:port)
                </label>
                <input
                  id="setup-lan"
                  type="text"
                  value={joinAddress?.lanHostPort ?? ''}
                  onChange={(e) =>
                    setJoinAddress((j) => (j ? { ...j, lanHostPort: e.target.value || null } : null))
                  }
                  placeholder="e.g. 192.168.1.10:5000"
                  className="w-full rounded-lg border border-white/20 bg-white/5 px-4 py-3 text-white placeholder-gray-400 focus:border-pink-400 focus:outline-none focus:ring-2 focus:ring-pink-400/50"
                  disabled={addressesSaving}
                />
              </div>
              <div>
                <label htmlFor="setup-wan" className="mb-1 block text-sm font-medium text-gray-200">
                  WAN (host:port or leave blank)
                </label>
                <input
                  id="setup-wan"
                  type="text"
                  value={joinAddress?.wanHostPort ?? ''}
                  onChange={(e) =>
                    setJoinAddress((j) => (j ? { ...j, wanHostPort: e.target.value || null } : null))
                  }
                  placeholder="Public IP or hostname"
                  className="w-full rounded-lg border border-white/20 bg-white/5 px-4 py-3 text-white placeholder-gray-400 focus:border-pink-400 focus:outline-none focus:ring-2 focus:ring-pink-400/50"
                  disabled={addressesSaving}
                />
              </div>
            </>
          )}
          {addressesError && (
            <div className="rounded-lg border border-red-500/20 bg-red-500/10 p-3 text-sm text-red-400">
              {addressesError}
            </div>
          )}
          <div className="flex gap-2">
            <button
              type="button"
              onClick={() => setStepIndex(3)}
              className="rounded-lg border border-white/20 bg-white/5 px-4 py-2 text-gray-200 hover:bg-white/10"
            >
              Back
            </button>
            <button
              type="submit"
              disabled={addressesLoading || addressesSaving || !joinAddress}
              className="flex-1 rounded-lg bg-gradient-to-r from-pink-500 to-purple-500 px-6 py-3 font-semibold text-white shadow-lg transition-transform hover:scale-[1.02] disabled:opacity-50 disabled:hover:scale-100"
            >
              {addressesSaving ? 'Saving…' : 'Save & Continue'}
            </button>
          </div>
        </form>
      )}

      {stepId === 'complete' && (
        <form onSubmit={handleComplete} className="space-y-4">
          <p className="text-sm text-gray-300">
            Optionally run initial library sync and TMDB metadata build now. You can also run these from Admin
            later.
          </p>
          <label className="flex cursor-pointer items-center gap-2 text-gray-200">
            <input
              type="checkbox"
              checked={completeRunLibrarySync}
              onChange={(e) => setCompleteRunLibrarySync(e.target.checked)}
              className="rounded border-white/20 bg-white/5 text-pink-500 focus:ring-pink-400"
            />
            Run library sync for configured media servers
          </label>
          <label className="flex cursor-pointer items-center gap-2 text-gray-200">
            <input
              type="checkbox"
              checked={completeRunTmdbBuild}
              onChange={(e) => setCompleteRunTmdbBuild(e.target.checked)}
              className="rounded border-white/20 bg-white/5 text-pink-500 focus:ring-pink-400"
            />
            Run TMDB metadata build
          </label>
          {completeError && (
            <div className="rounded-lg border border-red-500/20 bg-red-500/10 p-3 text-sm text-red-400">
              {completeError}
            </div>
          )}
          <button
            type="submit"
            disabled={completeSubmitting}
            className="w-full rounded-lg bg-gradient-to-r from-pink-500 to-purple-500 px-6 py-3 font-semibold text-white shadow-lg transition-transform hover:scale-[1.02] disabled:opacity-50 disabled:hover:scale-100"
          >
            {completeSubmitting ? 'Starting…' : 'Finish Setup'}
          </button>
        </form>
      )}

    </div>
  )
}
