import { useCallback, useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import TmdbAttribution from '../components/TmdbAttribution'
import {
  apiClient,
  type InteractionDto,
  type MovieDetailsDto,
} from '../lib/api'

const DEFAULT_SCOPE = { serviceType: 'tmdb', serverId: 'tmdb' } as const
const INTERACTION_LIMIT = 500
const DETAIL_BATCH_SIZE = 10

type ViewMode = 'table' | 'gallery'

interface LikeEntry {
  tmdbId: number
  action: 'Like' | 'Superlike'
  createdAtUtc: string
  details: MovieDetailsDto | null
}

function formatLikedDate(iso: string): string {
  try {
    const d = new Date(iso)
    return Number.isNaN(d.getTime()) ? iso : d.toLocaleDateString(undefined, { dateStyle: 'medium' })
  } catch {
    return iso
  }
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

export default function MyLikes() {
  const navigate = useNavigate()
  const [entries, setEntries] = useState<LikeEntry[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [viewMode, setViewMode] = useState<ViewMode>('gallery')

  const loadLikes = useCallback(async () => {
    setLoading(true)
    setError(null)
    try {
      const [likeResp, superlikeResp] = await Promise.all([
        apiClient.listInteractions(
          DEFAULT_SCOPE.serviceType,
          DEFAULT_SCOPE.serverId,
          'Like',
          INTERACTION_LIMIT
        ),
        apiClient.listInteractions(
          DEFAULT_SCOPE.serviceType,
          DEFAULT_SCOPE.serverId,
          'Superlike',
          INTERACTION_LIMIT
        ),
      ])

      const allItems: Array<{ tmdbId: number; action: 'Like' | 'Superlike'; createdAtUtc: string }> = [
        ...likeResp.items.filter((i): i is InteractionDto & { action: 'Like' | 'Superlike' } => i.action === 'Like' || i.action === 'Superlike'),
        ...superlikeResp.items.filter((i): i is InteractionDto & { action: 'Like' | 'Superlike' } => i.action === 'Like' || i.action === 'Superlike'),
      ]
      const byTmdbId = new Map<
        number,
        { action: 'Like' | 'Superlike'; createdAtUtc: string }
      >()
      for (const item of allItems) {
        const existing = byTmdbId.get(item.tmdbId)
        const itemTime = new Date(item.createdAtUtc).getTime()
        const latest = !existing || itemTime > new Date(existing.createdAtUtc).getTime()
        const action: 'Like' | 'Superlike' =
          item.action === 'Superlike' || existing?.action === 'Superlike' ? 'Superlike' : 'Like'
        const createdAtUtc = latest ? item.createdAtUtc : existing!.createdAtUtc
        byTmdbId.set(item.tmdbId, { action, createdAtUtc })
      }

      const sorted = Array.from(byTmdbId.entries())
        .map(([tmdbId, { action, createdAtUtc }]) => ({
          tmdbId,
          action,
          createdAtUtc,
          details: null as MovieDetailsDto | null,
        }))
        .sort(
          (a, b) =>
            new Date(b.createdAtUtc).getTime() - new Date(a.createdAtUtc).getTime()
        )

      const tmdbIds = sorted.map((e) => e.tmdbId)
      const detailsMap = await fetchDetailsBatched(tmdbIds, DETAIL_BATCH_SIZE)

      setEntries(
        sorted.map((e) => ({
          ...e,
          details: detailsMap.get(e.tmdbId) ?? null,
        }))
      )
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load likes')
      setEntries([])
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    loadLikes()
  }, [loadLikes])

  return (
    <div className="min-h-screen bg-gradient-to-br from-slate-900 via-purple-900 to-slate-900">
      <div className="container mx-auto px-4 py-8">
        <header className="mb-8 flex flex-wrap items-center justify-between gap-4">
          <div className="flex items-center gap-4">
            <button
              type="button"
              onClick={() => navigate('/')}
              className="rounded-xl border border-pink-500/40 bg-slate-800/90 px-4 py-2 text-gray-200 transition hover:bg-slate-700/90 focus:outline-none focus:ring-2 focus:ring-pink-400"
            >
              ← Back
            </button>
            <h1 className="bg-gradient-to-r from-pink-400 to-purple-400 bg-clip-text text-2xl font-bold text-transparent">
              My Likes
            </h1>
          </div>
          <div className="flex gap-2">
            <button
              type="button"
              onClick={() => setViewMode('gallery')}
              className={`rounded-xl px-4 py-2 text-sm font-medium transition focus:outline-none focus:ring-2 focus:ring-pink-400 ${
                viewMode === 'gallery'
                  ? 'bg-pink-500/30 text-pink-200 ring-1 ring-pink-400/50'
                  : 'bg-slate-800/90 text-gray-300 hover:bg-slate-700/90'
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
                  : 'bg-slate-800/90 text-gray-300 hover:bg-slate-700/90'
              }`}
            >
              Table
            </button>
          </div>
        </header>

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
              onClick={loadLikes}
              className="mt-4 rounded-xl bg-pink-500/30 px-4 py-2 text-pink-200 hover:bg-pink-500/50"
            >
              Retry
            </button>
          </div>
        )}

        {!loading && !error && entries.length === 0 && (
          <div className="rounded-2xl border border-pink-500/30 bg-slate-800/90 p-12 text-center text-gray-400">
            <p className="text-lg">No likes or superlikes yet.</p>
            <p className="mt-2 text-sm">Swipe right on movies to add them here.</p>
            <button
              type="button"
              onClick={() => navigate('/')}
              className="mt-6 rounded-xl bg-pink-500/30 px-4 py-2 text-pink-200 hover:bg-pink-500/50"
            >
              Go to Swipe
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
                  <span
                    className={`absolute right-2 top-2 rounded-lg px-2 py-0.5 text-xs font-semibold ${
                      entry.action === 'Superlike'
                        ? 'bg-blue-500/80 text-white'
                        : 'bg-green-500/80 text-white'
                    }`}
                  >
                    {entry.action === 'Superlike' ? 'Superlike' : 'Like'}
                  </span>
                </div>
                <div className="p-3">
                  <p className="truncate font-medium text-gray-200" title={entry.details?.title ?? String(entry.tmdbId)}>
                    {entry.details?.title ?? `Movie ${entry.tmdbId}`}
                  </p>
                  <p className="mt-1 text-xs text-gray-500">
                    {entry.details?.releaseYear ?? '—'} · {formatLikedDate(entry.createdAtUtc)}
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
                    <th className="px-4 py-3 font-medium text-gray-300">Poster</th>
                    <th className="px-4 py-3 font-medium text-gray-300">Title</th>
                    <th className="px-4 py-3 font-medium text-gray-300">Year</th>
                    <th className="px-4 py-3 font-medium text-gray-300">Rating</th>
                    <th className="px-4 py-3 font-medium text-gray-300">Type</th>
                    <th className="px-4 py-3 font-medium text-gray-300">Liked</th>
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
                      <td className="px-4 py-2">
                        <span
                          className={`inline-block rounded px-2 py-0.5 text-xs font-semibold ${
                            entry.action === 'Superlike'
                              ? 'bg-blue-500/80 text-white'
                              : 'bg-green-500/80 text-white'
                          }`}
                        >
                          {entry.action}
                        </span>
                      </td>
                      <td className="px-4 py-2 text-gray-500">
                        {formatLikedDate(entry.createdAtUtc)}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        )}

        <footer className="mt-8 flex justify-center">
          <TmdbAttribution compact />
        </footer>
      </div>
    </div>
  )
}
