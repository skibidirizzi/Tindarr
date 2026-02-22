import { useCallback, useEffect, useState } from 'react'
import {
  apiClient,
  type MatchDto,
  type MovieDetailsDto,
} from '../lib/api'

const DEFAULT_SCOPE = { serviceType: 'tmdb', serverId: 'tmdb' } as const
const DETAIL_BATCH_SIZE = 10

type ViewMode = 'table' | 'gallery'

interface MatchEntry {
  tmdbId: number
  matchedWithDisplayNames: string[]
  details: MovieDetailsDto | null
}

async function fetchDetailsBatched(
  tmdbIds: number[],
  batchSize: number
): Promise<Map<number, MovieDetailsDto | null>> {
  const map = new Map<number, MovieDetailsDto | null>()
  for (let i = 0; i < tmdbIds.length; i += batchSize) {
    const batch = tmdbIds.slice(i, i + batchSize)
    const results = await Promise.allSettled(
      batch.map((id) => apiClient.getMovieDetails(id))
    )
    batch.forEach((id, j) => {
      const r = results[j]
      map.set(id, r?.status === 'fulfilled' ? r.value : null)
    })
  }
  return map
}

interface MatchesModalProps {
  isOpen: boolean
  onClose: () => void
}

export default function MatchesModal({ isOpen, onClose }: MatchesModalProps) {
  const [entries, setEntries] = useState<MatchEntry[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [viewMode, setViewMode] = useState<ViewMode>('gallery')

  const loadMatches = useCallback(async () => {
    if (!isOpen) return
    setLoading(true)
    setError(null)
    try {
      const resp = await apiClient.listMatches(
        DEFAULT_SCOPE.serviceType,
        DEFAULT_SCOPE.serverId
      )
      const items = resp.items as MatchDto[]
      const tmdbIds = items.map((i) => i.tmdbId)
      const detailsMap = await fetchDetailsBatched(tmdbIds, DETAIL_BATCH_SIZE)
      setEntries(
        items.map((item) => ({
          tmdbId: item.tmdbId,
          matchedWithDisplayNames: item.matchedWithDisplayNames ?? [],
          details: detailsMap.get(item.tmdbId) ?? null,
        }))
      )
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load matches')
      setEntries([])
    } finally {
      setLoading(false)
    }
  }, [isOpen])

  useEffect(() => {
    if (isOpen) loadMatches()
  }, [isOpen, loadMatches])

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
              Matches
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
                onClick={loadMatches}
                className="mt-4 rounded-xl bg-pink-500/30 px-4 py-2 text-pink-200 hover:bg-pink-500/50"
              >
                Retry
              </button>
            </div>
          )}

          {!loading && !error && entries.length === 0 && (
            <div className="rounded-2xl border border-pink-500/30 bg-slate-800/90 p-12 text-center text-gray-400">
              <p className="text-lg">No matches yet.</p>
              <p className="mt-2 text-sm">
                Matches appear when you and others in the same scope like the same movies.
              </p>
              <button
                type="button"
                onClick={onClose}
                className="mt-6 rounded-xl bg-pink-500/30 px-4 py-2 text-pink-200 hover:bg-pink-500/50"
              >
                Close
              </button>
            </div>
          )}

          {!loading && !error && entries.length > 0 && viewMode === 'gallery' && (
            <div className="grid grid-cols-2 gap-4 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-5">
              {entries.map((entry) => (
                <div
                  key={entry.tmdbId}
                  className="overflow-hidden rounded-2xl border border-pink-500/30 bg-slate-800/90 shadow-lg transition hover:border-pink-400/50"
                >
                  <div className="relative aspect-[2/3] bg-slate-700">
                    {entry.details?.posterUrl ? (
                      <img
                        src={entry.details.posterUrl}
                        alt=""
                        className="h-full w-full object-cover"
                      />
                    ) : (
                      <div className="flex h-full items-center justify-center text-4xl text-gray-500">
                        🎬
                      </div>
                    )}
                    {entry.matchedWithDisplayNames.length > 0 && (
                      <div
                        className="absolute left-2 top-2 max-w-[85%] rounded-lg bg-slate-900/90 px-2 py-1.5 text-xs text-pink-200 shadow-lg"
                        title={`Matched with: ${entry.matchedWithDisplayNames.join(', ')}`}
                      >
                        <span className="font-semibold text-pink-300">
                          Matched with:
                        </span>{' '}
                        <span className="truncate">
                          {entry.matchedWithDisplayNames.join(', ')}
                        </span>
                      </div>
                    )}
                  </div>
                  <div className="p-3">
                    <p
                      className="truncate font-medium text-gray-200"
                      title={entry.details?.title ?? String(entry.tmdbId)}
                    >
                      {entry.details?.title ?? `Movie ${entry.tmdbId}`}
                    </p>
                    <p className="mt-1 text-xs text-gray-500">
                      {entry.details?.releaseYear ?? '—'}
                    </p>
                  </div>
                </div>
              ))}
            </div>
          )}

          {!loading && !error && entries.length > 0 && viewMode === 'table' && (
            <div className="overflow-hidden rounded-2xl border border-pink-500/30 bg-slate-800/90">
              <div className="overflow-x-auto">
                <table className="w-full min-w-[600px] text-left text-sm">
                  <thead>
                    <tr className="border-b border-pink-500/30 bg-slate-800">
                      <th className="px-4 py-3 font-medium text-gray-300">
                        Poster
                      </th>
                      <th className="px-4 py-3 font-medium text-gray-300">
                        Title
                      </th>
                      <th className="px-4 py-3 font-medium text-gray-300">
                        Year
                      </th>
                      <th className="px-4 py-3 font-medium text-gray-300">
                        Rating
                      </th>
                      <th className="px-4 py-3 font-medium text-gray-300">
                        Matched With
                      </th>
                    </tr>
                  </thead>
                  <tbody>
                    {entries.map((entry) => (
                      <tr
                        key={entry.tmdbId}
                        className="border-b border-slate-700/50 transition hover:bg-slate-700/30"
                      >
                        <td className="w-16 px-4 py-2">
                          <div className="aspect-[2/3] w-12 overflow-hidden rounded bg-slate-700">
                            {entry.details?.posterUrl ? (
                              <img
                                src={entry.details.posterUrl}
                                alt=""
                                className="h-full w-full object-cover"
                              />
                            ) : (
                              <div className="flex h-full items-center justify-center text-lg text-gray-500">
                                🎬
                              </div>
                            )}
                          </div>
                        </td>
                        <td className="px-4 py-2 font-medium text-gray-200">
                          {entry.details?.title ?? `Movie ${entry.tmdbId}`}
                        </td>
                        <td className="px-4 py-2 text-gray-400">
                          {entry.details?.releaseYear ?? '—'}
                        </td>
                        <td className="px-4 py-2 text-gray-400">
                          {entry.details?.rating != null
                            ? entry.details.rating.toFixed(1)
                            : '—'}
                        </td>
                        <td className="max-w-[200px] px-4 py-2 text-gray-300">
                          {entry.matchedWithDisplayNames.length > 0
                            ? entry.matchedWithDisplayNames.join(', ')
                            : '—'}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>
          )}
        </div>
      </div>
    </div>
  )
}
