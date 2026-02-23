import { useEffect, useRef, useState } from 'react'
import { apiClient, type MovieDetailsDto } from '../lib/api'

export interface MovieDetailsModalScope {
  serviceType: string
  serverId: string
}

export interface MovieDetailsModalProps {
  /** When set, modal is open and shows this movie. */
  movie: MovieDetailsDto | null
  /** When set and movie is null, modal fetches details by tmdbId. */
  tmdbId?: number | null
  /** When set and is a media-server scope (not tmdb/tmdb), show Cast option. */
  scope?: MovieDetailsModalScope | null
  /** When set (e.g. in Rooms), Cast is only shown if the movie's tmdbId is in this list. */
  matchedTmdbIds?: number[] | null
  /** When false, Cast section is hidden (e.g. for room guests). Default true. */
  canCast?: boolean
  /** When set (e.g. from Room), use shared cast devices and do not fetch; fetch is triggered once via ensureCastDevicesLoaded. */
  castDevicesFromParent?: { id: string; name: string }[]
  castDevicesLoadingFromParent?: boolean
  ensureCastDevicesLoaded?: () => void | Promise<void>
  onClose: () => void
}

function formatRuntime(minutes: number | null | undefined): string {
  if (minutes == null || minutes < 1) return '—'
  const h = Math.floor(minutes / 60)
  const m = minutes % 60
  if (h === 0) return `${m} min`
  return m === 0 ? `${h} h` : `${h} h ${m} min`
}

function languageDisplayName(code: string | null | undefined): string {
  if (code == null || code === '') return '—'
  try {
    return new Intl.DisplayNames(['en'], { type: 'language' }).of(code) ?? code
  } catch {
    return code
  }
}

function Pill({
  label,
  value,
  valueOnly,
  className = '',
}: {
  label: string
  value: string
  valueOnly?: boolean
  className?: string
}) {
  return (
    <div
      className={`min-w-[4.5rem] max-w-[10rem] rounded-lg px-3 py-2 backdrop-blur-sm ${className || 'bg-black/60'}`}
    >
      {!valueOnly && (
        <span className="block text-[10px] font-medium uppercase tracking-wider text-gray-400">
          {label}
        </span>
      )}
      <span className="block text-sm font-medium text-white leading-tight">{value}</span>
    </div>
  )
}

/** MPAA rating pill background: green (G) → yellow → red (NC-17), same transparency as other pills. */
function mpaaPillBgClass(rating: string): string {
  const r = rating.toUpperCase()
  if (r === 'G') return 'bg-emerald-600/60'
  if (r === 'PG') return 'bg-lime-500/60'
  if (r === 'PG-13') return 'bg-yellow-500/60'
  if (r === 'R') return 'bg-orange-500/60'
  if (r === 'NC-17') return 'bg-red-600/60'
  return 'bg-black/60'
}

function isMediaServerScope(scope: MovieDetailsModalScope | null | undefined): boolean {
  if (!scope) return false
  return scope.serviceType !== 'tmdb' || scope.serverId !== 'tmdb'
}

export default function MovieDetailsModal({
  movie: initialMovie,
  tmdbId,
  scope,
  matchedTmdbIds,
  canCast = true,
  castDevicesFromParent,
  castDevicesLoadingFromParent,
  ensureCastDevicesLoaded,
  onClose,
}: MovieDetailsModalProps) {
  const [movie, setMovie] = useState<MovieDetailsDto | null>(initialMovie ?? null)
  const [loading, setLoading] = useState(false)
  const [fetchError, setFetchError] = useState<string | null>(null)
  const [castDevices, setCastDevices] = useState<{ id: string; name: string }[]>([])
  const [castDevicesLoading, setCastDevicesLoading] = useState(false)
  const [selectedCastDeviceId, setSelectedCastDeviceId] = useState<string>('')
  const [casting, setCasting] = useState(false)
  const [castError, setCastError] = useState<string | null>(null)
  const castDevicesEnsuredRef = useRef(false)

  const isOpen = initialMovie != null || (tmdbId != null && tmdbId > 0)
  const isMatchedMovie =
    matchedTmdbIds == null || (movie != null && matchedTmdbIds.includes(movie.tmdbId))
  const showCast =
    canCast && isMediaServerScope(scope) && movie != null && isMatchedMovie

  const useParentCastDevices = castDevicesFromParent != null && ensureCastDevicesLoaded != null
  const effectiveCastDevices = useParentCastDevices ? (castDevicesFromParent ?? []) : castDevices
  const effectiveCastDevicesLoading = useParentCastDevices ? (castDevicesLoadingFromParent ?? false) : castDevicesLoading

  useEffect(() => {
    if (initialMovie != null) {
      setMovie(initialMovie)
      setFetchError(null)
      return
    }
    if (tmdbId != null && tmdbId > 0) {
      setMovie(null)
      setFetchError(null)
      setLoading(true)
      apiClient
        .getMovieDetails(tmdbId)
        .then((d) => {
          setMovie(d)
        })
        .catch((err) => {
          setFetchError(err instanceof Error ? err.message : 'Failed to load movie')
        })
        .finally(() => {
          setLoading(false)
        })
    } else {
      setMovie(null)
      setLoading(false)
      setFetchError(null)
    }
  }, [initialMovie, tmdbId])

  useEffect(() => {
    const handleEscape = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose()
    }
    if (isOpen) {
      document.addEventListener('keydown', handleEscape)
      document.body.style.overflow = 'hidden'
    }
    return () => {
      document.removeEventListener('keydown', handleEscape)
      document.body.style.overflow = ''
    }
  }, [isOpen, onClose])

  useEffect(() => {
    if (showCast && ensureCastDevicesLoaded && !castDevicesEnsuredRef.current) {
      castDevicesEnsuredRef.current = true
      ensureCastDevicesLoaded()
    }
  }, [showCast, ensureCastDevicesLoaded])

  useEffect(() => {
    if (useParentCastDevices && effectiveCastDevices.length > 0 && !effectiveCastDevices.some((d) => d.id === selectedCastDeviceId)) {
      setSelectedCastDeviceId(effectiveCastDevices[0]?.id ?? '')
    }
  }, [useParentCastDevices, effectiveCastDevices, selectedCastDeviceId])

  useEffect(() => {
    if (useParentCastDevices || !showCast || !scope) return
    setCastDevicesLoading(true)
    apiClient
      .listCastDevices()
      .then((list) => {
        setCastDevices(list.map((d) => ({ id: d.id, name: d.name })))
        setSelectedCastDeviceId(list[0]?.id ?? '')
      })
      .catch(() => setCastDevices([]))
      .finally(() => setCastDevicesLoading(false))
  }, [showCast, scope, useParentCastDevices])

  const handleCast = () => {
    if (!scope || !movie || !selectedCastDeviceId) return
    setCastError(null)
    setCasting(true)
    apiClient
      .castMovie(selectedCastDeviceId, scope.serviceType, scope.serverId, movie.tmdbId, movie.title ?? null)
      .then(() => onClose())
      .catch((err) => setCastError(err instanceof Error ? err.message : 'Cast failed'))
      .finally(() => setCasting(false))
  }

  if (!isOpen) return null

  if (loading) {
    return (
      <div
        className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4 backdrop-blur-sm"
        onClick={onClose}
        role="dialog"
        aria-modal="true"
        aria-label="Movie details"
      >
        <div
          className="flex flex-col items-center gap-4 rounded-2xl bg-slate-800 px-8 py-10 shadow-2xl"
          onClick={(e) => e.stopPropagation()}
        >
          <div className="h-10 w-10 animate-spin rounded-full border-4 border-pink-500 border-t-transparent" />
          <p className="text-gray-300">Loading movie details…</p>
        </div>
      </div>
    )
  }

  if (fetchError) {
    return (
      <div
        className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4 backdrop-blur-sm"
        onClick={onClose}
        role="dialog"
        aria-modal="true"
        aria-label="Movie details"
      >
        <div
          className="max-w-md rounded-2xl bg-slate-800 p-6 shadow-2xl"
          onClick={(e) => e.stopPropagation()}
        >
          <p className="text-red-400">{fetchError}</p>
          <button
            type="button"
            onClick={onClose}
            className="mt-4 rounded-lg bg-slate-600 px-4 py-2 text-white hover:bg-slate-500"
          >
            Close
          </button>
        </div>
      </div>
    )
  }

  if (movie == null) return null

  const posterUrl = movie.posterUrl ?? null

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4 backdrop-blur-sm"
      onClick={onClose}
      role="dialog"
      aria-modal="true"
      aria-labelledby="movie-details-title"
      aria-label="Movie details"
    >
      <div
        className="flex max-h-[90vh] w-full max-w-2xl flex-col overflow-hidden rounded-2xl bg-slate-800 shadow-2xl"
        onClick={(e) => e.stopPropagation()}
      >
        {/* Top 50%: poster as background with detail pills overlay */}
        <div
          className="relative flex-[0_0_50%] min-h-[200px] shrink-0 rounded-t-2xl bg-slate-700 bg-cover bg-center"
          style={posterUrl ? { backgroundImage: `url(${posterUrl})` } : undefined}
        >
          <div className="absolute inset-0 rounded-t-2xl bg-gradient-to-t from-slate-800 via-slate-800/30 to-transparent" />
          <div className="relative flex items-start justify-between gap-4 p-4">
            <h2 id="movie-details-title" className="text-xl font-bold text-white drop-shadow-lg sm:text-2xl">
              {movie.title}
            </h2>
            <button
              type="button"
              onClick={onClose}
              className="shrink-0 rounded-full bg-black/40 p-1.5 text-xl text-white transition-colors hover:bg-black/60 hover:text-white"
              aria-label="Close"
            >
              ✕
            </button>
          </div>
        </div>

        <div className="min-h-0 flex-1 overflow-y-auto p-6">
          <div className="space-y-4">
              <div className="flex flex-wrap justify-center gap-2">
                <Pill
                  label="Release year"
                  value={movie.releaseYear != null ? String(movie.releaseYear) : '—'}
                />
                {movie.mpaaRating && (
                  <Pill
                    label="Rating"
                    value={movie.mpaaRating}
                    className={mpaaPillBgClass(movie.mpaaRating)}
                  />
                )}
                <Pill
                  label="Rating (TMDB)"
                  value={movie.rating != null ? `${Number(movie.rating).toFixed(1)} / 10` : '—'}
                />
                {movie.genres.length > 0 && (
                  <Pill label="Genres" value={movie.genres.join(', ')} />
                )}
                <Pill label="Original language" value={languageDisplayName(movie.originalLanguage)} />
                <Pill label="Runtime" value={formatRuntime(movie.runtimeMinutes)} />
                <Pill label="TMDB ID" value={String(movie.tmdbId)} />
              </div>

              {/* Synopsis */}
              {movie.overview && (
                <div>
                  <span className="text-xs font-medium uppercase tracking-wider text-gray-500">
                    Synopsis
                  </span>
                  <p className="text-gray-300 leading-relaxed">{movie.overview}</p>
                </div>
              )}

              {/* Cast (media-server scope only) */}
              {showCast && scope && (
                <div>
                  <span className="text-xs font-medium uppercase tracking-wider text-gray-500">
                    Cast to device
                  </span>
                  <div className="mt-2 flex flex-wrap items-center gap-2">
                    <select
                      value={selectedCastDeviceId}
                      onChange={(e) => setSelectedCastDeviceId(e.target.value)}
                      className="rounded-lg border border-slate-600 bg-slate-700 px-3 py-2 text-gray-200 focus:border-pink-500 focus:outline-none focus:ring-1 focus:ring-pink-500"
                      aria-label="Select cast device"
                      disabled={effectiveCastDevicesLoading}
                    >
                      {effectiveCastDevicesLoading ? (
                        <option value="">Searching for devices…</option>
                      ) : effectiveCastDevices.length === 0 ? (
                        <option value="">No devices found</option>
                      ) : (
                        effectiveCastDevices.map((d) => (
                          <option key={d.id} value={d.id}>
                            {d.name}
                          </option>
                        ))
                      )}
                    </select>
                    <button
                      type="button"
                      onClick={handleCast}
                      disabled={casting || effectiveCastDevicesLoading || effectiveCastDevices.length === 0}
                      className="rounded-lg bg-pink-600 px-4 py-2 text-white hover:bg-pink-500 disabled:opacity-50 disabled:hover:bg-pink-600"
                    >
                      {casting ? 'Casting…' : 'Cast'}
                    </button>
                  </div>
                  {castError && (
                    <p className="mt-2 text-sm text-red-400">{castError}</p>
                  )}
                </div>
              )}
            </div>
          </div>
        </div>
      </div>
  )
}
