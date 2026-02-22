import { useState, useEffect, useCallback } from 'react'
import { motion, type PanInfo, useMotionValue, useTransform } from 'framer-motion'
import { useAuth } from '../contexts/AuthContext'
import { apiClient, type SwipeCardDto } from '../lib/api'
import MovieDetailsModal from './MovieDetailsModal'

interface Card {
  id: number
  title: string
  year: string
  rating: number
  posterUrl: string | null
  backdropUrl: string | null
  overview: string
}

const FALLBACK_GRADIENTS = [
  'from-pink-500 to-rose-600',
  'from-purple-500 to-indigo-600',
  'from-blue-500 to-cyan-600',
  'from-emerald-500 to-teal-600',
  'from-amber-500 to-orange-600',
] as const

function swipeCardToCard(dto: SwipeCardDto): Card {
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

interface RoomSwipeDeckProps {
  roomId: string
  serviceType: string
  serverId: string
  /** When set, Cast in movie details is only shown for these tmdbIds (matched movies). */
  matchedTmdbIds?: number[] | null
}

// INV-0013: room mode disables superlikes (not compatible with shared selection rules).
const ROOM_SUPERLIKE_ENABLED = false

export default function RoomSwipeDeck({ roomId, serviceType, serverId, matchedTmdbIds }: RoomSwipeDeckProps) {
  const { user } = useAuth()
  const [cards, setCards] = useState<Card[]>([])
  const [exitX, setExitX] = useState(0)
  const [exitY, setExitY] = useState(0)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [isAnimating, setIsAnimating] = useState(false)
  const [detailsModalTmdbId, setDetailsModalTmdbId] = useState<number | null>(null)

  const fetchMoreCards = useCallback(
    async (append: boolean) => {
      if (!user) return
      try {
        if (!append) setLoading(true)
        const response = await apiClient.getRoomSwipeDeck(roomId, 25)
        const newCards = (response.items ?? []).map(swipeCardToCard)
        setCards((prev) => {
          if (!append) return newCards.slice(0, 25)
          const ids = new Set(prev.map((c) => c.id))
          const added = newCards.filter((c) => !ids.has(c.id))
          return [...prev, ...added].slice(0, 25)
        })
        setError(null)
      } catch (err) {
        setError(
          err instanceof Error ? err.message : 'Failed to fetch movies'
        )
      } finally {
        setLoading(false)
      }
    },
    [user, roomId]
  )

  useEffect(() => {
    if (!user) return
    fetchMoreCards(false)
  }, [user, fetchMoreCards])

  const handleDragEnd = (
    _: MouseEvent | TouchEvent | PointerEvent,
    info: PanInfo
  ) => {
    if (!user || isAnimating || cards.length === 0) return
    const threshold = 150
    const { offset } = info
    if (
      ROOM_SUPERLIKE_ENABLED &&
      offset.y < -threshold &&
      Math.abs(offset.y) >= Math.abs(offset.x)
    ) {
      setIsAnimating(true)
      const movieId = cards[0].id
      apiClient
        .roomSwipe(roomId, { tmdbId: movieId, action: 'Superlike' })
        .catch((err) => console.error('Failed to record interaction:', err))
      setExitY(-1000)
      setTimeout(() => {
        setCards((prev) => prev.slice(1))
        setExitX(0)
        setExitY(0)
        setIsAnimating(false)
      }, 300)
      return
    }
    if (Math.abs(offset.x) > threshold) {
      setIsAnimating(true)
      const action = offset.x > 0 ? 'Like' : 'Nope'
      const movieId = cards[0].id
      apiClient
        .roomSwipe(roomId, { tmdbId: movieId, action })
        .catch((err) => console.error('Failed to record interaction:', err))
      setExitX(offset.x)
      setTimeout(() => {
        setCards((prev) => prev.slice(1))
        setExitX(0)
        setExitY(0)
        setIsAnimating(false)
      }, 300)
    }
  }

  const handleButtonAction = (type: 'Like' | 'Nope' | 'Superlike') => {
    if (!user || cards.length === 0 || isAnimating) return
    if (type === 'Superlike' && !ROOM_SUPERLIKE_ENABLED) return
    setIsAnimating(true)
    const movieId = cards[0].id
    apiClient
      .roomSwipe(roomId, { tmdbId: movieId, action: type })
      .catch((err) => console.error('Failed to record interaction:', err))
    if (type === 'Superlike') setExitY(-1000)
    else setExitX(type === 'Nope' ? -1000 : 1000)
    setTimeout(() => {
      setCards((prev) => prev.slice(1))
      setExitX(0)
      setExitY(0)
      setIsAnimating(false)
    }, 300)
  }

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
    return (
      <div className="flex h-[600px] items-center justify-center">
        <div className="text-center">
          <p className="text-xl text-red-400">Error: {error}</p>
          <button
            onClick={() => { setError(null); fetchMoreCards(false) }}
            className="mt-4 rounded-full bg-gradient-to-r from-pink-500 to-rose-500 px-6 py-3 font-semibold text-white shadow-lg transition-transform hover:scale-105"
          >
            Try Again
          </button>
        </div>
      </div>
    )
  }

  if (cards.length === 0 && !loading) {
    return (
      <div className="flex h-[600px] items-center justify-center">
        <div className="text-center">
          <p className="text-2xl text-gray-400">No more cards.</p>
          <button
            onClick={() => fetchMoreCards(false)}
            className="mt-4 rounded-full bg-gradient-to-r from-pink-500 to-rose-500 px-6 py-3 font-semibold text-white shadow-lg"
          >
            Load More
          </button>
        </div>
      </div>
    )
  }

  return (
    <div className="relative mx-auto w-full max-w-md">
      <div className="relative flex h-[600px] items-center justify-center">
        {cards.slice(0, 3).map((card, index) => (
          <RoomCard
            key={card.id}
            card={card}
            index={index}
            totalCards={Math.min(cards.length, 3)}
            active={index === 0 && !isAnimating}
            exitX={exitX}
            exitY={exitY}
            canSuperlike={ROOM_SUPERLIKE_ENABLED}
            onDragEnd={handleDragEnd}
            onOpenDetails={index === 0 && !isAnimating ? () => setDetailsModalTmdbId(card.id) : undefined}
          />
        ))}
      </div>
      <MovieDetailsModal
        movie={null}
        tmdbId={detailsModalTmdbId}
        scope={{ serviceType, serverId }}
        matchedTmdbIds={matchedTmdbIds}
        onClose={() => setDetailsModalTmdbId(null)}
      />
      <div className="mt-8 flex justify-center items-center gap-1">
        <button
          onClick={() => handleButtonAction('Nope')}
          disabled={isAnimating}
          className="flex h-16 w-16 shrink-0 items-center justify-center rounded-full border-4 border-red-500 bg-white text-3xl text-red-500 shadow-lg transition hover:scale-110 active:scale-95 disabled:opacity-50"
          title="Nope"
        >
          ✕
        </button>
        <button
          onClick={() => handleButtonAction('Like')}
          disabled={isAnimating}
          className="flex h-16 w-16 shrink-0 items-center justify-center rounded-full border-4 border-green-500 bg-white text-3xl shadow-lg transition hover:scale-110 active:scale-95 disabled:opacity-50"
          title="Like"
        >
          ❤️
        </button>
        {ROOM_SUPERLIKE_ENABLED && (
          <button
            onClick={() => handleButtonAction('Superlike')}
            disabled={isAnimating}
            className="flex h-12 w-12 shrink-0 items-center justify-center rounded-full border-4 border-blue-500 bg-white text-2xl text-blue-500 shadow-lg transition hover:scale-110 active:scale-95 disabled:opacity-50"
            title="Superlike"
          >
            ⭐
          </button>
        )}
      </div>
    </div>
  )
}

interface RoomCardProps {
  card: Card
  index: number
  totalCards: number
  active: boolean
  exitX: number
  exitY: number
  canSuperlike: boolean
  onDragEnd: (e: MouseEvent | TouchEvent | PointerEvent, info: PanInfo) => void
  onOpenDetails?: () => void
}

function RoomCard({
  card,
  index,
  totalCards,
  active,
  exitX,
  exitY,
  canSuperlike,
  onDragEnd,
  onOpenDetails,
}: RoomCardProps) {
  const x = useMotionValue(0)
  const y = useMotionValue(0)
  const rotate = useTransform(x, [-200, 200], [-25, 25])
  const opacity = useTransform(x, [-200, -100, 0, 100, 200], [0, 1, 1, 1, 0])
  const likeOpacity = useTransform(x, [0, 150], [0, 1])
  const nopeOpacity = useTransform(x, [-150, 0], [1, 0])
  const superlikeOpacity = useTransform(y, [0, -150], [0, 1])
  const fallbackClass =
    FALLBACK_GRADIENTS[index % FALLBACK_GRADIENTS.length] ?? 'from-slate-500 to-slate-700'

  return (
    <motion.div
      className="absolute flex h-[550px] w-full max-w-md cursor-grab flex-col justify-end overflow-hidden rounded-3xl shadow-2xl active:cursor-grabbing"
      style={{
        x: active ? x : 0,
        y: active ? y : index * -10,
        rotate: active ? rotate : 0,
        opacity: active ? opacity : 1 - index * 0.1,
        zIndex: totalCards - index,
        scale: 1 - index * 0.05,
        backgroundImage: card.backdropUrl ? `url(${card.backdropUrl})` : undefined,
        backgroundSize: 'cover',
        backgroundPosition: 'center',
      }}
      animate={
        active && exitY !== 0
          ? { x: 0, y: -1000, opacity: 0 }
          : active && exitX !== 0
            ? { x: exitX > 0 ? 1000 : -1000, y: index * -10, opacity: 0, rotate: exitX > 0 ? 45 : -45 }
            : { x: 0, y: index * -10 }
      }
      drag={active}
      dragConstraints={{ left: 0, right: 0, top: -400, bottom: 0 }}
      onDragEnd={active ? onDragEnd : undefined}
      onTap={() => active && onOpenDetails?.()}
      transition={{ type: 'spring', stiffness: 300, damping: 30 }}
    >
      {!card.backdropUrl && <div className={`absolute inset-0 bg-gradient-to-br ${fallbackClass}`} />}
      {active && (
        <motion.div className="absolute left-8 top-8 z-10 rotate-[-20deg] rounded-lg border-4 border-red-500 bg-transparent px-4 py-2" style={{ opacity: nopeOpacity }}>
          <span className="text-5xl font-bold text-red-500">NOPE</span>
        </motion.div>
      )}
      {active && (
        <motion.div className="absolute right-8 top-8 z-10 rotate-[20deg] rounded-lg border-4 border-green-500 bg-transparent px-4 py-2" style={{ opacity: likeOpacity }}>
          <span className="text-5xl font-bold text-green-500">LIKE</span>
        </motion.div>
      )}
      {active && canSuperlike && (
        <motion.div className="absolute left-1/2 top-8 z-10 -translate-x-1/2 rounded-lg border-4 border-blue-500 bg-transparent px-4 py-2" style={{ opacity: superlikeOpacity }}>
          <span className="text-5xl font-bold text-blue-400">SUPERLIKE</span>
        </motion.div>
      )}
      <div className="relative z-20 bg-gradient-to-t from-black via-black/80 to-transparent p-6 text-left">
        <div className="mb-2 flex items-center gap-2">
          <span className="rounded-full bg-yellow-500 px-2 py-1 text-xs font-bold text-black">⭐ {card.rating}</span>
          <span className="text-sm text-white/70">{card.year}</span>
        </div>
        <h2 className="mb-2 text-3xl font-bold text-white">{card.title}</h2>
        <p className="line-clamp-3 text-sm text-white/90">{card.overview}</p>
      </div>
    </motion.div>
  )
}
