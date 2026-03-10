import { useCallback, useEffect, useMemo, useState } from 'react'
import {
  apiClient,
  type AdminInteractionDto,
  type MovieDetailsDto,
} from '../lib/api'

interface AdminUserInteractionsModalProps {
  isOpen: boolean
  userId: string
  displayName: string
  onClose: () => void
  onInteractionsChanged?: () => void
}

type ViewMode = 'table' | 'gallery'
type TimelinePreset = '24h' | '7d' | '30d' | 'all'

const DETAIL_BATCH_SIZE = 10
const DETAIL_FETCH_LIMIT = 200

async function fetchDetailsBatched(
  tmdbIds: number[],
  batchSize: number
): Promise<Map<number, MovieDetailsDto | null>> {
  const map = new Map<number, MovieDetailsDto | null>()
  for (let i = 0; i < tmdbIds.length; i += batchSize) {
    const batch = tmdbIds.slice(i, i + batchSize)
    const results = await Promise.allSettled(batch.map((id) => apiClient.getMovieDetails(id)))
    batch.forEach((id, j) => {
      const r = results[j]
      map.set(id, r?.status === 'fulfilled' ? r.value : null)
    })
  }
  return map
}

function formatWhen(iso: string): string {
  try {
    const d = new Date(iso)
    return Number.isNaN(d.getTime()) ? iso : d.toLocaleString()
  } catch {
    return iso
  }
}

export default function AdminUserInteractionsModal({
  isOpen,
  userId,
  displayName,
  onClose,
  onInteractionsChanged,
}: AdminUserInteractionsModalProps) {
  const [items, setItems] = useState<AdminInteractionDto[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const [viewMode, setViewMode] = useState<ViewMode>('gallery')
  const [actionFilter, setActionFilter] = useState<AdminInteractionDto['action'] | 'all'>('all')
  const [scopeFilter, setScopeFilter] = useState<string>('all')
  const [timeline, setTimeline] = useState<TimelinePreset>('all')

  const [selectedIds, setSelectedIds] = useState<Set<number>>(() => new Set())
  const [detailsMap, setDetailsMap] = useState<Map<number, MovieDetailsDto | null>>(() => new Map())

  const scopeOptions = useMemo(() => {
    const keys = new Set<string>()
    for (const it of items) {
      keys.add(`${it.serviceType}:${it.serverId}`)
    }
    return Array.from(keys).sort()
  }, [items])

  const sinceUtc = useMemo(() => {
    if (timeline === 'all') return undefined
    const now = Date.now()
    const deltaMs =
      timeline === '24h'
        ? 24 * 60 * 60 * 1000
        : timeline === '7d'
          ? 7 * 24 * 60 * 60 * 1000
          : 30 * 24 * 60 * 60 * 1000
    return new Date(now - deltaMs).toISOString()
  }, [timeline])

  const load = useCallback(async () => {
    if (!isOpen) return
    setLoading(true)
    setError(null)

    try {
      const parsedScope =
        scopeFilter !== 'all' && scopeFilter.includes(':')
          ? {
              serviceType: scopeFilter.split(':')[0],
              serverId: scopeFilter.split(':').slice(1).join(':'),
            }
          : null

      const resp = await apiClient.searchAdminInteractions({
        userId,
        serviceType: parsedScope?.serviceType,
        serverId: parsedScope?.serverId,
        action: actionFilter === 'all' ? undefined : actionFilter,
        sinceUtc,
        limit: 5000,
      })
      setItems(resp.items ?? [])
      setSelectedIds(new Set())
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load interactions')
      setItems([])
      setSelectedIds(new Set())
    } finally {
      setLoading(false)
    }
  }, [isOpen, userId, actionFilter, scopeFilter, sinceUtc])

  useEffect(() => {
    if (isOpen) load()
  }, [isOpen, load])

  useEffect(() => {
    if (!isOpen) return
    if (viewMode !== 'gallery') return
    if (items.length === 0) {
      setDetailsMap(new Map())
      return
    }

    let cancelled = false
    ;(async () => {
      const unique = Array.from(new Set(items.map((i) => i.tmdbId))).slice(0, DETAIL_FETCH_LIMIT)
      const map = await fetchDetailsBatched(unique, DETAIL_BATCH_SIZE)
      if (!cancelled) setDetailsMap(map)
    })()

    return () => {
      cancelled = true
    }
  }, [isOpen, viewMode, items])

  const toggleSelected = useCallback((id: number, checked: boolean) => {
    setSelectedIds((prev) => {
      const next = new Set(prev)
      if (checked) next.add(id)
      else next.delete(id)
      return next
    })
  }, [])

  const selectAllVisible = useCallback((checked: boolean) => {
    if (!checked) {
      setSelectedIds(new Set())
      return
    }
    setSelectedIds(new Set(items.map((i) => i.id)))
  }, [items])

  const deleteOne = useCallback(
    async (id: number) => {
      if (!window.confirm('Delete this interaction?')) return
      try {
        await apiClient.deleteAdminInteraction(id)
        await load()
        onInteractionsChanged?.()
      } catch (err) {
        setError(err instanceof Error ? err.message : 'Failed to delete interaction')
      }
    },
    [load, onInteractionsChanged]
  )

  const deleteSelected = useCallback(async () => {
    const ids = Array.from(selectedIds)
    if (ids.length === 0) return
    if (!window.confirm(`Delete ${ids.length} interactions?`)) return
    try {
      await apiClient.deleteAdminInteractions(ids)
      await load()
      onInteractionsChanged?.()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to delete interactions')
    }
  }, [selectedIds, load, onInteractionsChanged])

  if (!isOpen) return null

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4 backdrop-blur-sm">
      <div className="flex max-h-[90vh] w-full max-w-6xl flex-col overflow-hidden rounded-2xl border border-pink-500/30 bg-gradient-to-br from-slate-900 via-purple-900/30 to-slate-900 shadow-2xl">
        <header className="flex shrink-0 flex-wrap items-center justify-between gap-4 border-b border-pink-500/30 bg-slate-800/90 px-6 py-4">
          <div className="flex items-center gap-4">
            <button
              type="button"
              onClick={onClose}
              className="rounded-xl border border-pink-500/40 bg-slate-800 px-4 py-2 text-gray-200 transition hover:bg-slate-700 focus:outline-none focus:ring-2 focus:ring-pink-400"
            >
              ✕ Close
            </button>
            <h2 className="bg-gradient-to-r from-pink-400 to-purple-400 bg-clip-text text-2xl font-bold text-transparent">
              All interactions — {displayName}
            </h2>
          </div>

          <div className="flex gap-2">
            <button
              type="button"
              onClick={() => setViewMode('gallery')}
              className={`rounded-xl px-4 py-2 text-sm font-medium transition focus:outline-none focus:ring-2 focus:ring-pink-400 ${
                viewMode === 'gallery'
                  ? 'bg-pink-500/30 text-pink-200 ring-1 ring-pink-400/50'
                  : 'bg-slate-800 text-gray-300 hover:bg-slate-700'
              }`}
            >
              Gallery
            </button>
            <button
              type="button"
              onClick={() => setViewMode('table')}
              className={`rounded-xl px-4 py-2 text-sm font-medium transition focus:outline-none focus:ring-2 focus:ring-pink-400 ${
                viewMode === 'table'
                  ? 'bg-pink-500/30 text-pink-200 ring-1 ring-pink-400/50'
                  : 'bg-slate-800 text-gray-300 hover:bg-slate-700'
              }`}
            >
              Table
            </button>
          </div>
        </header>

        <div className="flex-1 overflow-y-auto p-6">
          <div className="mb-4 rounded-2xl border border-pink-500/30 bg-slate-800/90 p-4">
            <div className="flex flex-wrap items-center gap-3">
              <div className="flex items-center gap-2">
                <span className="text-sm text-gray-400">Type</span>
                <select
                  value={actionFilter}
                  onChange={(e) => setActionFilter(e.target.value as typeof actionFilter)}
                  className="rounded-xl border border-pink-500/40 bg-slate-800 px-3 py-2 text-sm text-gray-200 transition hover:bg-slate-700 focus:outline-none focus:ring-2 focus:ring-pink-400"
                >
                  <option value="all">All</option>
                  <option value="Like">Like</option>
                  <option value="Nope">Nope</option>
                  <option value="Skip">Skip</option>
                  <option value="Superlike">Superlike</option>
                </select>
              </div>

              <div className="flex items-center gap-2">
                <span className="text-sm text-gray-400">Service</span>
                <select
                  value={scopeFilter}
                  onChange={(e) => setScopeFilter(e.target.value)}
                  className="rounded-xl border border-pink-500/40 bg-slate-800 px-3 py-2 text-sm text-gray-200 transition hover:bg-slate-700 focus:outline-none focus:ring-2 focus:ring-pink-400"
                >
                  <option value="all">All</option>
                  {scopeOptions.map((key) => (
                    <option key={key} value={key}>
                      {key}
                    </option>
                  ))}
                </select>
              </div>

              <div className="flex items-center gap-2">
                <span className="text-sm text-gray-400">Timeline</span>
                <select
                  value={timeline}
                  onChange={(e) => setTimeline(e.target.value as TimelinePreset)}
                  className="rounded-xl border border-pink-500/40 bg-slate-800 px-3 py-2 text-sm text-gray-200 transition hover:bg-slate-700 focus:outline-none focus:ring-2 focus:ring-pink-400"
                >
                  <option value="24h">Last 24 hours</option>
                  <option value="7d">Last 7 days</option>
                  <option value="30d">Last 30 days</option>
                  <option value="all">All time</option>
                </select>
              </div>

              <div className="flex-1" />

              <button
                type="button"
                onClick={deleteSelected}
                disabled={selectedIds.size === 0}
                className={`rounded-xl px-4 py-2 text-sm font-medium transition focus:outline-none focus:ring-2 focus:ring-pink-400 ${
                  selectedIds.size === 0
                    ? 'cursor-not-allowed bg-slate-800 text-gray-500 opacity-60'
                    : 'border border-red-500/40 bg-red-500/20 text-red-200 hover:bg-red-500/30'
                }`}
              >
                Delete selected ({selectedIds.size})
              </button>
            </div>
          </div>

          {loading && (
            <div className="flex justify-center py-16">
              <div className="h-12 w-12 animate-spin rounded-full border-4 border-pink-500 border-t-transparent" />
            </div>
          )}

          {error && (
            <div className="rounded-2xl border border-red-500/40 bg-slate-800/90 p-6 text-center text-red-300">
              <p>{error}</p>
              <button
                type="button"
                onClick={load}
                className="mt-4 rounded-xl bg-pink-500/30 px-4 py-2 text-pink-200 hover:bg-pink-500/50"
              >
                Retry
              </button>
            </div>
          )}

          {!loading && !error && items.length === 0 && (
            <div className="rounded-2xl border border-pink-500/30 bg-slate-800/90 p-12 text-center text-gray-400">
              <p className="text-lg">No interactions yet.</p>
              <button
                type="button"
                onClick={onClose}
                className="mt-6 rounded-xl bg-pink-500/30 px-4 py-2 text-pink-200 hover:bg-pink-500/50"
              >
                Close
              </button>
            </div>
          )}

          {!loading && !error && items.length > 0 && (
            <>
              {viewMode === 'gallery' && (
                <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
                  {items.map((it) => {
                    const details = detailsMap.get(it.tmdbId) ?? null
                    const bannerUrl = details?.backdropUrl ?? details?.posterUrl ?? null
                    return (
                      <div
                        key={it.id}
                        className="overflow-hidden rounded-2xl border border-pink-500/30 bg-slate-800/90 shadow-lg transition hover:border-pink-400/50"
                      >
                        <div
                          className="relative aspect-video bg-slate-700"
                          style={
                            bannerUrl
                              ? {
                                  backgroundImage: `url(${bannerUrl})`,
                                  backgroundSize: 'cover',
                                  backgroundPosition: 'center',
                                }
                              : undefined
                          }
                        >
                          {!bannerUrl && (
                            <div className="flex h-full items-center justify-center text-4xl text-gray-500">
                              🎬
                            </div>
                          )}
                          <div className="absolute inset-0 bg-gradient-to-t from-slate-900/90 via-slate-900/10 to-slate-900/10" />
                          <label className="absolute left-3 top-3 inline-flex items-center gap-2 rounded-lg bg-slate-900/80 px-2 py-1 text-xs text-gray-200">
                            <input
                              type="checkbox"
                              checked={selectedIds.has(it.id)}
                              onChange={(e) => toggleSelected(it.id, e.target.checked)}
                              className="h-4 w-4 accent-pink-500"
                            />
                            Select
                          </label>
                          <div className="absolute right-3 top-3 rounded-lg bg-slate-900/80 px-2 py-1 text-xs text-pink-200">
                            {it.action}
                          </div>
                          <div className="absolute bottom-0 w-full p-3">
                            <div className="flex items-end justify-between gap-3">
                              <div className="min-w-0">
                                <p className="truncate font-medium text-gray-200" title={details?.title ?? String(it.tmdbId)}>
                                  {details?.title ?? `Movie ${it.tmdbId}`}
                                </p>
                                <p className="mt-1 text-xs text-gray-400">
                                  {formatWhen(it.createdAtUtc)} • {it.serviceType}:{it.serverId}
                                </p>
                              </div>
                              <button
                                type="button"
                                onClick={() => void deleteOne(it.id)}
                                className="shrink-0 rounded-xl border border-red-500/40 bg-red-500/20 px-3 py-1.5 text-xs font-medium text-red-200 transition hover:bg-red-500/30 focus:outline-none focus:ring-2 focus:ring-pink-400"
                              >
                                Delete
                              </button>
                            </div>
                          </div>
                        </div>
                      </div>
                    )
                  })}
                </div>
              )}

              {viewMode === 'table' && (
                <div className="overflow-hidden rounded-2xl border border-pink-500/30 bg-slate-800/90">
                  <div className="overflow-x-auto">
                    <table className="w-full min-w-[920px] text-left text-sm">
                      <thead>
                        <tr className="border-b border-pink-500/30 bg-slate-800">
                          <th className="px-4 py-3 font-medium text-gray-300">
                            <input
                              type="checkbox"
                              checked={selectedIds.size > 0 && selectedIds.size === items.length}
                              onChange={(e) => selectAllVisible(e.target.checked)}
                              className="h-4 w-4 accent-pink-500"
                              aria-label="Select all"
                            />
                          </th>
                          <th className="px-4 py-3 font-medium text-gray-300">When</th>
                          <th className="px-4 py-3 font-medium text-gray-300">Action</th>
                          <th className="px-4 py-3 font-medium text-gray-300">Service</th>
                          <th className="px-4 py-3 font-medium text-gray-300">Server</th>
                          <th className="px-4 py-3 font-medium text-gray-300">TMDB</th>
                          <th className="px-4 py-3 font-medium text-gray-300">Delete</th>
                        </tr>
                      </thead>
                      <tbody>
                        {items.map((it) => (
                          <tr
                            key={it.id}
                            className="border-b border-slate-700/50 transition hover:bg-slate-700/30"
                          >
                            <td className="px-4 py-2">
                              <input
                                type="checkbox"
                                checked={selectedIds.has(it.id)}
                                onChange={(e) => toggleSelected(it.id, e.target.checked)}
                                className="h-4 w-4 accent-pink-500"
                                aria-label="Select interaction"
                              />
                            </td>
                            <td className="px-4 py-2 text-gray-300">{formatWhen(it.createdAtUtc)}</td>
                            <td className="px-4 py-2 text-gray-200">{it.action}</td>
                            <td className="px-4 py-2 text-gray-300">{it.serviceType}</td>
                            <td className="px-4 py-2 text-gray-300">{it.serverId}</td>
                            <td className="px-4 py-2 font-mono text-gray-200">{it.tmdbId}</td>
                            <td className="px-4 py-2">
                              <button
                                type="button"
                                onClick={() => void deleteOne(it.id)}
                                className="rounded-xl border border-red-500/40 bg-red-500/20 px-3 py-1.5 text-xs font-medium text-red-200 transition hover:bg-red-500/30 focus:outline-none focus:ring-2 focus:ring-pink-400"
                              >
                                Delete
                              </button>
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                </div>
              )}
            </>
          )}
        </div>
      </div>
    </div>
  )
}
