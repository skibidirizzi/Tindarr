import { useCallback, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { apiClient, type SearchMovieResultDto } from '../lib/api'
import SwipeCardStack, { type SwipeCard } from '../components/SwipeCardStack'
import TmdbAttribution from '../components/TmdbAttribution'
import { useAuth } from '../contexts/AuthContext'

const DEFAULT_SCOPE = { serviceType: 'tmdb', serverId: 'tmdb' } as const

function searchResultToCard(m: SearchMovieResultDto): SwipeCard {
  return {
    id: m.tmdbId,
    title: m.title,
    year: m.releaseYear != null ? String(m.releaseYear) : 'N/A',
    rating: 0,
    posterUrl: m.posterUrl ?? null,
    backdropUrl: m.backdropUrl ?? null,
    overview: '',
  }
}

export default function Search() {
  const navigate = useNavigate()
  const { user } = useAuth()
  const [query, setQuery] = useState('')
  const [submittedQuery, setSubmittedQuery] = useState('')
  const [cards, setCards] = useState<SwipeCard[]>([])
  const [lastSwipedCard, setLastSwipedCard] = useState<SwipeCard | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [detailsTmdbId, setDetailsTmdbId] = useState<number | null>(null)
  const [lastSearchHadResults, setLastSearchHadResults] = useState(false)

  const handleSearch = useCallback(async () => {
    const q = query.trim()
    if (!q) {
      setCards([])
      setSubmittedQuery('')
      setLastSwipedCard(null)
      return
    }
    setLoading(true)
    setError(null)
    setSubmittedQuery(q)
    setCards([])
    setLastSwipedCard(null)
    try {
      const list = await apiClient.searchMovies(q)
      const newCards = list.map(searchResultToCard)
      setCards(newCards)
      setLastSearchHadResults(newCards.length > 0)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Search failed')
      setCards([])
      setLastSearchHadResults(false)
    } finally {
      setLoading(false)
    }
  }, [query])

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'Enter') handleSearch()
  }

  const hasSearched = submittedQuery.length > 0
  const noResults = hasSearched && !loading && !lastSearchHadResults && !error
  const showDeck = hasSearched && (lastSearchHadResults || loading)

  return (
    <div className="min-h-screen bg-gradient-to-br from-slate-900 via-purple-900 to-slate-900 px-4 py-8">
      <div className="mx-auto max-w-4xl">
        <button
          type="button"
          onClick={() => navigate('/')}
          className="mb-6 flex items-center gap-2 text-gray-400 hover:text-white transition-colors"
        >
          <svg className="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M10 19l-7-7m0 0l7-7m-7 7h18" />
          </svg>
          <span>Back to App</span>
        </button>

        <h1 className="text-3xl font-bold text-white mb-2">Search</h1>
        <p className="text-gray-400 text-sm mb-6">
          Search by movie title. Swipe to like, nope, or superlike. Uses Radarr when configured, otherwise local cache and TMDB.
        </p>

        <div className="flex flex-wrap gap-3 mb-6">
          <input
            type="search"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            onKeyDown={handleKeyDown}
            placeholder="Movie title..."
            className="flex-1 min-w-[200px] rounded-lg border border-gray-600 bg-slate-800/90 px-4 py-3 text-white placeholder-gray-500 focus:border-pink-500 focus:outline-none focus:ring-2 focus:ring-pink-500/50"
            aria-label="Search by title"
          />
          <button
            type="button"
            onClick={handleSearch}
            disabled={loading}
            className="rounded-lg bg-pink-600 px-6 py-3 font-medium text-white hover:bg-pink-500 disabled:opacity-50 disabled:cursor-not-allowed"
          >
            {loading ? 'Searching…' : 'Search'}
          </button>
        </div>

        {error && (
          <div className="rounded-lg bg-red-500/10 border border-red-500/30 text-red-400 px-4 py-3 mb-6">
            {error}
          </div>
        )}

        {noResults && (
          <p className="text-xl text-gray-400 py-8 text-center">
            No results for &quot;{submittedQuery}&quot;. Try another search.
          </p>
        )}

        {showDeck && (
          <>
            {loading ? (
              <div className="flex h-[600px] items-center justify-center">
                <div className="text-center">
                  <div className="mb-4 inline-block h-12 w-12 animate-spin rounded-full border-4 border-pink-500 border-t-transparent"></div>
                  <p className="text-xl text-gray-300">Searching…</p>
                </div>
              </div>
            ) : (
              <>
                {cards.length > 0 && (
                  <div className="mb-4 text-center text-gray-400 text-sm">
                    {cards.length} result{cards.length === 1 ? '' : 's'} for &quot;{submittedQuery}&quot;
                  </div>
                )}
                <SwipeCardStack
                cards={cards}
                setCards={setCards}
                lastSwipedCard={lastSwipedCard}
                setLastSwipedCard={setLastSwipedCard}
                scope={DEFAULT_SCOPE}
                user={user}
                emptyMessage="You've gone through all results for this search."
                emptyAction={{
                  label: 'Search again',
                  onClick: () => {
                    setCards([])
                    setLastSearchHadResults(false)
                  },
                }}
                detailsTmdbId={detailsTmdbId}
                setDetailsTmdbId={setDetailsTmdbId}
              />
              </>
            )}
          </>
        )}

        <footer className="mt-12 text-center">
          <TmdbAttribution compact />
        </footer>
      </div>
    </div>
  )
}
