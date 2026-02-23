import { useState, useEffect, useCallback } from 'react'
import { useAuth } from '../contexts/AuthContext'
import { apiClient, type SwipeCardDto } from '../lib/api'
import SwipeCardStack, { type SwipeCard } from './SwipeCardStack'

/** Default scope for TMDB swipe deck (matches api/v1/scopes). */
const DEFAULT_SWIPE_SCOPE = { serviceType: 'tmdb', serverId: 'tmdb' } as const

function swipeCardToCard(dto: SwipeCardDto): SwipeCard {
  return {
    id: dto.tmdbId,
    title: dto.title || 'Untitled',
    year: dto.releaseYear != null ? String(dto.releaseYear) : 'N/A',
    rating: Math.round((dto.rating ?? 0) * 10) / 10,
    posterUrl: dto.posterUrl ?? null,
    backdropUrl: dto.backdropUrl ?? null,
    overview: dto.overview ?? 'No description available.',
  }
}

const SwipeDeck = () => {
  const { user } = useAuth()
  const [scope] = useState(DEFAULT_SWIPE_SCOPE)
  const [cards, setCards] = useState<SwipeCard[]>([])
  const [lastSwipedCard, setLastSwipedCard] = useState<SwipeCard | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [detailsModalTmdbId, setDetailsModalTmdbId] = useState<number | null>(null)

  const fetchMoreCards = useCallback(
    async (append: boolean) => {
      if (!user) return
      try {
        if (!append) setLoading(true)
        const response = await apiClient.getDiscoverCards(
          scope.serviceType,
          scope.serverId,
          25
        )
        const newCards = (response.items ?? []).map(swipeCardToCard)
        setCards((prev) => {
          if (!append) return newCards.slice(0, 25)
          const ids = new Set(prev.map((c) => c.id))
          const added = newCards.filter((c) => !ids.has(c.id))
          return [...prev, ...added].slice(0, 25)
        })
        setError(null)
      } catch (err) {
        const raw = err instanceof Error ? err.message : 'Failed to fetch movies'
        setError(
          /429|502|503|504|too many|rate limit|timeout/i.test(raw)
            ? "We're a bit busy — give it a moment and try again."
            : raw
        )
        console.error('Error fetching movies:', err)
      } finally {
        setLoading(false)
      }
    },
    [user, scope.serviceType, scope.serverId]
  )

  useEffect(() => {
    if (!user) return
    fetchMoreCards(false)
  }, [user, fetchMoreCards])

  useEffect(() => {
    if (!user?.id) {
      setCards([])
      setLastSwipedCard(null)
      setError(null)
    }
  }, [user?.id])

  useEffect(() => {
    if (cards.length <= 8 && cards.length > 0 && !loading) {
      fetchMoreCards(true)
    }
  }, [cards.length, loading, fetchMoreCards])

  if (loading && cards.length === 0) {
    return (
      <div className="flex h-[600px] items-center justify-center">
        <div className="text-center">
          <div className="mb-4 inline-block h-12 w-12 animate-spin rounded-full border-4 border-pink-500 border-t-transparent"></div>
          <p className="text-xl text-gray-300">Loading movies...</p>
        </div>
      </div>
    )
  }

  if (error && cards.length === 0) {
    const isRateLimit = error === "We're a bit busy — give it a moment and try again."
    return (
      <div className="flex h-[600px] items-center justify-center">
        <div className="text-center">
          <p className={`text-xl ${isRateLimit ? 'text-amber-400' : 'text-red-400'}`}>
            {isRateLimit ? error : `Error: ${error}`}
          </p>
          <button
            onClick={() => {
              setError(null)
              fetchMoreCards(false)
            }}
            className="mt-4 rounded-full bg-gradient-to-r from-pink-500 to-rose-500 px-6 py-3 font-semibold text-white shadow-lg transition-transform hover:scale-105"
          >
            Try Again
          </button>
        </div>
      </div>
    )
  }

  return (
    <SwipeCardStack
      cards={cards}
      setCards={setCards}
      lastSwipedCard={lastSwipedCard}
      setLastSwipedCard={setLastSwipedCard}
      scope={scope}
      user={user}
      emptyMessage="No more cards. Load more?"
      emptyAction={{ label: 'Load More', onClick: () => fetchMoreCards(false) }}
      detailsTmdbId={detailsModalTmdbId}
      setDetailsTmdbId={setDetailsModalTmdbId}
    />
  )
}

export default SwipeDeck
