import { useEffect, useRef, useState } from 'react'
import { useAuth } from '../contexts/AuthContext'
import { useNavigate } from 'react-router-dom'
import TmdbAttribution from '../components/TmdbAttribution'
import {
  apiClient,
  type AdminUserDto,
  type AdminUpdateCheckResponse,
  type JoinAddressSettingsDto,
  type CastingSettingsDto,
  type AdvancedSettingsDto,
  type AdminDbMovieListResponse,
  type PopulateStatusDto,
  type CastingDiagnosticsDto,
  type RadarrRootFolderDto,
  type ServiceScopeOptionDto,
  type TmdbCacheSettingsDto,
  type TmdbBuildStatusDto,
  type TmdbRestoreResultDto,
  type PlexAuthStatusResponse,
  type PlexServerDto,
  type PlexPinCreateResponse,
  type PlexLibrarySyncStatusDto,
  type JellyfinServerDto,
  type UpdateJellyfinSettingsRequest,
  type EmbyServerDto,
  type UpdateEmbySettingsRequest,
  type RegistrationSettingsDto,
} from '../lib/api'

// TMDB languages (ISO 639-1): top 10 pinned, then rest
const TMDB_LANGUAGES_TOP = [
  { code: 'en', name: 'English' },
  { code: 'es', name: 'Spanish' },
  { code: 'fr', name: 'French' },
  { code: 'de', name: 'German' },
  { code: 'it', name: 'Italian' },
  { code: 'pt', name: 'Portuguese' },
  { code: 'zh', name: 'Chinese' },
  { code: 'ja', name: 'Japanese' },
  { code: 'ko', name: 'Korean' },
  { code: 'hi', name: 'Hindi' },
] as const
const TMDB_LANGUAGES_OTHER = [
  'ru', 'ar', 'pl', 'nl', 'tr', 'vi', 'th', 'sv', 'id', 'el', 'he', 'no', 'da', 'fi', 'cs', 'ro', 'hu', 'uk',
].map((code) => ({ code, name: code }))

// TMDB regions (ISO 3166-1): top 10 pinned, then rest
const TMDB_REGIONS_TOP = [
  { code: 'US', name: 'United States' },
  { code: 'GB', name: 'United Kingdom' },
  { code: 'CA', name: 'Canada' },
  { code: 'AU', name: 'Australia' },
  { code: 'DE', name: 'Germany' },
  { code: 'FR', name: 'France' },
  { code: 'IN', name: 'India' },
  { code: 'JP', name: 'Japan' },
  { code: 'BR', name: 'Brazil' },
  { code: 'MX', name: 'Mexico' },
] as const
const TMDB_REGIONS_OTHER = [
  'ES', 'IT', 'NL', 'KR', 'RU', 'PL', 'SE', 'NO', 'DK', 'FI', 'CN', 'TW', 'HK', 'SG', 'TH', 'ID', 'PH', 'AR', 'CL', 'CO',
].map((code) => ({ code, name: code }))

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
  rootFolderPath: string
  tagLabel: string
  autoAddMovies: boolean
  enabled: boolean
  autoAddIntervalSeconds?: number
}

interface QualityProfile {
  id: number
  name: string
}

export default function AdminConsole() {
  const { user } = useAuth()
  const navigate = useNavigate()
  const [selectedService, setSelectedService] = useState<string | null>('tindarr')
  const [tindarrTab, setTindarrTab] = useState<'users' | 'rooms' | 'db' | 'casting' | 'advanced' | 'console'>('users')

  const servicePills = [
    { id: 'tindarr', label: 'Tindarr' },
    { id: 'radarr', label: 'Radarr' },
    { id: 'tmdb', label: 'TMDB' },
    { id: 'plex', label: 'Plex' },
    { id: 'jellyfin', label: 'JellyFin' },
    { id: 'emby', label: 'Emby' },
    { id: 'backup', label: 'Backup & Restore' },
  ]
  const [selectedMovie, setSelectedMovie] = useState<MovieDetails | null>(null)

  // Radarr: editable server id (scope for this instance)
  const [radarrServerId, setRadarrServerId] = useState('default')
  const [radarrSettings, setRadarrSettings] = useState<RadarrSettings>({
    apiUrl: '',
    apiKey: '',
    defaultQualityProfileId: 0,
    defaultRootFolderId: 0,
    rootFolderPath: '',
    tagLabel: '',
    autoAddMovies: false,
    enabled: false,
    autoAddIntervalSeconds: 300
  })
  const [qualityProfiles, setQualityProfiles] = useState<QualityProfile[]>([])
  const [rootFolders, setRootFolders] = useState<RadarrRootFolderDto[]>([])
  const [savingSettings, setSavingSettings] = useState(false)
  const [testingConnection, setTestingConnection] = useState(false)
  const [syncLibraryLoading, setSyncLibraryLoading] = useState(false)
  const [settingsMessage, setSettingsMessage] = useState<{ type: 'success' | 'error'; text: string } | null>(null)
  const [addingMovieId, setAddingMovieId] = useState<string | null>(null)
  const [hasValidatedConnection, setHasValidatedConnection] = useState(false)
  // Matching settings (per Radarr scope)
  const [matchSettings, setMatchSettings] = useState<{ minUsers: number | null; minUserPercent: number | null }>({
    minUsers: null,
    minUserPercent: null
  })
  const [matchSaving, setMatchSaving] = useState(false)

  // TMDB state
  const [tmdbCacheSettings, setTmdbCacheSettings] = useState<TmdbCacheSettingsDto | null>(null)
  const [tmdbCacheLoading, setTmdbCacheLoading] = useState(false)
  const [tmdbCacheSaving, setTmdbCacheSaving] = useState(false)
  const [tmdbBuildStatus, setTmdbBuildStatus] = useState<TmdbBuildStatusDto | null>(null)
  const [tmdbCredentialApiKey, setTmdbCredentialApiKey] = useState('')
  const [tmdbCredentialReadToken, setTmdbCredentialReadToken] = useState('')
  const [tmdbCredentialSaving, setTmdbCredentialSaving] = useState(false)
  const [tmdbMessage, setTmdbMessage] = useState<{ type: 'success' | 'error'; text: string } | null>(null)
  const [tmdbBuildDiscoverLimit, setTmdbBuildDiscoverLimit] = useState(50)
  const [tmdbBuildBypassLimit, setTmdbBuildBypassLimit] = useState(false)
  const [tmdbImportLoading, setTmdbImportLoading] = useState(false)
  const [tmdbImportSelectedFile, setTmdbImportSelectedFile] = useState<File | null>(null)
  const [tmdbRestoreResult, setTmdbRestoreResult] = useState<TmdbRestoreResultDto | null>(null)
  const tmdbBuildPollRef = useRef<ReturnType<typeof setInterval> | null>(null)
  const tmdbImportFileInputRef = useRef<HTMLInputElement | null>(null)

  // Backup & Restore tab
  const [backupLoading, setBackupLoading] = useState<string | null>(null)
  const [backupMessage, setBackupMessage] = useState<{ type: 'success' | 'error'; text: string } | null>(null)
  const [plexRestoreFile, setPlexRestoreFile] = useState<File | null>(null)
  const [jellyfinRestoreFile, setJellyfinRestoreFile] = useState<File | null>(null)
  const [embyRestoreFile, setEmbyRestoreFile] = useState<File | null>(null)
  const plexRestoreInputRef = useRef<HTMLInputElement | null>(null)
  const jellyfinRestoreInputRef = useRef<HTMLInputElement | null>(null)
  const embyRestoreInputRef = useRef<HTMLInputElement | null>(null)

  // Console tab (mirror stdout/stderr)
  const [consoleLines, setConsoleLines] = useState<string[]>([])
  const [consoleMaxLines, setConsoleMaxLines] = useState(500)
  const [consoleRefreshPaused, setConsoleRefreshPaused] = useState(false)
  const [consoleHighlightText, setConsoleHighlightText] = useState('')
  const consolePreRef = useRef<HTMLPreElement | null>(null)

  // Tindarr tabs state
  const [adminUsers, setAdminUsers] = useState<AdminUserDto[]>([])
  const [adminUsersLoading, setAdminUsersLoading] = useState(false)
  const [joinAddress, setJoinAddress] = useState<JoinAddressSettingsDto | null>(null)
  const [joinAddressLoading, setJoinAddressLoading] = useState(false)
  const [castingSettings, setCastingSettings] = useState<CastingSettingsDto | null>(null)
  const [castingLoading, setCastingLoading] = useState(false)
  const [advancedSettings, setAdvancedSettings] = useState<AdvancedSettingsDto | null>(null)
  const [advancedLoading, setAdvancedLoading] = useState(false)
  const [dbScopes, setDbScopes] = useState<ServiceScopeOptionDto[]>([])
  const [dbScope, setDbScope] = useState<{ serviceType: string; serverId: string } | null>(null)
  const [dbViewMode, setDbViewMode] = useState<'table' | 'gallery'>('table')
  const [dbMovies, setDbMovies] = useState<AdminDbMovieListResponse | null>(null)
  const [dbMoviesLoading, setDbMoviesLoading] = useState(false)
  const [dbPopulateLoading, setDbPopulateLoading] = useState(false)
  const [dbPopulateMessage, setDbPopulateMessage] = useState<string | null>(null)
  const [dbPopulateStatus, setDbPopulateStatus] = useState<PopulateStatusDto | null>(null)
  const [castingDiagnostics, setCastingDiagnostics] = useState<CastingDiagnosticsDto | null>(null)
  const [castingDiagLoading, setCastingDiagLoading] = useState(false)
  const [tindarrMessage, setTindarrMessage] = useState<{ type: 'success' | 'error'; text: string } | null>(null)
  const [registrationSettings, setRegistrationSettings] = useState<RegistrationSettingsDto | null>(null)
  const [updateCheck, setUpdateCheck] = useState<AdminUpdateCheckResponse | null>(null)
  const [updateCheckLoading, setUpdateCheckLoading] = useState(false)
  const [editingUserId, setEditingUserId] = useState<string | null>(null)
  const [editingDisplayName, setEditingDisplayName] = useState('')
  const ROLES_CYCLE = ['Contributor', 'Curator', 'Admin'] as const
  type RoleLevel = (typeof ROLES_CYCLE)[number]
  const nextRole = (r: string): RoleLevel => ROLES_CYCLE[(ROLES_CYCLE.indexOf(r as RoleLevel) + 1) % ROLES_CYCLE.length]
  const rolePillClass = (role: string) => (role === 'Admin' ? 'bg-red-500/90 text-white' : role === 'Curator' ? 'bg-amber-500/90 text-white' : 'bg-emerald-500/90 text-white')
  const [newUserForm, setNewUserForm] = useState<{ userId: string; displayName: string; password: string; role: RoleLevel }>({ userId: '', displayName: '', password: '', role: 'Contributor' })

  // Media Servers: Plex
  const [plexAuthStatus, setPlexAuthStatus] = useState<PlexAuthStatusResponse | null>(null)
  const [plexServers, setPlexServers] = useState<PlexServerDto[]>([])
  const [plexServersLoading, setPlexServersLoading] = useState(false)
  const [plexPin, setPlexPin] = useState<PlexPinCreateResponse | null>(null)
  const plexPinPollRef = useRef<ReturnType<typeof setInterval> | null>(null)
  const [plexMessage, setPlexMessage] = useState<{ type: 'success' | 'error'; text: string } | null>(null)
  const [plexSyncServerId, setPlexSyncServerId] = useState<string | null>(null)
  const [plexDeleteServerId, setPlexDeleteServerId] = useState<string | null>(null)
  const [plexTestServerId, setPlexTestServerId] = useState<string | null>(null)
  const [plexSyncStatus, setPlexSyncStatus] = useState<PlexLibrarySyncStatusDto | null>(null)
  const plexSyncUnsubscribeRef = useRef<(() => void) | null>(null)

  // Media Servers: Jellyfin
  const [jellyfinServers, setJellyfinServers] = useState<JellyfinServerDto[]>([])
  const [jellyfinServersLoading, setJellyfinServersLoading] = useState(false)
  const [jellyfinMessage, setJellyfinMessage] = useState<{ type: 'success' | 'error'; text: string } | null>(null)
  const [jellyfinAddForm, setJellyfinAddForm] = useState<UpdateJellyfinSettingsRequest>({ baseUrl: '', apiKey: '' })
  const [jellyfinAddSaving, setJellyfinAddSaving] = useState(false)
  const [jellyfinSyncServerId, setJellyfinSyncServerId] = useState<string | null>(null)
  const [jellyfinDeleteServerId, setJellyfinDeleteServerId] = useState<string | null>(null)
  const [jellyfinTestServerId, setJellyfinTestServerId] = useState<string | null>(null)

  // Media Servers: Emby
  const [embyServers, setEmbyServers] = useState<EmbyServerDto[]>([])
  const [embyServersLoading, setEmbyServersLoading] = useState(false)
  const [embyMessage, setEmbyMessage] = useState<{ type: 'success' | 'error'; text: string } | null>(null)
  const [embyAddForm, setEmbyAddForm] = useState<UpdateEmbySettingsRequest>({ baseUrl: '', apiKey: '' })
  const [embyAddSaving, setEmbyAddSaving] = useState(false)
  const [embySyncServerId, setEmbySyncServerId] = useState<string | null>(null)
  const [embyDeleteServerId, setEmbyDeleteServerId] = useState<string | null>(null)
  const [embyTestServerId, setEmbyTestServerId] = useState<string | null>(null)

  // Check if user is admin and ensure Tindarr is selected when entering admin console
  useEffect(() => {
    if (!user || !user.isAdmin) {
      navigate('/')
      return
    }
    setSelectedService('tindarr')
  }, [user, navigate])

  // Load Tindarr tab data when tab or service changes
  useEffect(() => {
    if (selectedService !== 'tindarr' || !user?.isAdmin) return
    if (tindarrTab === 'users') {
      setAdminUsersLoading(true)
      Promise.all([apiClient.listAdminUsers(), apiClient.getRegistrationSettings()])
        .then(([list, reg]) => { setAdminUsers(list); setRegistrationSettings(reg); setAdminUsersLoading(false) })
        .catch(() => setAdminUsersLoading(false))
    }
    if (tindarrTab === 'rooms') {
      setJoinAddressLoading(true)
      apiClient.getJoinAddressSettings().then((s) => { setJoinAddress(s); setJoinAddressLoading(false) }).catch(() => setJoinAddressLoading(false))
    }
    if (tindarrTab === 'casting') {
      setCastingLoading(true)
      setCastingDiagLoading(true)
      Promise.all([apiClient.getCastingSettings(), apiClient.getCastingDiagnostics()])
        .then(([settings, diag]) => { setCastingSettings(settings); setCastingDiagnostics(diag); setCastingLoading(false); setCastingDiagLoading(false) })
        .catch(() => { setCastingLoading(false); setCastingDiagLoading(false) })
    }
    if (tindarrTab === 'advanced') {
      setAdvancedLoading(true)
      Promise.all([apiClient.getAdvancedSettings(), apiClient.getRegistrationSettings()])
        .then(([s, reg]) => { setAdvancedSettings(s); setRegistrationSettings(reg); setAdvancedLoading(false) })
        .catch(() => setAdvancedLoading(false))
    }
    if (tindarrTab === 'db') {
      apiClient.getScopes().then(setDbScopes)
      if (dbScope) {
        setDbMoviesLoading(true)
        apiClient.getAdminDbMovies(dbScope.serviceType, dbScope.serverId, 0, 50).then((r) => { setDbMovies(r); setDbMoviesLoading(false) }).catch(() => setDbMoviesLoading(false))
      } else setDbMovies(null)
    }
  }, [selectedService, tindarrTab, user?.isAdmin, dbScope?.serviceType, dbScope?.serverId])

  useEffect(() => {
    if (tindarrTab === 'db' && dbScopes.length > 0 && !dbScope) setDbScope({ serviceType: dbScopes[0].serviceType, serverId: dbScopes[0].serverId })
  }, [tindarrTab, dbScopes, dbScope])

  // Fetch Radarr settings and match settings when Radarr pill is selected or serverId changes
  useEffect(() => {
    if (selectedService !== 'radarr') return
    fetchRadarrSettings()
    apiClient
      .getMatchSettings('radarr', radarrServerId)
      .then((d) => setMatchSettings({ minUsers: d.minUsers ?? null, minUserPercent: d.minUserPercent ?? null }))
      .catch(() => setMatchSettings({ minUsers: null, minUserPercent: null }))
  }, [selectedService, radarrServerId])

  // Fetch TMDB cache settings and advanced (for credentials status) when TMDB pill is selected
  useEffect(() => {
    if (selectedService !== 'tmdb') return
    setTmdbCacheLoading(true)
    Promise.all([apiClient.getTmdbCacheSettings(), apiClient.getAdvancedSettings()])
      .then(([cache, advanced]) => {
        setTmdbCacheSettings(cache)
        setAdvancedSettings(advanced)
      })
      .catch(() => setTmdbCacheSettings(null))
      .finally(() => setTmdbCacheLoading(false))
    apiClient.getTmdbBuildStatus().then(setTmdbBuildStatus).catch(() => setTmdbBuildStatus(null))
  }, [selectedService])

  // Poll TMDB build status when build is running
  useEffect(() => {
    if (!tmdbBuildStatus || tmdbBuildStatus.state !== 'running') return
    const poll = () => {
      apiClient.getTmdbBuildStatus().then(setTmdbBuildStatus).catch(() => {})
    }
    const id = setInterval(poll, 2000)
    tmdbBuildPollRef.current = id
    return () => {
      if (tmdbBuildPollRef.current) clearInterval(tmdbBuildPollRef.current)
      tmdbBuildPollRef.current = null
    }
  }, [tmdbBuildStatus?.state])

  // Fetch populate status when DB tab is selected
  useEffect(() => {
    if (selectedService !== 'tindarr' || tindarrTab !== 'db') return
    apiClient.getPopulateStatus().then(setDbPopulateStatus).catch(() => setDbPopulateStatus(null))
  }, [selectedService, tindarrTab])

  // Poll populate status when populate is running
  useEffect(() => {
    if (!dbPopulateStatus || dbPopulateStatus.state !== 'running') return
    const poll = () => {
      apiClient.getPopulateStatus().then(setDbPopulateStatus).catch(() => {})
    }
    const id = setInterval(poll, 2000)
    return () => clearInterval(id)
  }, [dbPopulateStatus?.state])

  // Fetch and poll console output when Console tab is selected (pausable)
  useEffect(() => {
    if (selectedService !== 'tindarr' || tindarrTab !== 'console') return
    const maxLines = Math.min(2000, Math.max(1, consoleMaxLines))
    const fetchConsole = async () => {
      try {
        const dto = await apiClient.getConsoleOutput(maxLines)
        setConsoleLines(dto.lines ?? [])
      } catch {
        setConsoleLines([])
      }
    }
    fetchConsole()
    if (consoleRefreshPaused) return
    const id = setInterval(fetchConsole, 2000)
    return () => clearInterval(id)
  }, [selectedService, tindarrTab, consoleRefreshPaused, consoleMaxLines])

  // Auto-scroll Console pre to bottom when lines change
  useEffect(() => {
    if (tindarrTab !== 'console' || !consolePreRef.current) return
    consolePreRef.current.scrollTop = consolePreRef.current.scrollHeight
  }, [tindarrTab, consoleLines])

  // Load Media Servers data when Plex / Jellyfin / Emby pill is selected
  useEffect(() => {
    if (selectedService === 'plex') {
      setPlexServersLoading(true)
      Promise.all([apiClient.getPlexAuthStatus(), apiClient.listPlexServers()])
        .then(([auth, servers]) => {
          setPlexAuthStatus(auth)
          setPlexServers(servers)
          setPlexSyncStatus(null)
          // If any server is already syncing, subscribe to live status
          const checkRunning = async () => {
            for (const s of servers) {
              try {
                const status = await apiClient.getPlexLibrarySyncStatus('plex', s.serverId)
                if (status.state === 'running') {
                  setPlexSyncStatus(status)
                  const unsub = apiClient.subscribePlexLibrarySyncStatus('plex', s.serverId, (next) => {
                    setPlexSyncStatus(next)
                    if (next.state !== 'running') {
                      plexSyncUnsubscribeRef.current?.()
                      plexSyncUnsubscribeRef.current = null
                      apiClient.listPlexServers().then(setPlexServers)
                    }
                  })
                  plexSyncUnsubscribeRef.current = unsub
                  break
                }
              } catch {
                // ignore
              }
            }
          }
          checkRunning()
        })
        .catch(() => {
          setPlexAuthStatus(null)
          setPlexServers([])
        })
        .finally(() => setPlexServersLoading(false))
    }
    if (selectedService === 'jellyfin') {
      setJellyfinServersLoading(true)
      apiClient.listJellyfinServers().then(setJellyfinServers).catch(() => setJellyfinServers([])).finally(() => setJellyfinServersLoading(false))
    }
    if (selectedService === 'emby') {
      setEmbyServersLoading(true)
      apiClient.listEmbyServers().then(setEmbyServers).catch(() => setEmbyServers([])).finally(() => setEmbyServersLoading(false))
    }
  }, [selectedService])

  // Unsubscribe from Plex sync SSE when leaving Plex panel
  useEffect(() => {
    if (selectedService !== 'plex') {
      plexSyncUnsubscribeRef.current?.()
      plexSyncUnsubscribeRef.current = null
      setPlexSyncStatus(null)
    }
    return () => {
      plexSyncUnsubscribeRef.current?.()
      plexSyncUnsubscribeRef.current = null
    }
  }, [selectedService])

  // Plex pin: poll verify until authorized or unmount
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
          setPlexMessage({ type: 'success', text: 'Plex authorized. Refreshing servers…' })
          setPlexServersLoading(true)
          Promise.all([apiClient.syncPlexServers(), apiClient.getPlexAuthStatus()])
            .then(([servers, auth]) => {
              setPlexServers(servers)
              setPlexAuthStatus(auth)
            })
            .catch(() => setPlexMessage({ type: 'error', text: 'Failed to refresh servers' }))
            .finally(() => setPlexServersLoading(false))
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

  const handlePlexCreatePin = async () => {
    setPlexMessage(null)
    try {
      const pin = await apiClient.createPlexPin()
      setPlexPin(pin)
    } catch (err) {
      setPlexMessage({ type: 'error', text: err instanceof Error ? err.message : 'Failed to create pin' })
    }
  }

  const handlePlexSyncServers = async () => {
    setPlexMessage(null)
    setPlexServersLoading(true)
    try {
      const servers = await apiClient.syncPlexServers()
      setPlexServers(servers)
      setPlexAuthStatus(await apiClient.getPlexAuthStatus())
      setPlexMessage({ type: 'success', text: 'Servers refreshed' })
    } catch (err) {
      setPlexMessage({ type: 'error', text: err instanceof Error ? err.message : 'Failed to sync servers' })
    } finally {
      setPlexServersLoading(false)
    }
  }

  const handlePlexDeleteServer = async (serverId: string) => {
    setPlexDeleteServerId(serverId)
    setPlexMessage(null)
    try {
      await apiClient.deletePlexServer(serverId)
      setPlexServers((prev) => prev.filter((s) => s.serverId !== serverId))
      setPlexMessage({ type: 'success', text: 'Server removed' })
    } catch (err) {
      setPlexMessage({ type: 'error', text: err instanceof Error ? err.message : 'Failed to delete server' })
    } finally {
      setPlexDeleteServerId(null)
    }
  }

  const handlePlexSyncLibrary = async (serverId: string) => {
    setPlexSyncServerId(serverId)
    setPlexMessage(null)
    try {
      const status = await apiClient.startPlexLibrarySync('plex', serverId)
      setPlexSyncStatus(status)
      plexSyncUnsubscribeRef.current?.()
      const unsub = apiClient.subscribePlexLibrarySyncStatus('plex', serverId, (next) => {
        setPlexSyncStatus(next)
        if (next.state !== 'running') {
          plexSyncUnsubscribeRef.current?.()
          plexSyncUnsubscribeRef.current = null
          setPlexMessage(next.state === 'completed' ? { type: 'success', text: 'Library sync completed' } : next.state === 'failed' ? { type: 'error', text: next.message ?? 'Sync failed' } : null)
          apiClient.listPlexServers().then(setPlexServers)
        }
      })
      plexSyncUnsubscribeRef.current = unsub
    } catch (err) {
      setPlexMessage({ type: 'error', text: err instanceof Error ? err.message : 'Sync failed' })
    } finally {
      setPlexSyncServerId(null)
    }
  }

  const handlePlexTestConnection = async () => {
    setPlexTestServerId('_')
    setPlexMessage(null)
    try {
      await apiClient.syncPlexServers()
      setPlexMessage({ type: 'success', text: 'Connection OK — servers refreshed' })
    } catch (err) {
      setPlexMessage({ type: 'error', text: err instanceof Error ? err.message : 'Connection failed' })
    } finally {
      setPlexTestServerId(null)
    }
  }

  const handleJellyfinAddServer = async () => {
    const baseUrl = jellyfinAddForm.baseUrl?.trim()
    const apiKey = jellyfinAddForm.apiKey?.trim()
    if (!baseUrl || !apiKey) {
      setJellyfinMessage({ type: 'error', text: 'URL:Port and API Key are required' })
      return
    }
    setJellyfinAddSaving(true)
    setJellyfinMessage(null)
    try {
      await apiClient.putJellyfinSettings({ baseUrl, apiKey }, true)
      setJellyfinAddForm({ baseUrl: '', apiKey: '' })
      const servers = await apiClient.listJellyfinServers()
      setJellyfinServers(servers)
      setJellyfinMessage({ type: 'success', text: 'Server added' })
    } catch (err) {
      setJellyfinMessage({ type: 'error', text: err instanceof Error ? err.message : 'Failed to add server' })
    } finally {
      setJellyfinAddSaving(false)
    }
  }

  const handleJellyfinSyncLibrary = async (serverId: string) => {
    setJellyfinSyncServerId(serverId)
    setJellyfinMessage(null)
    try {
      await apiClient.postJellyfinSyncLibrary('jellyfin', serverId)
      setJellyfinMessage({ type: 'success', text: 'Library synced' })
      const servers = await apiClient.listJellyfinServers()
      setJellyfinServers(servers)
    } catch (err) {
      setJellyfinMessage({ type: 'error', text: err instanceof Error ? err.message : 'Sync failed' })
    } finally {
      setJellyfinSyncServerId(null)
    }
  }

  const handleJellyfinTestConnection = async (serverId: string) => {
    setJellyfinTestServerId(serverId)
    setJellyfinMessage(null)
    try {
      const result = await apiClient.postJellyfinTestConnection('jellyfin', serverId)
      setJellyfinMessage(result.ok ? { type: 'success', text: result.message ?? 'Connection OK' } : { type: 'error', text: result.message ?? 'Connection failed' })
    } catch (err) {
      setJellyfinMessage({ type: 'error', text: err instanceof Error ? err.message : 'Test failed' })
    } finally {
      setJellyfinTestServerId(null)
    }
  }

  const handleJellyfinDeleteServer = async (serverId: string) => {
    setJellyfinDeleteServerId(serverId)
    setJellyfinMessage(null)
    try {
      await apiClient.deleteJellyfinServer(serverId)
      setJellyfinServers((prev) => prev.filter((s) => s.serverId !== serverId))
      setJellyfinMessage({ type: 'success', text: 'Server removed' })
    } catch (err) {
      setJellyfinMessage({ type: 'error', text: err instanceof Error ? err.message : 'Failed to delete server' })
    } finally {
      setJellyfinDeleteServerId(null)
    }
  }

  const handleEmbyAddServer = async () => {
    const baseUrl = embyAddForm.baseUrl?.trim()
    const apiKey = embyAddForm.apiKey?.trim()
    if (!baseUrl || !apiKey) {
      setEmbyMessage({ type: 'error', text: 'URL:Port and API Key are required' })
      return
    }
    setEmbyAddSaving(true)
    setEmbyMessage(null)
    try {
      await apiClient.putEmbySettings({ baseUrl, apiKey }, true)
      setEmbyAddForm({ baseUrl: '', apiKey: '' })
      const servers = await apiClient.listEmbyServers()
      setEmbyServers(servers)
      setEmbyMessage({ type: 'success', text: 'Server added' })
    } catch (err) {
      setEmbyMessage({ type: 'error', text: err instanceof Error ? err.message : 'Failed to add server' })
    } finally {
      setEmbyAddSaving(false)
    }
  }

  const handleEmbySyncLibrary = async (serverId: string) => {
    setEmbySyncServerId(serverId)
    setEmbyMessage(null)
    try {
      await apiClient.postEmbySyncLibrary('emby', serverId)
      setEmbyMessage({ type: 'success', text: 'Library synced' })
      const servers = await apiClient.listEmbyServers()
      setEmbyServers(servers)
    } catch (err) {
      setEmbyMessage({ type: 'error', text: err instanceof Error ? err.message : 'Sync failed' })
    } finally {
      setEmbySyncServerId(null)
    }
  }

  const handleEmbyTestConnection = async (serverId: string) => {
    setEmbyTestServerId(serverId)
    setEmbyMessage(null)
    try {
      const result = await apiClient.postEmbyTestConnection('emby', serverId)
      setEmbyMessage(result.ok ? { type: 'success', text: result.message ?? 'Connection OK' } : { type: 'error', text: result.message ?? 'Connection failed' })
    } catch (err) {
      setEmbyMessage({ type: 'error', text: err instanceof Error ? err.message : 'Test failed' })
    } finally {
      setEmbyTestServerId(null)
    }
  }

  const handleEmbyDeleteServer = async (serverId: string) => {
    setEmbyDeleteServerId(serverId)
    setEmbyMessage(null)
    try {
      await apiClient.deleteEmbyServer(serverId)
      setEmbyServers((prev) => prev.filter((s) => s.serverId !== serverId))
      setEmbyMessage({ type: 'success', text: 'Server removed' })
    } catch (err) {
      setEmbyMessage({ type: 'error', text: err instanceof Error ? err.message : 'Failed to delete server' })
    } finally {
      setEmbyDeleteServerId(null)
    }
  }

  const formatLastSync = (utc: string | null) => (utc ? new Date(utc).toLocaleString() : '—')

  const fetchRadarrSettings = async () => {
    try {
      const data = await apiClient.getRadarrSettings('radarr', radarrServerId)
      const savedRootPath = data.rootFolderPath ?? ''
      setRadarrSettings(prev => ({
        ...prev,
        apiUrl: data.baseUrl ?? '',
        apiKey: prev.apiKey || '', // backend never returns API key; preserve in-memory
        defaultQualityProfileId: data.qualityProfileId ?? 0,
        defaultRootFolderId: 0, // resolved below when we have root folders list
        rootFolderPath: savedRootPath,
        tagLabel: data.tagLabel ?? '',
        autoAddMovies: data.autoAddEnabled ?? false,
        enabled: !!data.configured,
        autoAddIntervalSeconds: (data.autoAddIntervalMinutes ?? 5) * 60
      }))
      setHasValidatedConnection(
        !!data.hasApiKey && (data.qualityProfileId ?? 0) > 0 && !!data.rootFolderPath
      )
      // Load root folders so we can resolve defaultRootFolderId from saved path (persists on reload)
      if (data.baseUrl) {
        try {
          const folders = await apiClient.getRadarrRootFolders('radarr', radarrServerId)
          setRootFolders(folders)
          if (folders.length > 0 && savedRootPath) {
            const matchId = folders.find(f => f.path === savedRootPath)?.id
            if (matchId != null) {
              setRadarrSettings(prev => ({ ...prev, defaultRootFolderId: matchId, rootFolderPath: savedRootPath }))
            }
          }
        } catch {
          // ignore; root folders only needed to show dropdown
        }
      }
    } catch (err) {
      console.error('Failed to fetch Radarr settings:', err)
    }
  }

  const fetchQualityProfiles = async () => {
    try {
      const data = await apiClient.getRadarrQualityProfiles('radarr', radarrServerId)
      setQualityProfiles(data)
      if (data.length > 0 && radarrSettings.defaultQualityProfileId === 0) {
        setRadarrSettings(prev => ({ ...prev, defaultQualityProfileId: data[0].id }))
      }
    } catch (err) {
      console.error('Failed to fetch quality profiles:', err)
    }
  }

  const fetchRootFolders = async () => {
    try {
      const data = await apiClient.getRadarrRootFolders('radarr', radarrServerId)
      setRootFolders(data)
      if (data.length > 0) {
        setRadarrSettings(prev => {
          const matchId = prev.rootFolderPath
            ? data.find(f => f.path === prev.rootFolderPath)?.id
            : undefined
          const defaultId = matchId ?? (prev.defaultRootFolderId || data[0].id)
          const path = (prev.rootFolderPath || data.find(f => f.id === defaultId)?.path) ?? ''
          return { ...prev, defaultRootFolderId: defaultId, rootFolderPath: path }
        })
      }
    } catch (err) {
      console.error('Failed to fetch root folders:', err)
    }
  }

  const buildRadarrSettingsRequest = () => {
    const rootPath =
      (radarrSettings.rootFolderPath ||
        rootFolders.find(f => f.id === radarrSettings.defaultRootFolderId)?.path) ??
      null
    return {
      baseUrl: radarrSettings.apiUrl,
      apiKey: radarrSettings.apiKey || null,
      qualityProfileId: radarrSettings.defaultQualityProfileId || null,
      rootFolderPath: rootPath,
      tagLabel: radarrSettings.tagLabel.trim() || null,
      autoAddEnabled: radarrSettings.autoAddMovies,
      autoAddIntervalMinutes: radarrSettings.autoAddIntervalSeconds
        ? Math.round(radarrSettings.autoAddIntervalSeconds / 60)
        : null
    }
  }

  const handleTestConnection = async () => {
    setTestingConnection(true)
    setSettingsMessage(null)
    try {
      await apiClient.putRadarrSettings('radarr', radarrServerId, buildRadarrSettingsRequest())
      await fetchRadarrSettings()
      const result = await apiClient.postRadarrTestConnection('radarr', radarrServerId)
      if (result.ok) {
        setSettingsMessage({ type: 'success', text: 'Settings saved and connection successful' })
        await fetchQualityProfiles()
        await fetchRootFolders()
        setHasValidatedConnection(true)
      } else {
        setSettingsMessage({ type: 'error', text: result.message ?? 'Connection failed' })
        setHasValidatedConnection(false)
      }
    } catch (err) {
      setSettingsMessage({
        type: 'error',
        text: err instanceof Error ? err.message : 'Failed to test connection'
      })
      setHasValidatedConnection(false)
    } finally {
      setTestingConnection(false)
    }
  }

  const handleSaveSettings = async () => {
    setSavingSettings(true)
    setSettingsMessage(null)
    try {
      await apiClient.putRadarrSettings('radarr', radarrServerId, buildRadarrSettingsRequest())
      setSettingsMessage({ type: 'success', text: 'Settings saved' })
      if (radarrSettings.defaultQualityProfileId > 0 && radarrSettings.defaultRootFolderId > 0) {
        setHasValidatedConnection(true)
      }
    } catch (err) {
      setSettingsMessage({
        type: 'error',
        text: err instanceof Error ? err.message : 'Failed to save settings'
      })
    } finally {
      setSavingSettings(false)
    }
  }

  const handleSyncLibrary = async () => {
    setSyncLibraryLoading(true)
    setSettingsMessage(null)
    try {
      const data = await apiClient.postRadarrSyncLibrary('radarr', radarrServerId)
      setSettingsMessage({
        type: 'success',
        text: `Synced ${data.count} movies at ${new Date(data.syncedAtUtc).toLocaleString()}`
      })
    } catch (err) {
      setSettingsMessage({
        type: 'error',
        text: err instanceof Error ? err.message : 'Sync failed'
      })
    } finally {
      setSyncLibraryLoading(false)
    }
  }

  const handleSaveMatchSettings = async () => {
    setMatchSaving(true)
    setSettingsMessage(null)
    try {
      await apiClient.putMatchSettings('radarr', radarrServerId, {
        minUsers: matchSettings.minUsers,
        minUserPercent: matchSettings.minUserPercent
      })
      setSettingsMessage({ type: 'success', text: 'Matching settings saved' })
    } catch (err) {
      setSettingsMessage({
        type: 'error',
        text: err instanceof Error ? err.message : 'Failed to save matching settings'
      })
    } finally {
      setMatchSaving(false)
    }
  }

  const handleAutoAddMovies = async () => {
    setAddingMovieId('auto')
    try {
      const data = await apiClient.postRadarrAutoAddAcceptedMovies('radarr', radarrServerId)
      if (data.message) {
        setSettingsMessage({ type: 'error', text: data.message })
      } else {
        setSettingsMessage({
          type: 'success',
          text: `Added ${data.added} of ${data.attempted} movies (${data.skippedExisting} already in library, ${data.failed} failed)`
        })
      }
    } catch (err) {
      setSettingsMessage({
        type: 'error',
        text: err instanceof Error ? err.message : 'Failed to auto-add movies'
      })
    } finally {
      setAddingMovieId(null)
    }
  }

  const handleSaveTmdbCredentials = async () => {
    setTmdbCredentialSaving(true)
    setTmdbMessage(null)
    try {
      // Only set tmdbApiKeySet/tmdbReadAccessTokenSet when user entered a value for that field.
      // Blank means "keep current"; sending Set: true with null clears the credential in the DB.
      const apiKeyTrimmed = tmdbCredentialApiKey?.trim() ?? ''
      const tokenTrimmed = tmdbCredentialReadToken?.trim() ?? ''
      const request: Parameters<typeof apiClient.updateAdvancedSettings>[0] = {}
      if (apiKeyTrimmed !== '') {
        request.tmdbApiKeySet = true
        request.tmdbApiKey = apiKeyTrimmed
      }
      if (tokenTrimmed !== '') {
        request.tmdbReadAccessTokenSet = true
        request.tmdbReadAccessToken = tokenTrimmed
      }
      const updated = await apiClient.updateAdvancedSettings(request)
      setAdvancedSettings(updated)
      setTmdbMessage({ type: 'success', text: 'TMDB credentials saved' })
    } catch (err) {
      setTmdbMessage({ type: 'error', text: err instanceof Error ? err.message : 'Failed to save credentials' })
    } finally {
      setTmdbCredentialSaving(false)
    }
  }

  const handleSaveTmdbCacheSettings = async () => {
    if (!tmdbCacheSettings) return
    setTmdbCacheSaving(true)
    setTmdbMessage(null)
    try {
      const updated = await apiClient.putTmdbCacheSettings({
        maxRows: tmdbCacheSettings.maxRows,
        maxMovies: tmdbCacheSettings.maxMovies,
        imageCacheMaxMb: tmdbCacheSettings.imageCacheMaxMb,
        posterMode: tmdbCacheSettings.posterMode,
        prewarmOriginalLanguage: tmdbCacheSettings.prewarmOriginalLanguage || null,
        prewarmRegion: tmdbCacheSettings.prewarmRegion || null
      })
      setTmdbCacheSettings(updated)
      setTmdbMessage({ type: 'success', text: 'Cache settings saved' })
    } catch (err) {
      setTmdbMessage({ type: 'error', text: err instanceof Error ? err.message : 'Failed to save cache settings' })
    } finally {
      setTmdbCacheSaving(false)
    }
  }

  const handleStartTmdbBuild = async (rateLimitOverride: boolean, discoverLimitPerUser: number) => {
    setTmdbMessage(null)
    try {
      const status = await apiClient.postTmdbBuildStart({
        rateLimitOverride,
        discoverLimitPerUser: Math.min(1000, Math.max(1, discoverLimitPerUser)),
        prefetchImages: true
      })
      setTmdbBuildStatus(status)
      setTmdbMessage({ type: 'success', text: 'Build started' })
    } catch (err) {
      setTmdbMessage({ type: 'error', text: err instanceof Error ? err.message : 'Build start failed' })
    }
  }

  const handleCancelTmdbBuild = async () => {
    try {
      const status = await apiClient.postTmdbBuildCancel('Stopped by admin')
      setTmdbBuildStatus(status)
      setTmdbMessage({ type: 'success', text: 'Build stopped' })
    } catch (err) {
      setTmdbMessage({ type: 'error', text: err instanceof Error ? err.message : 'Stop failed' })
    }
  }

  const handleTmdbDownloadBackup = async () => {
    setTmdbImportLoading(true)
    setTmdbMessage(null)
    try {
      const blob = await apiClient.getTmdbBackupDownload()
      const url = URL.createObjectURL(blob)
      const a = document.createElement('a')
      a.href = url
      a.download = 'tindarr-tmdb-backup.zip'
      a.click()
      URL.revokeObjectURL(url)
      setTmdbMessage({ type: 'success', text: 'Backup downloaded.' })
    } catch (err) {
      setTmdbMessage({ type: 'error', text: err instanceof Error ? err.message : 'Download failed' })
    } finally {
      setTmdbImportLoading(false)
    }
  }

  const handleTmdbRestore = async () => {
    if (!tmdbImportSelectedFile) return
    const file = tmdbImportSelectedFile
    setTmdbImportLoading(true)
    setTmdbMessage(null)
    setTmdbRestoreResult(null)
    try {
      const result = await apiClient.postTmdbRestore(file)
      setTmdbRestoreResult(result)
      if (result.notImportedReasons?.length) {
        setTmdbMessage({ type: 'error', text: result.notImportedReasons.join(' ') })
      } else {
        setTmdbMessage({ type: 'success', text: 'Restore complete.' })
      }
      const cache = await apiClient.getTmdbCacheSettings()
      setTmdbCacheSettings(cache)
      setTmdbImportSelectedFile(null)
      if (tmdbImportFileInputRef.current) tmdbImportFileInputRef.current.value = ''
    } catch (err) {
      setTmdbMessage({ type: 'error', text: err instanceof Error ? err.message : 'Restore failed' })
      setTmdbRestoreResult(null)
    } finally {
      setTmdbImportLoading(false)
    }
  }

  const runBackupAction = async (
    actionId: string,
    download: () => Promise<Blob>,
    filename: string
  ) => {
    setBackupLoading(actionId)
    setBackupMessage(null)
    try {
      const blob = await download()
      const url = URL.createObjectURL(blob)
      const a = document.createElement('a')
      a.href = url
      a.download = filename
      a.click()
      URL.revokeObjectURL(url)
      setBackupMessage({ type: 'success', text: 'Download complete.' })
    } catch (err) {
      setBackupMessage({ type: 'error', text: err instanceof Error ? err.message : 'Download failed' })
    } finally {
      setBackupLoading(null)
    }
  }

  const runBackupRestore = async (
    actionId: string,
    file: File | null,
    restore: (f: File) => Promise<{ message?: string }>,
    clearFile: () => void,
    inputRef: React.RefObject<HTMLInputElement | null>
  ) => {
    if (!file) return
    setBackupLoading(actionId)
    setBackupMessage(null)
    try {
      const result = await restore(file)
      setBackupMessage({ type: 'success', text: result.message ?? 'Restore complete.' })
      clearFile()
      if (inputRef.current) inputRef.current.value = ''
    } catch (err) {
      setBackupMessage({ type: 'error', text: err instanceof Error ? err.message : 'Restore failed' })
    } finally {
      setBackupLoading(null)
    }
  }

  if (!user?.isAdmin) {
    return (
      <div className="flex h-screen items-center justify-center">
        <div className="text-center">
          <p className="text-2xl text-red-400">Access Denied</p>
          <p className="mt-2 text-gray-400">Admin access required</p>
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

        {/* Service pill buttons */}
        <div className="mb-8 flex flex-wrap gap-3">
          {servicePills.map((service) => (
            <button
              key={service.id}
              type="button"
              onClick={() => setSelectedService(service.id)}
              className={`rounded-full px-5 py-2.5 text-sm font-semibold transition-all ${
                selectedService === service.id
                  ? 'bg-pink-500 text-white shadow-lg shadow-pink-500/30 ring-2 ring-pink-400'
                  : 'bg-slate-700/80 text-gray-200 ring-1 ring-slate-600 hover:bg-slate-600/80 hover:text-white hover:ring-slate-500'
              }`}
            >
              {service.label}
            </button>
          ))}
        </div>

        {/* Tindarr: Check for updates (at top of Tindarr settings) */}
        {selectedService === 'tindarr' && (
          <div className="mb-6 rounded-lg bg-gray-800 p-4">
            <h3 className="text-lg font-bold text-white mb-3">Check for updates</h3>
            <p className="text-gray-400 text-sm mb-3">Compare the running Tindarr version with the latest GitHub release.</p>
            <div className="flex flex-wrap items-center gap-4">
              <button
                type="button"
                disabled={updateCheckLoading}
                onClick={async () => {
                  setUpdateCheckLoading(true)
                  setUpdateCheck(null)
                  try {
                    const result = await apiClient.getAdminUpdateCheck()
                    setUpdateCheck(result)
                  } catch (err) {
                    setUpdateCheck({
                      currentVersion: '',
                      latestVersion: null,
                      updateAvailable: false,
                      checkedAtUtc: new Date().toISOString(),
                      latestReleaseUrl: null,
                      latestReleaseName: null,
                      publishedAtUtc: null,
                      isPreRelease: null,
                      releaseNotes: null,
                      error: err instanceof Error ? err.message : 'Check failed',
                    })
                  } finally {
                    setUpdateCheckLoading(false)
                  }
                }}
                className="rounded bg-pink-600 px-4 py-2 text-white font-medium hover:bg-pink-500 disabled:opacity-50 disabled:cursor-not-allowed"
              >
                {updateCheckLoading ? 'Checking…' : 'Check for updates'}
              </button>
              {updateCheck && !updateCheck.error && (
                <span className="text-gray-300 text-sm">
                  Current: <strong className="text-white">{updateCheck.currentVersion}</strong>
                  {updateCheck.latestVersion != null && (
                    <> · Latest: <strong className="text-white">{updateCheck.latestVersion}</strong></>
                  )}
                  {updateCheck.updateAvailable && updateCheck.latestReleaseUrl && (
                    <a
                      href={updateCheck.latestReleaseUrl}
                      target="_blank"
                      rel="noopener noreferrer"
                      className="ml-2 inline-flex items-center rounded bg-amber-500/20 px-2 py-0.5 text-amber-400 text-sm font-medium hover:bg-amber-500/30"
                    >
                      Update available →
                    </a>
                  )}
                </span>
              )}
              {updateCheck?.error && (
                <span className="text-red-400 text-sm">{updateCheck.error}</span>
              )}
            </div>
            {updateCheck && !updateCheck.error && updateCheck.checkedAtUtc && (
              <p className="text-gray-500 text-xs mt-2">Last checked: {new Date(updateCheck.checkedAtUtc).toLocaleString()}</p>
            )}
          </div>
        )}

        {/* Tindarr sub-tabs (when Tindarr pill is selected) */}
        {selectedService === 'tindarr' && (
          <div className="mb-8 flex gap-4 border-b border-gray-700 overflow-x-auto">
            {(['users', 'rooms', 'db', 'casting', 'advanced', 'console'] as const).map((tab) => (
              <button
                key={tab}
                onClick={() => { setTindarrTab(tab); setTindarrMessage(null) }}
                className={`px-4 py-2 font-semibold transition-colors whitespace-nowrap ${
                  tindarrTab === tab ? 'border-b-2 border-pink-500 text-pink-500' : 'text-gray-400 hover:text-gray-200'
                }`}
              >
                {tab === 'users' ? 'User Management' : tab === 'rooms' ? 'Rooms' : tab === 'db' ? 'DB' : tab === 'casting' ? 'Casting' : tab === 'advanced' ? 'Advanced' : 'Console'}
              </button>
            ))}
          </div>
        )}

        {/* Tindarr: User Management */}
        {selectedService === 'tindarr' && tindarrTab === 'users' && (
          <div className="space-y-6">
            {tindarrMessage && (
              <div className={`rounded-lg px-4 py-3 ${tindarrMessage.type === 'success' ? 'bg-green-500/10 text-green-400' : 'bg-red-500/10 text-red-400'}`}>
                {tindarrMessage.text}
              </div>
            )}
            <div className="rounded-lg bg-gray-800 p-6">
              <h3 className="text-lg font-bold text-white mb-4">Add user</h3>
              <div className="flex flex-wrap gap-4 items-end">
                <div>
                  <label className="block text-sm text-gray-400 mb-1">User ID</label>
                  <input
                    type="text"
                    value={newUserForm.userId}
                    onChange={(e) => setNewUserForm((f) => ({ ...f, userId: e.target.value }))}
                    className="rounded bg-gray-700 px-3 py-2 text-white w-40"
                    placeholder="e.g. alice"
                  />
                </div>
                <div>
                  <label className="block text-sm text-gray-400 mb-1">Display name</label>
                  <input
                    type="text"
                    value={newUserForm.displayName}
                    onChange={(e) => setNewUserForm((f) => ({ ...f, displayName: e.target.value }))}
                    className="rounded bg-gray-700 px-3 py-2 text-white w-40"
                    placeholder="Alice"
                  />
                </div>
                <div>
                  <label className="block text-sm text-gray-400 mb-1">Password</label>
                  <input
                    type="password"
                    value={newUserForm.password}
                    onChange={(e) => setNewUserForm((f) => ({ ...f, password: e.target.value }))}
                    className="rounded bg-gray-700 px-3 py-2 text-white w-40"
                    placeholder="••••••••"
                  />
                </div>
                <div>
                  <label className="block text-sm text-gray-400 mb-1">Role</label>
                  <button
                    type="button"
                    onClick={() => setNewUserForm((f) => ({ ...f, role: nextRole(f.role) }))}
                    className={`rounded-full px-3 py-1.5 text-xs font-semibold text-white ${rolePillClass(newUserForm.role)}`}
                  >
                    {newUserForm.role}
                  </button>
                  <p className="text-xs text-gray-500 mt-1">Click to cycle: Contributor → Curator → Admin</p>
                </div>
                <button
                  type="button"
                  onClick={async () => {
                    const uid = newUserForm.userId.trim()
                    if (/[^a-zA-Z0-9_-]/.test(uid)) {
                      setTindarrMessage({ type: 'error', text: 'User ID must contain only letters, digits, hyphens, and underscores' })
                      return
                    }
                    try {
                      await apiClient.createAdminUser({
                        userId: uid,
                        displayName: newUserForm.displayName.trim() || uid,
                        password: newUserForm.password,
                        roles: [newUserForm.role],
                      })
                      setTindarrMessage({ type: 'success', text: 'User created' })
                      setNewUserForm({ userId: '', displayName: '', password: '', role: 'Contributor' })
                      const list = await apiClient.listAdminUsers()
                      setAdminUsers(list)
                    } catch (err) {
                      setTindarrMessage({ type: 'error', text: err instanceof Error ? err.message : 'Failed to create user' })
                    }
                  }}
                  className="rounded-full bg-pink-600 px-4 py-2 text-sm font-semibold text-white hover:bg-pink-700"
                >
                  Add user
                </button>
              </div>
            </div>
            {registrationSettings?.requireAdminApprovalForNewUsers && (
              <p className="mb-4 text-sm text-amber-200">
                New users require admin approval before they can log in. Approve pending users below.
              </p>
            )}
            <div className="rounded-lg bg-gray-800 overflow-hidden">
              <h3 className="text-lg font-bold text-white p-4 border-b border-gray-700">Current users</h3>
              {adminUsersLoading ? (
                <div className="p-8 text-center text-gray-400">Loading...</div>
              ) : (
                <table className="w-full text-left text-gray-300">
                  <thead className="bg-gray-900 text-gray-200">
                    <tr>
                      <th className="px-4 py-3">UID</th>
                      <th className="px-4 py-3">Display name</th>
                      <th className="px-4 py-3">Role</th>
                      <th className="px-4 py-3">Password</th>
                      <th className="px-4 py-3">Actions</th>
                    </tr>
                  </thead>
                  <tbody>
                    {adminUsers.map((u) => (
                      <tr key={u.userId} className="border-t border-gray-700">
                        <td className="px-4 py-3 font-mono text-sm">{u.userId}</td>
                        <td className="px-4 py-3">
                          {editingUserId === u.userId ? (
                            <input
                              type="text"
                              value={editingDisplayName}
                              onChange={(e) => setEditingDisplayName(e.target.value)}
                              className="rounded bg-gray-700 px-2 py-1 text-white w-40"
                              autoFocus
                            />
                          ) : (
                            <span
                              className="cursor-pointer hover:text-white"
                              onClick={() => { setEditingUserId(u.userId); setEditingDisplayName(u.displayName) }}
                            >
                              {u.displayName}
                            </span>
                          )}
                          {editingUserId === u.userId && (
                            <button
                              type="button"
                              onClick={async () => {
                                try {
                                  await apiClient.updateAdminUser(editingUserId, { displayName: editingDisplayName })
                                  setAdminUsers((prev) => prev.map((x) => (x.userId === editingUserId ? { ...x, displayName: editingDisplayName } : x)))
                                  setEditingUserId(null)
                                  setTindarrMessage({ type: 'success', text: 'Display name updated' })
                                } catch (err) {
                                  setTindarrMessage({ type: 'error', text: err instanceof Error ? err.message : 'Update failed' })
                                }
                              }}
                              className="ml-2 text-pink-400 text-sm"
                            >
                              Save
                            </button>
                          )}
                        </td>
                        <td className="px-4 py-3">
                          <button
                            type="button"
                            onClick={async () => {
                              const isPending = u.roles.length === 1 && u.roles[0].toLowerCase() === 'pendingapproval'
                              const next = isPending
                                ? (registrationSettings?.defaultRole ?? 'Contributor')
                                : nextRole(u.roles[0] || 'Contributor')
                              try {
                                await apiClient.setAdminUserRoles(u.userId, { roles: [next] })
                                setAdminUsers((prev) => prev.map((x) => (x.userId === u.userId ? { ...x, roles: [next] } : x)))
                                setTindarrMessage({ type: 'success', text: isPending ? 'User approved' : 'Role updated' })
                              } catch (err) {
                                setTindarrMessage({ type: 'error', text: err instanceof Error ? err.message : (isPending ? 'Approve failed' : 'Failed') })
                              }
                            }}
                            className={`rounded-full px-3 py-1 text-xs font-semibold text-white cursor-pointer hover:opacity-90 ${u.roles.length === 1 && u.roles[0].toLowerCase() === 'pendingapproval' ? 'bg-amber-600/80' : rolePillClass(u.roles[0] || 'Contributor')}`}
                          >
                            {u.roles.length === 1 && u.roles[0].toLowerCase() === 'pendingapproval' ? 'Pending approval' : (u.roles[0] || '—')}
                          </button>
                          <p className="text-xs text-gray-500 mt-0.5">Click: {u.roles.length === 1 && u.roles[0].toLowerCase() === 'pendingapproval' ? 'Approve (set to default role)' : 'Contributor → Curator → Admin → Contributor'}</p>
                        </td>
                        <td className="px-4 py-3">
                          {u.hasPassword ? (
                            <span className="text-gray-500 text-sm">Set</span>
                          ) : (
                            <span className="text-amber-400 text-sm">None</span>
                          )}
                          <button
                            type="button"
                            className="ml-2 text-pink-400 text-sm"
                            onClick={() => {
                              const newPass = window.prompt('New password for ' + u.userId)
                              if (!newPass) return
                              apiClient.adminSetPassword(u.userId, { newPassword: newPass }).then(() => setTindarrMessage({ type: 'success', text: 'Password updated' })).catch((err) => setTindarrMessage({ type: 'error', text: err instanceof Error ? err.message : 'Failed' }))
                            }}
                          >
                            Change
                          </button>
                        </td>
                        <td className="px-4 py-3">
                          <button
                            type="button"
                            className="rounded bg-red-600/80 px-2 py-1 text-xs text-white hover:bg-red-600"
                            onClick={() => { if (window.confirm(`Delete user ${u.userId}?`)) apiClient.deleteAdminUser(u.userId).then(() => { setAdminUsers((prev) => prev.filter((x) => x.userId !== u.userId)); setTindarrMessage({ type: 'success', text: 'User deleted' }) }).catch((err) => setTindarrMessage({ type: 'error', text: err instanceof Error ? err.message : 'Delete failed' })) }}
                          >
                            Delete
                          </button>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )}
            </div>
          </div>
        )}

        {/* Tindarr: Rooms */}
        {selectedService === 'tindarr' && tindarrTab === 'rooms' && (
          <div className="space-y-6">
            {tindarrMessage && (
              <div className={`rounded-lg px-4 py-3 ${tindarrMessage.type === 'success' ? 'bg-green-500/10 text-green-400' : 'bg-red-500/10 text-red-400'}`}>{tindarrMessage.text}</div>
            )}
            {joinAddressLoading && !joinAddress ? (
              <div className="rounded-lg bg-gray-800 p-8 text-center text-gray-400">Loading...</div>
            ) : (
            <div className="rounded-lg bg-gray-800 p-6">
              <h3 className="text-lg font-bold text-white mb-4">Host:Port (LAN & WAN)</h3>
              <div className="grid grid-cols-1 md:grid-cols-2 gap-4 mb-4">
                <div>
                  <label className="block text-sm text-gray-400 mb-1">LAN host:port</label>
                  <input
                    type="text"
                    value={joinAddress?.lanHostPort ?? ''}
                    onChange={(e) => setJoinAddress((s) => s ? { ...s, lanHostPort: e.target.value || null } : null)}
                    className="w-full rounded bg-gray-700 px-3 py-2 text-white"
                    placeholder="e.g. 192.168.1.10:5000"
                  />
                </div>
                <div>
                  <label className="block text-sm text-gray-400 mb-1">WAN host:port</label>
                  <input
                    type="text"
                    value={joinAddress?.wanHostPort ?? ''}
                    onChange={(e) => setJoinAddress((s) => s ? { ...s, wanHostPort: e.target.value || null } : null)}
                    className="w-full rounded bg-gray-700 px-3 py-2 text-white"
                    placeholder="e.g. example.com:5000"
                  />
                </div>
              </div>
              <h3 className="text-lg font-bold text-white mb-4 mt-6">Room Expiry</h3>
              <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                <div>
                  <label className="block text-sm text-gray-400 mb-1">Room lifetime (minutes)</label>
                  <input
                    type="number"
                    min={1}
                    placeholder="360"
                    value={joinAddress?.roomLifetimeMinutes ?? ''}
                    onChange={(e) => setJoinAddress((s) => s ? { ...s, roomLifetimeMinutes: e.target.value ? parseInt(e.target.value, 10) : null } : null)}
                    className="w-full rounded bg-gray-700 px-3 py-2 text-white placeholder-gray-500"
                  />
                  <p className="text-xs text-gray-500 mt-1">Default: 360</p>
                </div>
                <div>
                  <label className="block text-sm text-gray-400 mb-1">Guest session lifetime (minutes)</label>
                  <input
                    type="number"
                    min={1}
                    placeholder="120"
                    value={joinAddress?.guestSessionLifetimeMinutes ?? ''}
                    onChange={(e) => setJoinAddress((s) => s ? { ...s, guestSessionLifetimeMinutes: e.target.value ? parseInt(e.target.value, 10) : null } : null)}
                    className="w-full rounded bg-gray-700 px-3 py-2 text-white placeholder-gray-500"
                  />
                  <p className="text-xs text-gray-500 mt-1">Default: 120</p>
                </div>
              </div>
              <button
                type="button"
                disabled={!joinAddress || joinAddressLoading}
                onClick={async () => {
                  if (!joinAddress) return
                  try {
                    await apiClient.updateJoinAddressSettings({
                      lanHostPort: joinAddress.lanHostPort,
                      wanHostPort: joinAddress.wanHostPort,
                      roomLifetimeMinutes: joinAddress.roomLifetimeMinutes ?? undefined,
                      guestSessionLifetimeMinutes: joinAddress.guestSessionLifetimeMinutes ?? undefined,
                    })
                    setTindarrMessage({ type: 'success', text: 'Rooms settings saved' })
                  } catch (err) {
                    setTindarrMessage({ type: 'error', text: err instanceof Error ? err.message : 'Save failed' })
                  }
                }}
                className="mt-6 rounded-full bg-pink-600 px-5 py-2.5 text-sm font-semibold text-white hover:bg-pink-700 disabled:opacity-50"
              >
                Save
              </button>
            </div>
            )}
          </div>
        )}

        {/* Tindarr: DB */}
        {selectedService === 'tindarr' && tindarrTab === 'db' && (
          <div className="space-y-6">
            <div className="rounded-lg bg-gray-800 p-6">
              <div className="flex flex-wrap gap-4 items-center mb-4">
                <div className="flex gap-2">
                  <button type="button" onClick={() => setDbViewMode('table')} className={`rounded-full px-4 py-2 text-sm font-semibold ${dbViewMode === 'table' ? 'bg-pink-500 text-white' : 'bg-gray-600 text-gray-300'}`}>Table</button>
                  <button type="button" onClick={() => setDbViewMode('gallery')} className={`rounded-full px-4 py-2 text-sm font-semibold ${dbViewMode === 'gallery' ? 'bg-pink-500 text-white' : 'bg-gray-600 text-gray-300'}`}>Gallery</button>
                </div>
                <div>
                  <label className="block text-sm text-gray-400 mb-1">Scope (enabled services, per server)</label>
                  <select
                    value={dbScope ? `${dbScope.serviceType}:${dbScope.serverId}` : ''}
                    onChange={(e) => { const v = e.target.value; if (v) { const [st, sid] = v.split(':'); setDbScope({ serviceType: st, serverId: sid }) } }}
                    className="rounded bg-gray-700 px-3 py-2 text-white"
                  >
                    {dbScopes.map((s) => (
                      <option key={`${s.serviceType}:${s.serverId}`} value={`${s.serviceType}:${s.serverId}`}>{s.displayName}</option>
                    ))}
                  </select>
                </div>
                <div className="flex items-end gap-2">
                  <button
                    type="button"
                    disabled={dbPopulateLoading || dbPopulateStatus?.state === 'running'}
                    onClick={async () => {
                      setDbPopulateMessage(null)
                      setDbPopulateLoading(true)
                      try {
                        const res = await apiClient.setupComplete({
                          runLibrarySync: false,
                          runTmdbBuild: false,
                          runFetchAllDetails: true,
                          runFetchAllImages: true,
                        })
                        setDbPopulateMessage(res.message || 'Populate started.')
                        apiClient.getPopulateStatus().then(setDbPopulateStatus).catch(() => {})
                        if (dbScope) {
                          const r = await apiClient.getAdminDbMovies(dbScope.serviceType, dbScope.serverId, 0, 50)
                          setDbMovies(r)
                        }
                      } catch (err) {
                        setDbPopulateMessage(err instanceof Error ? err.message : 'Populate failed')
                      } finally {
                        setDbPopulateLoading(false)
                      }
                    }}
                    className="rounded-full bg-pink-600 px-4 py-2 text-sm font-semibold text-white hover:bg-pink-700 disabled:opacity-50"
                  >
                    {dbPopulateLoading || dbPopulateStatus?.state === 'running' ? 'Populating…' : 'Populate'}
                  </button>
                  {dbPopulateMessage && <span className="text-sm text-gray-400">{dbPopulateMessage}</span>}
                </div>
              </div>
              <p className="text-xs text-gray-500 mb-2">Populate fetches Details and Images into DB / tmdb-images when not cached.</p>
              {dbPopulateStatus && (() => {
                const isRunning = dbPopulateStatus.state === 'running'
                const total = dbPopulateStatus.detailsTotal + dbPopulateStatus.imagesTotal
                const done = dbPopulateStatus.detailsDone + dbPopulateStatus.imagesDone
                const progressPct = isRunning && total > 0 ? Math.min(1, done / total) : 0
                const showProgressBar = isRunning && total > 0
                return (
                  <div className="mt-4 rounded-lg border border-gray-700 overflow-hidden relative">
                    {showProgressBar && (
                      <div className="absolute inset-0 bg-gray-800" aria-hidden>
                        <div
                          className="h-full bg-pink-600/40 transition-all duration-500 ease-out"
                          style={{ width: `${progressPct * 100}%` }}
                        />
                      </div>
                    )}
                    <div className="relative z-10 p-4 bg-gray-900/50">
                      <p className="text-white font-medium mb-1">Populate: {dbPopulateStatus.state}</p>
                      {dbPopulateStatus.lastMessage && <p className="text-sm text-gray-400">{dbPopulateStatus.lastMessage}</p>}
                      <p className="text-sm text-gray-400 mt-1">
                        Details: {dbPopulateStatus.detailsDone} / {dbPopulateStatus.detailsTotal} · Images: {dbPopulateStatus.imagesDone} / {dbPopulateStatus.imagesTotal}
                      </p>
                      {showProgressBar && (
                        <p className="text-xs text-gray-500 mt-2">
                          {Math.round(progressPct * 100)}%
                        </p>
                      )}
                    </div>
                  </div>
                )
              })()}
              {dbMoviesLoading && <p className="text-gray-400">Loading...</p>}
              {dbMovies && !dbMoviesLoading && (
                dbViewMode === 'table' ? (
                  <table className="w-full text-left text-gray-300">
                    <thead className="bg-gray-900 text-gray-200">
                      <tr>
                        <th className="px-4 py-3">TMDB ID</th>
                        <th className="px-4 py-3">Title (year)</th>
                        <th className="px-4 py-3">Details in DB</th>
                        <th className="px-4 py-3">Images cached?</th>
                        <th className="px-4 py-3">Last updated</th>
                      </tr>
                    </thead>
                    <tbody>
                      {dbMovies.items.map((m) => (
                        <tr key={m.tmdbId} className="border-t border-gray-700">
                          <td className="px-4 py-3 font-mono">{m.tmdbId}</td>
                          <td className="px-4 py-3">{m.title}{m.releaseYear ? ` (${m.releaseYear})` : ''}</td>
                          <td className="px-4 py-3">{m.detailsFetchedAtUtc ? 'Yes' : 'No'}</td>
                          <td className="px-4 py-3">{m.posterCached || m.backdropCached ? 'Yes' : 'No'}</td>
                          <td className="px-4 py-3 text-sm">{m.updatedAtUtc ? new Date(m.updatedAtUtc).toLocaleString() : '—'}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                ) : (
                  <div className="grid grid-cols-2 sm:grid-cols-4 md:grid-cols-6 gap-4">
                    {dbMovies.items.map((m) => (
                      <div key={m.tmdbId} className="rounded bg-gray-700 p-2 text-center">
                        <p className="font-mono text-xs text-gray-400">{m.tmdbId}</p>
                        <p className="text-white text-sm truncate" title={m.title}>{m.title}</p>
                        <p className="text-xs text-gray-500">{m.releaseYear ?? '—'}</p>
                      </div>
                    ))}
                  </div>
                )
              )}
              {dbMovies && dbMovies.hasMore && (
                <button
                  type="button"
                  onClick={() => dbScope && apiClient.getAdminDbMovies(dbScope.serviceType, dbScope.serverId, dbMovies!.nextSkip, 50).then((r) => setDbMovies((prev) => prev ? { ...r, items: [...prev.items, ...r.items] } : r))}
                  className="mt-4 text-pink-400 text-sm"
                >
                  Load more
                </button>
              )}
            </div>
          </div>
        )}

        {/* Tindarr: Casting */}
        {selectedService === 'tindarr' && tindarrTab === 'casting' && (
          <div className="space-y-6">
            <div className="rounded-lg bg-gray-800 p-6">
              <h3 className="text-lg font-bold text-white mb-4">Diagnostics</h3>
              {castingDiagLoading ? <p className="text-gray-400">Loading...</p> : castingDiagnostics && (
                <div className="space-y-4">
                  <p className="text-gray-300">Active sessions: {castingDiagnostics.activeSessions.length}</p>
                  <ul className="text-sm text-gray-400 space-y-1">
                    {castingDiagnostics.recentEvents.slice(-10).reverse().map((ev) => (
                      <li key={ev.eventId}>{new Date(ev.occurredAtUtc).toLocaleTimeString()} — {ev.eventType}: {ev.message}</li>
                    ))}
                  </ul>
                </div>
              )}
            </div>
            <div className="rounded-lg bg-gray-800 p-6">
              <h3 className="text-lg font-bold text-white mb-4">Policy</h3>
              <p className="text-amber-400/90 text-sm mb-4">(WIP — Casting may not work on all servers.)</p>
              {castingLoading ? <p className="text-gray-400">Loading...</p> : castingSettings && (
                <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
                  <div>
                    <h4 className="text-white font-semibold mb-3">Subtitles</h4>
                    <div className="space-y-3">
                      <div>
                        <label className="block text-xs text-gray-400 mb-1">Preferred Source</label>
                        <select value={castingSettings.preferredSubtitleSource ?? ''} onChange={(e) => setCastingSettings((s) => s ? { ...s, preferredSubtitleSource: e.target.value || null } : null)} className="w-full rounded bg-gray-700 px-3 py-2 text-white text-sm">
                          <option value="">—</option>
                          <option value="embedded">embedded</option>
                          <option value="neighboring file">neighboring file</option>
                          <option value="none">none</option>
                        </select>
                      </div>
                      <div>
                        <label className="block text-xs text-gray-400 mb-1">Preferred Language</label>
                        <select value={castingSettings.preferredSubtitleLanguage ?? ''} onChange={(e) => setCastingSettings((s) => s ? { ...s, preferredSubtitleLanguage: e.target.value || null } : null)} className="w-full rounded bg-gray-700 px-3 py-2 text-white text-sm">
                          <option value="">—</option>
                          {['en', 'zh', 'hi', 'es', 'fr', 'ar', 'bn', 'pt', 'ru', 'ja'].map((code) => (
                            <option key={code} value={code}>{code === 'en' ? 'English' : code === 'zh' ? 'Chinese' : code === 'hi' ? 'Hindi' : code === 'es' ? 'Spanish' : code === 'fr' ? 'French' : code === 'ar' ? 'Arabic' : code === 'bn' ? 'Bengali' : code === 'pt' ? 'Portuguese' : code === 'ru' ? 'Russian' : 'Japanese'}</option>
                          ))}
                        </select>
                      </div>
                      <div>
                        <label className="block text-xs text-gray-400 mb-1">Fallback</label>
                        <select value={castingSettings.subtitleFallback ?? ''} onChange={(e) => setCastingSettings((s) => s ? { ...s, subtitleFallback: e.target.value || null } : null)} className="w-full rounded bg-gray-700 px-3 py-2 text-white text-sm">
                          <option value="">—</option>
                          <option value="auto">Auto</option>
                          <option value="none">none</option>
                          <option value="embedded">embedded</option>
                        </select>
                      </div>
                    </div>
                  </div>
                  <div>
                    <h4 className="text-white font-semibold mb-3">Audio</h4>
                    <div className="space-y-3">
                      <div>
                        <label className="block text-xs text-gray-400 mb-1">Preferred Style</label>
                        <select value={castingSettings.preferredAudioStyle ?? ''} onChange={(e) => setCastingSettings((s) => s ? { ...s, preferredAudioStyle: e.target.value || null } : null)} className="w-full rounded bg-gray-700 px-3 py-2 text-white text-sm">
                          <option value="">—</option>
                          <option value="DD5.1">DD5.1</option>
                          <option value="DD2.0">DD2.0</option>
                          <option value="2Ch">2Ch</option>
                          <option value="5.1">5.1</option>
                          <option value="7.1">7.1</option>
                          <option value="Stereo">Stereo</option>
                          <option value="Mono">Mono</option>
                        </select>
                      </div>
                      <div>
                        <label className="block text-xs text-gray-400 mb-1">Preferred Language</label>
                        <select value={castingSettings.preferredAudioLanguage ?? ''} onChange={(e) => setCastingSettings((s) => s ? { ...s, preferredAudioLanguage: e.target.value || null } : null)} className="w-full rounded bg-gray-700 px-3 py-2 text-white text-sm">
                          <option value="">—</option>
                          {['en', 'zh', 'hi', 'es', 'fr', 'ar', 'bn', 'pt', 'ru', 'ja'].map((code) => (
                            <option key={code} value={code}>{code === 'en' ? 'English' : code === 'zh' ? 'Chinese' : code === 'hi' ? 'Hindi' : code === 'es' ? 'Spanish' : code === 'fr' ? 'French' : code === 'ar' ? 'Arabic' : code === 'bn' ? 'Bengali' : code === 'pt' ? 'Portuguese' : code === 'ru' ? 'Russian' : 'Japanese'}</option>
                          ))}
                        </select>
                      </div>
                      <div>
                        <label className="block text-xs text-gray-400 mb-1">Style Fallback</label>
                        <select value={castingSettings.audioFallback ?? ''} onChange={(e) => setCastingSettings((s) => s ? { ...s, audioFallback: e.target.value || null } : null)} className="w-full rounded bg-gray-700 px-3 py-2 text-white text-sm">
                          <option value="">—</option>
                          <option value="auto">Auto</option>
                          <option value="2ch">2ch</option>
                        </select>
                      </div>
                      <div>
                        <label className="block text-xs text-gray-400 mb-1">Language Fallback</label>
                        <select value={castingSettings.audioLanguageFallback ?? 'auto'} onChange={(e) => setCastingSettings((s) => s ? { ...s, audioLanguageFallback: e.target.value || null } : null)} className="w-full rounded bg-gray-700 px-3 py-2 text-white text-sm">
                          <option value="auto">Auto</option>
                        </select>
                      </div>
                    </div>
                  </div>
                </div>
              )}
              <button
                type="button"
                disabled={!castingSettings || castingLoading}
                onClick={async () => {
                  if (!castingSettings) return
                  try {
                    await apiClient.updateCastingSettings({
                      preferredSubtitleSource: castingSettings.preferredSubtitleSource,
                      preferredSubtitleLanguage: castingSettings.preferredSubtitleLanguage,
                      preferredSubtitleTrackSource: castingSettings.preferredSubtitleTrackSource,
                      subtitleFallback: castingSettings.subtitleFallback,
                      subtitleLanguageFallback: castingSettings.subtitleLanguageFallback,
                      subtitleTrackSourceFallback: castingSettings.subtitleTrackSourceFallback,
                      preferredAudioStyle: castingSettings.preferredAudioStyle,
                      preferredAudioLanguage: castingSettings.preferredAudioLanguage,
                      preferredAudioTrackKind: castingSettings.preferredAudioTrackKind,
                      audioFallback: castingSettings.audioFallback,
                      audioLanguageFallback: castingSettings.audioLanguageFallback,
                      audioTrackKindFallback: castingSettings.audioTrackKindFallback,
                    })
                    setTindarrMessage({ type: 'success', text: 'Casting policy saved' })
                  } catch (err) {
                    setTindarrMessage({ type: 'error', text: err instanceof Error ? err.message : 'Save failed' })
                  }
                }}
                className="mt-4 rounded-full bg-pink-600 px-5 py-2.5 text-sm font-semibold text-white hover:bg-pink-700 disabled:opacity-50"
              >
                Save
              </button>
            </div>
          </div>
        )}

        {/* Tindarr: Advanced */}
        {selectedService === 'tindarr' && tindarrTab === 'advanced' && (
          <div className="space-y-6">
            {tindarrMessage && (
              <div className={`rounded-lg px-4 py-3 ${tindarrMessage.type === 'success' ? 'bg-green-500/10 text-green-400' : 'bg-red-500/10 text-red-400'}`}>{tindarrMessage.text}</div>
            )}

            {/* Registration (editable) */}
            <div className="rounded-lg bg-gray-800 p-6">
              <h3 className="text-lg font-bold text-white mb-4">Registration</h3>
              {advancedLoading && !registrationSettings ? (
                <p className="text-gray-400">Loading...</p>
              ) : registrationSettings ? (
                <div className="space-y-4">
                  <div className="flex items-center gap-3">
                    <label className="flex items-center gap-2 text-white cursor-pointer">
                      <input
                        type="checkbox"
                        checked={registrationSettings.allowOpenRegistration}
                        onChange={(e) => setRegistrationSettings((s) => s ? { ...s, allowOpenRegistration: e.target.checked, ...(e.target.checked ? {} : { requireAdminApprovalForNewUsers: false }) } : null)}
                        className="rounded"
                      />
                      Allow open registration
                    </label>
                  </div>
                  <div className="flex items-center gap-3">
                    <label className={`flex items-center gap-2 cursor-pointer ${!registrationSettings.allowOpenRegistration ? 'text-gray-500' : 'text-white'}`}>
                      <input
                        type="checkbox"
                        checked={registrationSettings.requireAdminApprovalForNewUsers}
                        onChange={(e) => setRegistrationSettings((s) => s ? { ...s, requireAdminApprovalForNewUsers: e.target.checked } : null)}
                        disabled={!registrationSettings.allowOpenRegistration}
                        className="rounded disabled:opacity-50"
                      />
                      Require admin approval for new users
                    </label>
                    {!registrationSettings.allowOpenRegistration && (
                      <span className="text-xs text-gray-500">Only when open registration is on</span>
                    )}
                  </div>
                  <div>
                    <label className="block text-sm text-gray-400 mb-1">Default role for new users</label>
                    <button
                      type="button"
                      onClick={() => {
                        const current = ROLES_CYCLE.includes(registrationSettings.defaultRole as RoleLevel) ? registrationSettings.defaultRole : 'Contributor'
                        setRegistrationSettings((s) => s ? { ...s, defaultRole: nextRole(current) } : null)
                      }}
                      className={`rounded-full px-3 py-1.5 text-xs font-semibold text-white cursor-pointer hover:opacity-90 ${rolePillClass(ROLES_CYCLE.includes(registrationSettings.defaultRole as RoleLevel) ? registrationSettings.defaultRole : 'Contributor')}`}
                    >
                      {ROLES_CYCLE.includes(registrationSettings.defaultRole as RoleLevel) ? registrationSettings.defaultRole : 'Contributor'}
                    </button>
                    <p className="text-xs text-gray-500 mt-0.5">Click: Contributor → Curator → Admin → Contributor</p>
                  </div>
                  <button
                    type="button"
                    onClick={async () => {
                      if (!registrationSettings) return
                      try {
                        const updated = await apiClient.updateRegistrationSettings({
                          allowOpenRegistration: registrationSettings.allowOpenRegistration,
                          requireAdminApprovalForNewUsers: registrationSettings.allowOpenRegistration ? registrationSettings.requireAdminApprovalForNewUsers : false,
                          defaultRole: registrationSettings.defaultRole || null,
                        })
                        setRegistrationSettings(updated)
                        setTindarrMessage({ type: 'success', text: 'Registration settings saved' })
                      } catch (err) {
                        setTindarrMessage({ type: 'error', text: err instanceof Error ? err.message : 'Save failed' })
                      }
                    }}
                    className="rounded-full bg-pink-600 px-4 py-2 text-sm font-semibold text-white hover:bg-pink-700"
                  >
                    Save registration
                  </button>
                </div>
              ) : null}
            </div>

            <div className="rounded-lg bg-gray-800 p-6">
              <h3 className="text-lg font-bold text-white mb-4">TINDARR API Rate Limit</h3>
              <p className="text-gray-400 text-sm mb-2">Default 200 requests per 1 minute window.</p>
              {advancedLoading ? <p className="text-gray-400">Loading...</p> : advancedSettings && (
                <div className="space-y-4">
                  <div className="flex items-center gap-4">
                    <label className="flex items-center gap-2 text-white">
                      <input type="checkbox" checked={advancedSettings.apiRateLimit.enabled} onChange={(e) => setAdvancedSettings((s) => s ? { ...s, apiRateLimit: { ...s.apiRateLimit, enabled: e.target.checked } } : null)} className="rounded" />
                      Enable rate limit
                    </label>
                  </div>
                  <div className="flex gap-4 flex-wrap">
                    <div>
                      <label className="block text-sm text-gray-400 mb-1">Permit limit (req)</label>
                      <input type="number" min={1} max={10000} value={advancedSettings.apiRateLimit.permitLimit} onChange={(e) => setAdvancedSettings((s) => s ? { ...s, apiRateLimit: { ...s.apiRateLimit, permitLimit: parseInt(e.target.value, 10) || 200 } } : null)} className="rounded bg-gray-700 px-3 py-2 text-white w-24" />
                    </div>
                    <div>
                      <label className="block text-sm text-gray-400 mb-1">Window (minutes)</label>
                      <input type="number" min={1} value={advancedSettings.apiRateLimit.windowMinutes} onChange={(e) => setAdvancedSettings((s) => s ? { ...s, apiRateLimit: { ...s.apiRateLimit, windowMinutes: parseInt(e.target.value, 10) || 1 } } : null)} className="rounded bg-gray-700 px-3 py-2 text-white w-24" />
                    </div>
                  </div>
                </div>
              )}
            </div>
            <button
              type="button"
              disabled={!advancedSettings || advancedLoading}
              onClick={async () => {
                if (!advancedSettings) return
                try {
                  await apiClient.updateAdvancedSettings({
                    apiRateLimitEnabled: advancedSettings.apiRateLimit.enabled,
                    apiRateLimitPermitLimit: advancedSettings.apiRateLimit.permitLimit,
                    apiRateLimitWindowMinutes: advancedSettings.apiRateLimit.windowMinutes,
                  })
                  setTindarrMessage({ type: 'success', text: 'Advanced settings saved' })
                } catch (err) {
                  setTindarrMessage({ type: 'error', text: err instanceof Error ? err.message : 'Save failed' })
                }
              }}
              className="rounded-full bg-pink-600 px-5 py-2.5 text-sm font-semibold text-white hover:bg-pink-700 disabled:opacity-50"
            >
              Save
            </button>
          </div>
        )}

        {/* Tindarr: Console (mirror stdout/stderr) */}
        {selectedService === 'tindarr' && tindarrTab === 'console' && (
          <div className="rounded-lg bg-gray-800 p-6 text-left">
            <div className="mb-4 flex flex-wrap items-center gap-4">
              <h3 className="text-lg font-bold text-white">Console output</h3>
              <label className="flex items-center gap-2 text-sm text-gray-300">
                <span>Lines:</span>
                <input
                  type="number"
                  min={1}
                  max={2000}
                  value={consoleMaxLines}
                  onChange={(e) => setConsoleMaxLines(Math.min(2000, Math.max(1, parseInt(e.target.value, 10) || 500)))}
                  className="w-20 rounded bg-gray-700 px-2 py-1 text-white focus:outline-none focus:ring-2 focus:ring-pink-500"
                />
              </label>
              <button
                type="button"
                onClick={() => setConsoleRefreshPaused((p) => !p)}
                className="rounded bg-gray-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-gray-500"
              >
                {consoleRefreshPaused ? 'Resume' : 'Pause'}
              </button>
            </div>
            <div className="mb-4">
              <label className="block text-sm text-gray-400 mb-1">Highlight lines containing (comma-separated)</label>
              <input
                type="text"
                value={consoleHighlightText}
                onChange={(e) => setConsoleHighlightText(e.target.value)}
                placeholder="e.g. trace, mykey, /api/foo"
                className="w-full max-w-md rounded bg-gray-700 px-3 py-2 text-sm text-white placeholder-gray-500 focus:outline-none focus:ring-2 focus:ring-pink-500"
              />
            </div>
            <p className="text-sm text-gray-400 mb-4">
              {consoleRefreshPaused
                ? 'Paused. Click Resume to refresh again.'
                : `Last ${Math.min(2000, Math.max(1, consoleMaxLines))} lines from the API process (refreshes every 2s).`}
            </p>
            <pre
              ref={consolePreRef}
              className="w-full max-h-[70vh] overflow-auto rounded bg-gray-900 p-4 text-left text-sm font-mono whitespace-pre-wrap break-words"
            >
              {consoleLines.length === 0
                ? <span className="text-gray-400">(no output yet)</span>
                : consoleLines.map((line, i) => {
                    const isError = /\berror\b|exception|\bfail(ed)?\b|stack\s+trace|\s4\d{2}\s|\s5\d{2}\s|4\d{2}\]|5\d{2}\]/i.test(line)
                    const isSuccess = /\s200\s|\s201\s|\s204\s|200\]|201\]|204\]|\s200$|\s201$|\s204$/i.test(line)
                    const highlightTerms = consoleHighlightText.split(',').map((t) => t.trim()).filter(Boolean)
                    const isHighlight = highlightTerms.length > 0 && highlightTerms.some((term) => line.toLowerCase().includes(term.toLowerCase()))
                    const className = isError ? 'text-red-400' : isSuccess ? 'text-green-400' : isHighlight ? 'text-amber-300' : 'text-gray-300'
                    return <span key={i} className={className} style={{ display: 'block' }}>{line}{i < consoleLines.length - 1 ? '\n' : ''}</span>
                  })}
            </pre>
          </div>
        )}

        {/* Radarr pill content */}
        {selectedService === 'radarr' && (
          <div className="space-y-6">
            {/* Config table */}
            <div className="rounded-lg bg-gray-800 p-6">
              <h3 className="text-xl font-bold text-white mb-4">Config</h3>
              <div className="space-y-4">
                <div>
                  <label className="block text-sm font-semibold text-white mb-2">Server ID</label>
                  <input
                    type="text"
                    placeholder="e.g. default"
                    value={radarrServerId}
                    onChange={(e) => setRadarrServerId(e.target.value.trim() || 'default')}
                    className="w-full rounded bg-gray-700 px-3 py-2 text-white placeholder-gray-500 focus:outline-none focus:ring-2 focus:ring-pink-500"
                  />
                  <p className="mt-1.5 text-xs text-amber-400/90">
                    Keep this unique. Multiple Radarr instances may be supported in the future.
                  </p>
                </div>
                <div>
                  <label className="block text-sm font-semibold text-white mb-2">URL</label>
                  <input
                    type="text"
                    placeholder="http://localhost:7878"
                    value={radarrSettings.apiUrl}
                    onChange={(e) => setRadarrSettings({ ...radarrSettings, apiUrl: e.target.value })}
                    className="w-full rounded bg-gray-700 px-3 py-2 text-white placeholder-gray-500 focus:outline-none focus:ring-2 focus:ring-pink-500"
                  />
                </div>
                <div>
                  <label className="block text-sm font-semibold text-white mb-2">API Key</label>
                  <input
                    type="password"
                    placeholder="Your Radarr API Key"
                    value={radarrSettings.apiKey}
                    onChange={(e) => setRadarrSettings({ ...radarrSettings, apiKey: e.target.value })}
                    className="w-full rounded bg-gray-700 px-3 py-2 text-white placeholder-gray-500 focus:outline-none focus:ring-2 focus:ring-pink-500"
                  />
                </div>
                {hasValidatedConnection && qualityProfiles.length > 0 && (
                  <div>
                    <label className="block text-sm font-semibold text-white mb-2">Quality Profile</label>
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
                  <div>
                    <label className="block text-sm font-semibold text-white mb-2">Root Folder</label>
                    <select
                      value={radarrSettings.defaultRootFolderId}
                      onChange={(e) => {
                        const id = parseInt(e.target.value)
                        const folder = rootFolders.find(f => f.id === id)
                        setRadarrSettings({
                          ...radarrSettings,
                          defaultRootFolderId: id,
                          rootFolderPath: folder?.path ?? ''
                        })
                      }}
                      className="w-full rounded bg-gray-700 px-3 py-2 text-white focus:outline-none focus:ring-2 focus:ring-pink-500"
                    >
                      <option value={0}>Select Root Folder</option>
                      {rootFolders.map(folder => (
                        <option key={folder.id} value={folder.id}>{folder.path}</option>
                      ))}
                    </select>
                  </div>
                )}
                <div>
                  <label className="block text-sm font-semibold text-white mb-2">Label</label>
                  <input
                    type="text"
                    placeholder="e.g. tindarr (for identifying Tindarr downloads in Radarr)"
                    value={radarrSettings.tagLabel}
                    onChange={(e) => setRadarrSettings({ ...radarrSettings, tagLabel: e.target.value })}
                    className="w-full rounded bg-gray-700 px-3 py-2 text-white placeholder-gray-500 focus:outline-none focus:ring-2 focus:ring-pink-500"
                  />
                </div>
                <div className="flex items-center justify-between">
                  <div>
                    <p className="text-white font-semibold">Auto-add</p>
                    <p className="text-sm text-gray-400 mt-0.5">Automatically add consensus movies to Radarr</p>
                  </div>
                  <button
                    type="button"
                    onClick={() => {
                      const next = !radarrSettings.autoAddMovies
                      setRadarrSettings({
                        ...radarrSettings,
                        autoAddMovies: next,
                        enabled: radarrSettings.enabled || next
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
                <div>
                  <label className="block text-sm font-semibold text-white mb-2">Auto-add interval (minutes)</label>
                  <input
                    type="number"
                    min={1}
                    step={1}
                    value={radarrSettings.autoAddIntervalSeconds ? Math.round(radarrSettings.autoAddIntervalSeconds / 60) : 5}
                    onChange={(e) =>
                      setRadarrSettings({
                        ...radarrSettings,
                        autoAddIntervalSeconds: Math.max(60, (Number(e.target.value) || 5) * 60)
                      })
                    }
                    className="w-full rounded bg-gray-700 px-3 py-2 text-white placeholder-gray-500 focus:outline-none focus:ring-2 focus:ring-pink-500"
                  />
                </div>
                <div className="flex flex-wrap gap-3 pt-2">
                  <button
                    onClick={handleSaveSettings}
                    disabled={savingSettings || !hasValidatedConnection}
                    className="rounded bg-pink-600 px-4 py-2 font-semibold text-white hover:bg-pink-700 disabled:bg-gray-600 transition-colors"
                  >
                    {savingSettings ? 'Saving...' : 'Save Settings'}
                  </button>
                  <button
                    type="button"
                    onClick={handleTestConnection}
                    disabled={testingConnection || !radarrSettings.apiUrl || !radarrSettings.apiKey}
                    className="rounded bg-blue-600 px-4 py-2 font-semibold text-white hover:bg-blue-700 disabled:bg-gray-600 transition-colors"
                  >
                    {testingConnection ? 'Testing...' : 'Test Connection'}
                  </button>
                  <button
                    type="button"
                    onClick={handleSyncLibrary}
                    disabled={syncLibraryLoading || !hasValidatedConnection}
                    className="rounded bg-emerald-600 px-4 py-2 font-semibold text-white hover:bg-emerald-700 disabled:bg-gray-600 transition-colors"
                  >
                    {syncLibraryLoading ? 'Syncing...' : 'Sync Now'}
                  </button>
                </div>
                <button
                  type="button"
                  onClick={handleAutoAddMovies}
                  disabled={addingMovieId === 'auto' || !hasValidatedConnection}
                  className="w-full rounded bg-blue-600/80 px-4 py-2 font-semibold text-white hover:bg-blue-600 disabled:bg-gray-600 transition-colors"
                >
                  {addingMovieId === 'auto' ? 'Adding...' : 'Add accepted movies now'}
                </button>
              </div>
              {settingsMessage && (
                <div
                  className={`mt-4 rounded px-4 py-3 ${
                    settingsMessage.type === 'success'
                      ? 'bg-green-500/10 border border-green-500/30 text-green-400'
                      : 'bg-red-500/10 border border-red-500/30 text-red-400'
                  }`}
                >
                  {settingsMessage.text}
                </div>
              )}
            </div>

            {/* Matching settings */}
            <div className="rounded-lg bg-gray-800 p-6">
              <h3 className="text-xl font-bold text-white mb-4">Matching settings</h3>
              <div className="space-y-4">
                <div>
                  <label className="block text-sm font-semibold text-white mb-2">Min Users</label>
                  <input
                    type="number"
                    min={1}
                    max={50}
                    placeholder="e.g. 2"
                    value={matchSettings.minUsers ?? ''}
                    onChange={(e) => {
                      const v = e.target.value === '' ? null : parseInt(e.target.value, 10)
                      setMatchSettings(prev => ({ ...prev, minUsers: v ?? null }))
                    }}
                    className="w-full rounded bg-gray-700 px-3 py-2 text-white placeholder-gray-500 focus:outline-none focus:ring-2 focus:ring-pink-500"
                  />
                  <p className="mt-1 text-xs text-gray-400">Between 1 and 50. Minimum number of users who must like a movie for it to count as a match.</p>
                </div>
                <div>
                  <label className="block text-sm font-semibold text-white mb-2">Min % users</label>
                  <input
                    type="number"
                    min={1}
                    max={100}
                    placeholder="e.g. 50"
                    value={matchSettings.minUserPercent ?? ''}
                    onChange={(e) => {
                      const v = e.target.value === '' ? null : parseInt(e.target.value, 10)
                      setMatchSettings(prev => ({ ...prev, minUserPercent: v ?? null }))
                    }}
                    className="w-full rounded bg-gray-700 px-3 py-2 text-white placeholder-gray-500 focus:outline-none focus:ring-2 focus:ring-pink-500"
                  />
                  <p className="mt-1 text-xs text-gray-400">Between 1 and 100. Minimum percentage of room users who must like a movie.</p>
                </div>
                <button
                  type="button"
                  onClick={handleSaveMatchSettings}
                  disabled={matchSaving}
                  className="rounded bg-pink-600 px-4 py-2 font-semibold text-white hover:bg-pink-700 disabled:bg-gray-600 transition-colors"
                >
                  {matchSaving ? 'Saving...' : 'Save Settings'}
                </button>
              </div>
            </div>
          </div>
        )}

        {/* TMDB pill content */}
        {selectedService === 'tmdb' && (
          <div className="space-y-6">
            {tmdbMessage && (
              <div className={`rounded-lg px-4 py-3 ${tmdbMessage.type === 'success' ? 'bg-green-500/10 text-green-400' : 'bg-red-500/10 text-red-400'}`}>
                {tmdbMessage.text}
              </div>
            )}
            <TmdbAttribution compact />

            {/* API / Read Access Token */}
            <div className="rounded-lg bg-gray-800 p-6">
              <h3 className="text-xl font-bold text-white mb-4">API / Read Access Token</h3>
              <p className="text-sm text-gray-400 mb-4">
                {advancedSettings?.tmdb.hasTmdbApiKey || advancedSettings?.tmdb.hasTmdbReadAccessToken
                  ? 'Credentials are set. Enter new values to update (leave blank to keep current).'
                  : 'Set either API Key or Read Access Token for TMDB.'}
              </p>
              <div className="space-y-4">
                <div>
                  <label className="block text-sm font-semibold text-white mb-2">API Key (optional)</label>
                  <input
                    type="password"
                    placeholder="Leave blank to keep current"
                    value={tmdbCredentialApiKey}
                    onChange={(e) => setTmdbCredentialApiKey(e.target.value)}
                    className="w-full rounded bg-gray-700 px-3 py-2 text-white placeholder-gray-500 focus:outline-none focus:ring-2 focus:ring-pink-500"
                  />
                </div>
                <div>
                  <label className="block text-sm font-semibold text-white mb-2">Read Access Token (optional)</label>
                  <input
                    type="password"
                    placeholder="Leave blank to keep current"
                    value={tmdbCredentialReadToken}
                    onChange={(e) => setTmdbCredentialReadToken(e.target.value)}
                    className="w-full rounded bg-gray-700 px-3 py-2 text-white placeholder-gray-500 focus:outline-none focus:ring-2 focus:ring-pink-500"
                  />
                </div>
                <button
                  type="button"
                  disabled={tmdbCredentialSaving}
                  onClick={handleSaveTmdbCredentials}
                  className="rounded bg-pink-600 px-4 py-2 font-semibold text-white hover:bg-pink-700 disabled:bg-gray-600"
                >
                  {tmdbCredentialSaving ? 'Saving...' : 'Save'}
                </button>
              </div>
            </div>

            {/* Cache Settings */}
            <div className="rounded-lg bg-gray-800 p-6">
              <h3 className="text-xl font-bold text-white mb-4">Cache Settings</h3>
              {tmdbCacheLoading ? (
                <p className="text-gray-400">Loading...</p>
              ) : tmdbCacheSettings && (
                <div className="space-y-4">
                  <div>
                    <label className="block text-sm font-semibold text-white mb-2">Max Movies</label>
                    <input
                      type="number"
                      min={500}
                      value={tmdbCacheSettings.maxMovies}
                      onChange={(e) => setTmdbCacheSettings((s) => s ? { ...s, maxMovies: Math.max(500, parseInt(e.target.value, 10) || 500) } : null)}
                      className="w-full rounded bg-gray-700 px-3 py-2 text-white focus:outline-none focus:ring-2 focus:ring-pink-500"
                    />
                    <p className="mt-1 text-xs text-gray-400">Current in DB: {tmdbCacheSettings.currentMovies}</p>
                  </div>
                  <div>
                    <label className="block text-sm font-semibold text-white mb-2">Posters / backdrops</label>
                    <select
                      value={tmdbCacheSettings.posterMode}
                      onChange={(e) => setTmdbCacheSettings((s) => s ? { ...s, posterMode: e.target.value } : null)}
                      className="w-full rounded bg-gray-700 px-3 py-2 text-white focus:outline-none focus:ring-2 focus:ring-pink-500"
                    >
                      <option value="LocalProxy">Keep local (cache posters/backdrops)</option>
                      <option value="Tmdb">Always fetch from TMDB</option>
                    </select>
                  </div>
                  <div>
                    <label className="block text-sm font-semibold text-white mb-2">Max Image Cache Size (MB)</label>
                    <input
                      type="number"
                      min={0}
                      value={tmdbCacheSettings.imageCacheMaxMb}
                      onChange={(e) => setTmdbCacheSettings((s) => s ? { ...s, imageCacheMaxMb: Math.max(0, parseInt(e.target.value, 10) || 0) } : null)}
                      className="w-full rounded bg-gray-700 px-3 py-2 text-white focus:outline-none focus:ring-2 focus:ring-pink-500"
                    />
                    <p className="mt-1 text-xs text-gray-400">
                      Current size: {Math.round(tmdbCacheSettings.imageCacheBytes / (1024 * 1024))} MB
                    </p>
                  </div>
                  <button
                    type="button"
                    disabled={tmdbCacheSaving}
                    onClick={handleSaveTmdbCacheSettings}
                    className="rounded bg-pink-600 px-4 py-2 font-semibold text-white hover:bg-pink-700 disabled:bg-gray-600"
                  >
                    {tmdbCacheSaving ? 'Saving...' : 'Save Settings'}
                  </button>
                </div>
              )}
            </div>

            {/* Prewarm now */}
            <div className="rounded-lg bg-gray-800 p-6">
              <h3 className="text-xl font-bold text-white mb-4">Prewarm now</h3>
              {tmdbCacheLoading || !tmdbCacheSettings ? (
                <p className="text-gray-400">Load cache settings first.</p>
              ) : (
                <>
                <div className="space-y-4">
                  <p className="text-sm text-gray-400">Restrict prewarm to language/region (optional):</p>
                  <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                    <div>
                      <label className="block text-sm font-semibold text-white mb-2">Language</label>
                      <select
                        value={tmdbCacheSettings.prewarmOriginalLanguage ?? ''}
                        onChange={(e) => setTmdbCacheSettings((s) => s ? { ...s, prewarmOriginalLanguage: e.target.value || null } : null)}
                        className="w-full rounded bg-gray-700 px-3 py-2 text-white focus:outline-none focus:ring-2 focus:ring-pink-500"
                      >
                        <option value="">— Any</option>
                        {TMDB_LANGUAGES_TOP.map(({ code, name }) => (
                          <option key={code} value={code}>{name}</option>
                        ))}
                        <option disabled>—</option>
                        {TMDB_LANGUAGES_OTHER.map(({ code, name }) => (
                          <option key={code} value={code}>{name}</option>
                        ))}
                      </select>
                    </div>
                    <div>
                      <label className="block text-sm font-semibold text-white mb-2">Region</label>
                      <select
                        value={tmdbCacheSettings.prewarmRegion ?? ''}
                        onChange={(e) => setTmdbCacheSettings((s) => s ? { ...s, prewarmRegion: e.target.value || null } : null)}
                        className="w-full rounded bg-gray-700 px-3 py-2 text-white focus:outline-none focus:ring-2 focus:ring-pink-500"
                      >
                        <option value="">— Any</option>
                        {TMDB_REGIONS_TOP.map(({ code, name }) => (
                          <option key={code} value={code}>{name}</option>
                        ))}
                        <option disabled>—</option>
                        {TMDB_REGIONS_OTHER.map(({ code, name }) => (
                          <option key={code} value={code}>{name}</option>
                        ))}
                      </select>
                    </div>
                  </div>
                  <p className="text-xs text-gray-400">Save cache settings above to persist language/region for prewarm.</p>
                  <div className="flex items-center gap-2">
                    <input
                      type="checkbox"
                      id="tmdb-bypass-limit"
                      checked={tmdbBuildBypassLimit}
                      onChange={(e) => setTmdbBuildBypassLimit(e.target.checked)}
                      className="rounded"
                    />
                    <label htmlFor="tmdb-bypass-limit" className="text-white font-medium">Bypass Tindarr API limit?</label>
                  </div>
                  <div>
                    <label className="block text-sm font-semibold text-white mb-2">Count of movies to cache (per user discover limit)</label>
                    <input
                      type="number"
                      min={1}
                      max={1000}
                      value={tmdbBuildDiscoverLimit}
                      onChange={(e) => setTmdbBuildDiscoverLimit(Math.min(1000, Math.max(1, parseInt(e.target.value, 10) || 50)))}
                      className="w-full rounded bg-gray-700 px-3 py-2 text-white focus:outline-none focus:ring-2 focus:ring-pink-500"
                    />
                  </div>
                  <div className="flex flex-wrap items-center gap-3">
                    <button
                      type="button"
                      disabled={tmdbBuildStatus?.state === 'running'}
                      onClick={() => handleStartTmdbBuild(tmdbBuildBypassLimit, tmdbBuildDiscoverLimit)}
                      className="rounded bg-emerald-600 px-4 py-2 font-semibold text-white hover:bg-emerald-700 disabled:bg-gray-600"
                    >
                      Build Now
                    </button>
                    {tmdbBuildStatus?.state === 'running' && (
                      <button
                        type="button"
                        onClick={handleCancelTmdbBuild}
                        className="rounded bg-red-600 px-4 py-2 font-semibold text-white hover:bg-red-700"
                      >
                        Stop
                      </button>
                    )}
                  </div>
                  {tmdbBuildStatus && (() => {
                    const isRunning = tmdbBuildStatus.state === 'running'
                    const limit = Math.max(tmdbBuildStatus.moviesDiscovered, 1)
                    const imagesCached = tmdbCacheSettings?.posterMode === 'LocalProxy' && (tmdbCacheSettings?.imageCacheMaxMb ?? 0) > 0
                    const progressPct = isRunning && limit > 0
                      ? imagesCached
                        ? Math.min(1, ((tmdbBuildStatus.detailsFetched + tmdbBuildStatus.imagesFetched) / 2) / limit)
                        : Math.min(1, tmdbBuildStatus.detailsFetched / limit)
                      : 0
                    const showProgressBar = isRunning && tmdbBuildStatus.moviesDiscovered > 0
                    return (
                      <div className="mt-4 rounded-lg border border-gray-700 overflow-hidden relative">
                        {showProgressBar && (
                          <div className="absolute inset-0 bg-gray-800" aria-hidden>
                            <div
                              className="h-full bg-emerald-600/40 transition-all duration-500 ease-out"
                              style={{ width: `${progressPct * 100}%` }}
                            />
                          </div>
                        )}
                        <div className="relative z-10 p-4 bg-gray-900/50">
                          <p className="text-white font-medium mb-1">Status: {tmdbBuildStatus.state}</p>
                          {tmdbBuildStatus.lastMessage && <p className="text-sm text-gray-400">{tmdbBuildStatus.lastMessage}</p>}
                          {tmdbBuildStatus.lastError && <p className="text-sm text-red-400">{tmdbBuildStatus.lastError}</p>}
                          <p className="text-sm text-gray-400 mt-1">
                            Users: {tmdbBuildStatus.usersProcessed} / {tmdbBuildStatus.usersTotal} · Discovered: {tmdbBuildStatus.moviesDiscovered} · Details: {tmdbBuildStatus.detailsFetched} · Images: {tmdbBuildStatus.imagesFetched}
                          </p>
                          {showProgressBar && (
                            <p className="text-xs text-gray-500 mt-2">
                              Backfill: {Math.round(progressPct * 100)}%
                            </p>
                          )}
                        </div>
                      </div>
                    )
                  })()}
                </div>

                <div className="mt-6 rounded-lg border border-gray-700 p-4">
                  <h3 className="text-sm font-medium text-white mb-3">Backup and restore</h3>
                  <p className="text-xs text-gray-400 mb-3">
                    Download a ZIP of tmdbmetadata.db and cached images (if configured), or restore from a backup ZIP or SQLite file (restore runs while the app is running).
                  </p>
                  <div className="flex flex-wrap items-end gap-3">
                    <button
                      type="button"
                      onClick={handleTmdbDownloadBackup}
                      disabled={tmdbImportLoading}
                      className="rounded bg-emerald-600 px-3 py-2 text-sm text-white hover:bg-emerald-500 disabled:opacity-50"
                    >
                      Download backup (ZIP)
                    </button>
                    <input
                      ref={tmdbImportFileInputRef}
                      type="file"
                      accept=".zip,.sqlite,.sqlite3,.db"
                      className="hidden"
                      onChange={(e) => setTmdbImportSelectedFile(e.target.files?.[0] ?? null)}
                    />
                    <button
                      type="button"
                      onClick={() => tmdbImportFileInputRef.current?.click()}
                      disabled={tmdbImportLoading}
                      className="rounded bg-gray-600 px-3 py-2 text-sm text-white hover:bg-gray-500 disabled:opacity-50"
                    >
                      Choose file
                    </button>
                    <button
                      type="button"
                      onClick={handleTmdbRestore}
                      disabled={tmdbImportLoading || !tmdbImportSelectedFile}
                      className="rounded bg-gray-600 px-3 py-2 text-sm text-white hover:bg-gray-500 disabled:opacity-50"
                    >
                      Restore from file
                    </button>
                    {tmdbImportSelectedFile && (
                      <span className="text-xs text-gray-400">{tmdbImportSelectedFile.name}</span>
                    )}
                  </div>
                  {tmdbImportLoading && <p className="text-xs text-gray-500 mt-2">Please wait…</p>}
                  {tmdbRestoreResult && !tmdbImportLoading && (
                    <div className="mt-3 rounded border border-gray-600 bg-gray-800/60 p-3 text-sm">
                      <p className="font-medium text-white mb-2">Restore result</p>
                      <ul className="space-y-1 text-gray-300">
                        <li><span className="text-green-400">New:</span> {tmdbRestoreResult.inserted}</li>
                        <li><span className="text-amber-400">Already existed (updated):</span> {tmdbRestoreResult.updated}</li>
                        {tmdbRestoreResult.skipped > 0 && (
                          <li><span className="text-gray-400">Skipped:</span> {tmdbRestoreResult.skipped}</li>
                        )}
                        {tmdbRestoreResult.imagesRestored > 0 && (
                          <li><span className="text-blue-400">Images restored:</span> {tmdbRestoreResult.imagesRestored}</li>
                        )}
                      </ul>
                      {tmdbRestoreResult.notImportedReasons?.length > 0 && (
                        <div className="mt-2">
                          <p className="text-red-400 font-medium">Not imported (reasons):</p>
                          <ul className="list-disc list-inside text-red-300/90 text-xs mt-1 space-y-0.5">
                            {tmdbRestoreResult.notImportedReasons.map((r, i) => (
                              <li key={i}>{r}</li>
                            ))}
                          </ul>
                        </div>
                      )}
                    </div>
                  )}
                </div>
                </>
              )}
            </div>
          </div>
        )}

        {/* Backup & Restore pill content */}
        {selectedService === 'backup' && (
          <div className="space-y-6">
            {backupMessage && (
              <div className={`rounded-lg px-4 py-3 ${backupMessage.type === 'success' ? 'bg-green-500/10 text-green-400' : 'bg-red-500/10 text-red-400'}`}>
                {backupMessage.text}
              </div>
            )}
            <p className="text-gray-400 text-sm">
              Download backups or restore from a file. Master backup includes all databases and tmdb-images.
            </p>
            <div className="rounded-lg border border-amber-500/40 bg-amber-500/10 px-4 py-3 text-amber-200 text-sm">
              <strong>Master backup and main database (tindarr.db) are manual restore only:</strong> stop the application (and Tindarr.Workers service if running), replace the files in the data directory, then restart. Media server caches (Plex, Jellyfin, Emby) and TMDB can be restored from file using the buttons below.
            </div>

            {/* Master */}
            <div className="rounded-lg bg-gray-800 p-6">
              <h3 className="text-lg font-semibold text-white mb-3">Master backup</h3>
              <p className="text-sm text-gray-400 mb-3">All DBs (tindarr, Plex, Jellyfin, Emby, TMDB) and tmdb-images in one ZIP. To restore: stop the app/service, extract the ZIP and copy files to the data directory, then restart.</p>
              <div className="flex flex-wrap items-center gap-4">
                <button
                  type="button"
                  disabled={backupLoading !== null}
                  onClick={() => runBackupAction('master-dl', () => apiClient.getMasterBackupDownload(), 'tindarr-master-backup.zip')}
                  className="rounded bg-pink-600 px-4 py-2 text-white font-medium hover:bg-pink-500 disabled:opacity-50"
                >
                  {backupLoading === 'master-dl' ? 'Downloading…' : 'Download master (ZIP)'}
                </button>
              </div>
            </div>

            {/* Main (tindarr.db) */}
            <div className="rounded-lg bg-gray-800 p-6">
              <h3 className="text-lg font-semibold text-white mb-3">Main database (tindarr.db)</h3>
              <p className="text-sm text-gray-400 mb-3">Manual restore only: stop the app/service, replace tindarr.db in the data directory, then restart.</p>
              <div className="flex flex-wrap items-center gap-4">
                <button
                  type="button"
                  disabled={backupLoading !== null}
                  onClick={() => runBackupAction('main-dl', () => apiClient.getMainBackupDownload(), 'tindarr.db')}
                  className="rounded bg-pink-600 px-4 py-2 text-white font-medium hover:bg-pink-500 disabled:opacity-50"
                >
                  {backupLoading === 'main-dl' ? 'Downloading…' : 'Download'}
                </button>
              </div>
            </div>

            {/* Plex */}
            <div className="rounded-lg bg-gray-800 p-6">
              <h3 className="text-lg font-semibold text-white mb-3">Plex cache (plexcache.db)</h3>
              <p className="text-sm text-gray-400 mb-3">Download or restore from file (restore runs while the app is running).</p>
              <div className="flex flex-wrap items-center gap-4">
                <button
                  type="button"
                  disabled={backupLoading !== null}
                  onClick={() => runBackupAction('plex-dl', () => apiClient.getPlexBackupDownload(), 'plexcache.db')}
                  className="rounded bg-pink-600 px-4 py-2 text-white font-medium hover:bg-pink-500 disabled:opacity-50"
                >
                  {backupLoading === 'plex-dl' ? 'Downloading…' : 'Download'}
                </button>
                <input type="file" accept=".db,.sqlite,.sqlite3" ref={plexRestoreInputRef} className="hidden" onChange={(e) => setPlexRestoreFile(e.target.files?.[0] ?? null)} />
                <button type="button" onClick={() => plexRestoreInputRef.current?.click()} disabled={backupLoading !== null} className="rounded bg-gray-600 px-4 py-2 text-white font-medium hover:bg-gray-500 disabled:opacity-50">Choose file…</button>
                <button
                  type="button"
                  disabled={backupLoading !== null || !plexRestoreFile}
                  onClick={() => runBackupRestore('plex-restore', plexRestoreFile, (f) => apiClient.postPlexRestore(f), () => setPlexRestoreFile(null), plexRestoreInputRef)}
                  className="rounded bg-amber-600 px-4 py-2 text-white font-medium hover:bg-amber-500 disabled:opacity-50"
                >
                  {backupLoading === 'plex-restore' ? 'Restoring…' : 'Restore'}
                </button>
                {plexRestoreFile && <span className="text-sm text-gray-400">{plexRestoreFile.name}</span>}
              </div>
            </div>

            {/* Jellyfin */}
            <div className="rounded-lg bg-gray-800 p-6">
              <h3 className="text-lg font-semibold text-white mb-3">Jellyfin cache (jellyfincache.db)</h3>
              <p className="text-sm text-gray-400 mb-3">Download or restore from file (restore runs while the app is running).</p>
              <div className="flex flex-wrap items-center gap-4">
                <button
                  type="button"
                  disabled={backupLoading !== null}
                  onClick={() => runBackupAction('jellyfin-dl', () => apiClient.getJellyfinBackupDownload(), 'jellyfincache.db')}
                  className="rounded bg-pink-600 px-4 py-2 text-white font-medium hover:bg-pink-500 disabled:opacity-50"
                >
                  {backupLoading === 'jellyfin-dl' ? 'Downloading…' : 'Download'}
                </button>
                <input type="file" accept=".db,.sqlite,.sqlite3" ref={jellyfinRestoreInputRef} className="hidden" onChange={(e) => setJellyfinRestoreFile(e.target.files?.[0] ?? null)} />
                <button type="button" onClick={() => jellyfinRestoreInputRef.current?.click()} disabled={backupLoading !== null} className="rounded bg-gray-600 px-4 py-2 text-white font-medium hover:bg-gray-500 disabled:opacity-50">Choose file…</button>
                <button
                  type="button"
                  disabled={backupLoading !== null || !jellyfinRestoreFile}
                  onClick={() => runBackupRestore('jellyfin-restore', jellyfinRestoreFile, (f) => apiClient.postJellyfinRestore(f), () => setJellyfinRestoreFile(null), jellyfinRestoreInputRef)}
                  className="rounded bg-amber-600 px-4 py-2 text-white font-medium hover:bg-amber-500 disabled:opacity-50"
                >
                  {backupLoading === 'jellyfin-restore' ? 'Restoring…' : 'Restore'}
                </button>
                {jellyfinRestoreFile && <span className="text-sm text-gray-400">{jellyfinRestoreFile.name}</span>}
              </div>
            </div>

            {/* Emby */}
            <div className="rounded-lg bg-gray-800 p-6">
              <h3 className="text-lg font-semibold text-white mb-3">Emby cache (embycache.db)</h3>
              <p className="text-sm text-gray-400 mb-3">Download or restore from file (restore runs while the app is running).</p>
              <div className="flex flex-wrap items-center gap-4">
                <button
                  type="button"
                  disabled={backupLoading !== null}
                  onClick={() => runBackupAction('emby-dl', () => apiClient.getEmbyBackupDownload(), 'embycache.db')}
                  className="rounded bg-pink-600 px-4 py-2 text-white font-medium hover:bg-pink-500 disabled:opacity-50"
                >
                  {backupLoading === 'emby-dl' ? 'Downloading…' : 'Download'}
                </button>
                <input type="file" accept=".db,.sqlite,.sqlite3" ref={embyRestoreInputRef} className="hidden" onChange={(e) => setEmbyRestoreFile(e.target.files?.[0] ?? null)} />
                <button type="button" onClick={() => embyRestoreInputRef.current?.click()} disabled={backupLoading !== null} className="rounded bg-gray-600 px-4 py-2 text-white font-medium hover:bg-gray-500 disabled:opacity-50">Choose file…</button>
                <button
                  type="button"
                  disabled={backupLoading !== null || !embyRestoreFile}
                  onClick={() => runBackupRestore('emby-restore', embyRestoreFile, (f) => apiClient.postEmbyRestore(f), () => setEmbyRestoreFile(null), embyRestoreInputRef)}
                  className="rounded bg-amber-600 px-4 py-2 text-white font-medium hover:bg-amber-500 disabled:opacity-50"
                >
                  {backupLoading === 'emby-restore' ? 'Restoring…' : 'Restore'}
                </button>
                {embyRestoreFile && <span className="text-sm text-gray-400">{embyRestoreFile.name}</span>}
              </div>
            </div>

            {/* TMDB — link to TMDB tab */}
            <div className="rounded-lg bg-gray-800 p-6">
              <h3 className="text-lg font-semibold text-white mb-3">TMDB (tmdbmetadata.db + tmdb-images)</h3>
              <p className="text-sm text-gray-400 mb-3">Backup and restore TMDB metadata and image cache from the <button type="button" onClick={() => setSelectedService('tmdb')} className="text-pink-400 hover:underline">TMDB</button> tab.</p>
            </div>
          </div>
        )}

        {/* Media Servers: Plex */}
        {selectedService === 'plex' && (
          <div className="rounded-lg bg-gray-800 p-6">
            <h2 className="text-lg font-semibold text-white mb-4">Media Servers — Plex</h2>
            {plexMessage && (
              <p className={`mb-4 text-sm ${plexMessage.type === 'success' ? 'text-green-400' : 'text-red-400'}`}>
                {plexMessage.text}
              </p>
            )}
            <div className="space-y-6">
              <section>
                <h3 className="text-sm font-medium text-gray-300 mb-2">Plex Authorization</h3>
                {plexAuthStatus && !plexAuthStatus.hasAuthToken && !plexPin && (
                  <p className="text-gray-400 text-sm mb-2">Authorize Tindarr with your Plex account to discover servers.</p>
                )}
                {plexPin ? (
                  <div className="rounded bg-gray-700 p-4 max-w-md">
                    <p className="text-white text-sm mb-2">Enter this code at the URL below, then wait for authorization.</p>
                    <p className="text-2xl font-mono font-bold text-amber-400 mb-2">{plexPin.code}</p>
                    <a href={plexPin.authUrl} target="_blank" rel="noreferrer" className="text-blue-400 hover:underline text-sm break-all">
                      {plexPin.authUrl}
                    </a>
                  </div>
                ) : (
                  <button
                    type="button"
                    onClick={handlePlexCreatePin}
                    disabled={plexServersLoading}
                    className="rounded bg-amber-600 px-4 py-2 text-white text-sm font-medium hover:bg-amber-500 disabled:opacity-50"
                  >
                    {plexAuthStatus?.hasAuthToken ? 'Re-authorize Plex' : 'Authorize with Plex'}
                  </button>
                )}
              </section>
              <section>
                <div className="flex items-center justify-between mb-2">
                  <h3 className="text-sm font-medium text-gray-300">Current servers</h3>
                  <div className="flex gap-2">
                    <button
                      type="button"
                      onClick={handlePlexTestConnection}
                      disabled={plexTestServerId !== null || plexServersLoading}
                      className="rounded bg-gray-600 px-3 py-1.5 text-white text-sm hover:bg-gray-500 disabled:opacity-50"
                    >
                      Test connection
                    </button>
                    <button
                      type="button"
                      onClick={handlePlexSyncServers}
                      disabled={plexServersLoading}
                      className="rounded bg-gray-600 px-3 py-1.5 text-white text-sm hover:bg-gray-500 disabled:opacity-50"
                    >
                      Sync servers
                    </button>
                  </div>
                </div>
                {plexServersLoading && plexServers.length === 0 ? (
                  <p className="text-gray-400 text-sm">Loading…</p>
                ) : plexServers.length === 0 ? (
                  <p className="text-gray-400 text-sm">No Plex servers. Authorize above and click Sync servers.</p>
                ) : (
                  <div className="overflow-x-auto rounded border border-gray-600">
                    <table className="min-w-full text-sm">
                      <thead>
                        <tr className="bg-gray-700/50 text-left">
                          <th className="px-4 py-2 text-gray-300 font-medium">Friendly name</th>
                          <th className="px-4 py-2 text-gray-300 font-medium">Version</th>
                          <th className="px-4 py-2 text-gray-300 font-medium">Movies</th>
                          <th className="px-4 py-2 text-gray-300 font-medium">Last sync</th>
                          <th className="px-4 py-2 text-gray-300 font-medium">Actions</th>
                        </tr>
                      </thead>
                      <tbody>
                        {plexServers.map((s) => {
                          const isSyncing = plexSyncStatus?.serverId === s.serverId && plexSyncStatus.state === 'running'
                          return (
                          <tr key={s.serverId} className="border-t border-gray-600 hover:bg-gray-700/30">
                            <td className="px-4 py-2 text-white">{s.name}</td>
                            <td className="px-4 py-2 text-gray-400">{s.version ?? '—'}</td>
                            <td className="px-4 py-2 text-gray-400">
                              {isSyncing ? (
                                <span className="text-amber-400" title={`${plexSyncStatus.processedItems} / ${plexSyncStatus.totalItems} items · ${plexSyncStatus.tmdbIdsFound} TMDB IDs`}>
                                  Syncing: {plexSyncStatus.processedItems}/{plexSyncStatus.totalItems}
                                </span>
                              ) : (
                                s.movieCount
                              )}
                            </td>
                            <td className="px-4 py-2 text-gray-400">
                              {isSyncing ? (
                                <span className="text-amber-400">In progress…</span>
                              ) : (
                                formatLastSync(s.lastLibrarySyncUtc)
                              )}
                            </td>
                            <td className="px-4 py-2 flex flex-wrap gap-2">
                              <button
                                type="button"
                                onClick={() => handlePlexSyncLibrary(s.serverId)}
                                disabled={plexSyncServerId !== null}
                                className="rounded bg-gray-600 px-2 py-1 text-xs text-white hover:bg-gray-500 disabled:opacity-50"
                              >
                                Sync now
                              </button>
                              <button
                                type="button"
                                onClick={() => handlePlexDeleteServer(s.serverId)}
                                disabled={plexDeleteServerId !== null}
                                className="rounded bg-red-600/80 px-2 py-1 text-xs text-white hover:bg-red-600 disabled:opacity-50"
                              >
                                Delete
                              </button>
                            </td>
                          </tr>
                          )
                        })}
                      </tbody>
                    </table>
                  </div>
                )}
              </section>
            </div>
          </div>
        )}

        {/* Media Servers: Jellyfin */}
        {selectedService === 'jellyfin' && (
          <div className="rounded-lg bg-gray-800 p-6">
            <h2 className="text-lg font-semibold text-white mb-4">Media Servers — JellyFin</h2>
            {jellyfinMessage && (
              <p className={`mb-4 text-sm ${jellyfinMessage.type === 'success' ? 'text-green-400' : 'text-red-400'}`}>
                {jellyfinMessage.text}
              </p>
            )}
            <div className="space-y-6">
              <section>
                <h3 className="text-sm font-medium text-gray-300 mb-2">Add server</h3>
                <div className="flex flex-wrap gap-3 items-end">
                  <div>
                    <label className="block text-xs text-gray-400 mb-1">URL:Port</label>
                    <input
                      type="text"
                      value={jellyfinAddForm.baseUrl}
                      onChange={(e) => setJellyfinAddForm((f) => ({ ...f, baseUrl: e.target.value }))}
                      placeholder="http://host:8096"
                      className="rounded bg-gray-700 border border-gray-600 px-3 py-2 text-white text-sm w-56"
                    />
                  </div>
                  <div>
                    <label className="block text-xs text-gray-400 mb-1">API Key</label>
                    <input
                      type="password"
                      value={jellyfinAddForm.apiKey}
                      onChange={(e) => setJellyfinAddForm((f) => ({ ...f, apiKey: e.target.value }))}
                      placeholder="API Key"
                      className="rounded bg-gray-700 border border-gray-600 px-3 py-2 text-white text-sm w-48"
                    />
                  </div>
                  <button
                    type="button"
                    onClick={handleJellyfinAddServer}
                    disabled={jellyfinAddSaving}
                    className="rounded bg-emerald-600 px-4 py-2 text-white text-sm font-medium hover:bg-emerald-500 disabled:opacity-50"
                  >
                    Add server
                  </button>
                </div>
              </section>
              <section>
                <h3 className="text-sm font-medium text-gray-300 mb-2">Current servers</h3>
                {jellyfinServersLoading && jellyfinServers.length === 0 ? (
                  <p className="text-gray-400 text-sm">Loading…</p>
                ) : jellyfinServers.length === 0 ? (
                  <p className="text-gray-400 text-sm">No JellyFin servers. Add one above.</p>
                ) : (
                  <div className="overflow-x-auto rounded border border-gray-600">
                    <table className="min-w-full text-sm">
                      <thead>
                        <tr className="bg-gray-700/50 text-left">
                          <th className="px-4 py-2 text-gray-300 font-medium">Friendly name</th>
                          <th className="px-4 py-2 text-gray-300 font-medium">Version</th>
                          <th className="px-4 py-2 text-gray-300 font-medium">Movies</th>
                          <th className="px-4 py-2 text-gray-300 font-medium">URL:Port</th>
                          <th className="px-4 py-2 text-gray-300 font-medium">Last sync</th>
                          <th className="px-4 py-2 text-gray-300 font-medium">Actions</th>
                        </tr>
                      </thead>
                      <tbody>
                        {jellyfinServers.map((s) => (
                          <tr key={s.serverId} className="border-t border-gray-600 hover:bg-gray-700/30">
                            <td className="px-4 py-2 text-white">{s.name}</td>
                            <td className="px-4 py-2 text-gray-400">{s.version ?? '—'}</td>
                            <td className="px-4 py-2 text-gray-400">{s.movieCount}</td>
                            <td className="px-4 py-2 text-gray-400 font-mono text-xs">{s.baseUrl ?? '—'}</td>
                            <td className="px-4 py-2 text-gray-400">{formatLastSync(s.lastLibrarySyncUtc)}</td>
                            <td className="px-4 py-2 flex flex-wrap gap-2">
                              <button
                                type="button"
                                onClick={() => handleJellyfinSyncLibrary(s.serverId)}
                                disabled={jellyfinSyncServerId !== null}
                                className="rounded bg-gray-600 px-2 py-1 text-xs text-white hover:bg-gray-500 disabled:opacity-50"
                              >
                                Sync now
                              </button>
                              <button
                                type="button"
                                onClick={() => handleJellyfinTestConnection(s.serverId)}
                                disabled={jellyfinTestServerId !== null}
                                className="rounded bg-gray-600 px-2 py-1 text-xs text-white hover:bg-gray-500 disabled:opacity-50"
                              >
                                Test connection
                              </button>
                              <button
                                type="button"
                                onClick={() => handleJellyfinDeleteServer(s.serverId)}
                                disabled={jellyfinDeleteServerId !== null}
                                className="rounded bg-red-600/80 px-2 py-1 text-xs text-white hover:bg-red-600 disabled:opacity-50"
                              >
                                Delete
                              </button>
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                )}
              </section>
            </div>
          </div>
        )}

        {/* Media Servers: Emby */}
        {selectedService === 'emby' && (
          <div className="rounded-lg bg-gray-800 p-6">
            <h2 className="text-lg font-semibold text-white mb-4">Media Servers — Emby</h2>
            {embyMessage && (
              <p className={`mb-4 text-sm ${embyMessage.type === 'success' ? 'text-green-400' : 'text-red-400'}`}>
                {embyMessage.text}
              </p>
            )}
            <div className="space-y-6">
              <section>
                <h3 className="text-sm font-medium text-gray-300 mb-2">Add server</h3>
                <div className="flex flex-wrap gap-3 items-end">
                  <div>
                    <label className="block text-xs text-gray-400 mb-1">URL:Port</label>
                    <input
                      type="text"
                      value={embyAddForm.baseUrl}
                      onChange={(e) => setEmbyAddForm((f) => ({ ...f, baseUrl: e.target.value }))}
                      placeholder="http://host:8096"
                      className="rounded bg-gray-700 border border-gray-600 px-3 py-2 text-white text-sm w-56"
                    />
                  </div>
                  <div>
                    <label className="block text-xs text-gray-400 mb-1">API Key</label>
                    <input
                      type="password"
                      value={embyAddForm.apiKey}
                      onChange={(e) => setEmbyAddForm((f) => ({ ...f, apiKey: e.target.value }))}
                      placeholder="API Key"
                      className="rounded bg-gray-700 border border-gray-600 px-3 py-2 text-white text-sm w-48"
                    />
                  </div>
                  <button
                    type="button"
                    onClick={handleEmbyAddServer}
                    disabled={embyAddSaving}
                    className="rounded bg-emerald-600 px-4 py-2 text-white text-sm font-medium hover:bg-emerald-500 disabled:opacity-50"
                  >
                    Add server
                  </button>
                </div>
              </section>
              <section>
                <h3 className="text-sm font-medium text-gray-300 mb-2">Current servers</h3>
                {embyServersLoading && embyServers.length === 0 ? (
                  <p className="text-gray-400 text-sm">Loading…</p>
                ) : embyServers.length === 0 ? (
                  <p className="text-gray-400 text-sm">No Emby servers. Add one above.</p>
                ) : (
                  <div className="overflow-x-auto rounded border border-gray-600">
                    <table className="min-w-full text-sm">
                      <thead>
                        <tr className="bg-gray-700/50 text-left">
                          <th className="px-4 py-2 text-gray-300 font-medium">Friendly name</th>
                          <th className="px-4 py-2 text-gray-300 font-medium">Version</th>
                          <th className="px-4 py-2 text-gray-300 font-medium">Movies</th>
                          <th className="px-4 py-2 text-gray-300 font-medium">URL:Port</th>
                          <th className="px-4 py-2 text-gray-300 font-medium">Last sync</th>
                          <th className="px-4 py-2 text-gray-300 font-medium">Actions</th>
                        </tr>
                      </thead>
                      <tbody>
                        {embyServers.map((s) => (
                          <tr key={s.serverId} className="border-t border-gray-600 hover:bg-gray-700/30">
                            <td className="px-4 py-2 text-white">{s.name}</td>
                            <td className="px-4 py-2 text-gray-400">{s.version ?? '—'}</td>
                            <td className="px-4 py-2 text-gray-400">{s.movieCount}</td>
                            <td className="px-4 py-2 text-gray-400 font-mono text-xs">{s.baseUrl ?? '—'}</td>
                            <td className="px-4 py-2 text-gray-400">{formatLastSync(s.lastLibrarySyncUtc)}</td>
                            <td className="px-4 py-2 flex flex-wrap gap-2">
                              <button
                                type="button"
                                onClick={() => handleEmbySyncLibrary(s.serverId)}
                                disabled={embySyncServerId !== null}
                                className="rounded bg-gray-600 px-2 py-1 text-xs text-white hover:bg-gray-500 disabled:opacity-50"
                              >
                                Sync now
                              </button>
                              <button
                                type="button"
                                onClick={() => handleEmbyTestConnection(s.serverId)}
                                disabled={embyTestServerId !== null}
                                className="rounded bg-gray-600 px-2 py-1 text-xs text-white hover:bg-gray-500 disabled:opacity-50"
                              >
                                Test connection
                              </button>
                              <button
                                type="button"
                                onClick={() => handleEmbyDeleteServer(s.serverId)}
                                disabled={embyDeleteServerId !== null}
                                className="rounded bg-red-600/80 px-2 py-1 text-xs text-white hover:bg-red-600 disabled:opacity-50"
                              >
                                Delete
                              </button>
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                )}
              </section>
            </div>
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
                      â­ {selectedMovie.vote_average.toFixed(1)}
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
    </div>
  )
}
