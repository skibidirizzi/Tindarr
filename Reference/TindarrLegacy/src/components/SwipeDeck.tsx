import { useState, useEffect } from 'react'
import { motion, PanInfo, useMotionValue, useTransform } from 'framer-motion'
import { tmdbClient } from '../lib/tmdb'
import type { BackendDiscoverResponse, BackendDiscoverMovie } from '../lib/api'
import { useAuth } from '../contexts/AuthContext'
import { apiClient } from '../lib/api'

interface Card {
  id: number
  title: string
  year: string
  rating: number
  posterUrl: string | null
  backdropUrl: string | null
  overview: string
}

const SwipeDeck = () => {
  const { user } = useAuth()
  const [cards, setCards] = useState<Card[]>([])
  const [exitX, setExitX] = useState(0)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [currentPage, setCurrentPage] = useState(1)
  const [isAnimating, setIsAnimating] = useState(false)

  // Fetch movies from TMDB with user preferences (20 per page)
  useEffect(() => {
    if (!user) return

    const fetchMovies = async () => {
      try {
        setLoading(true)
        // Fetch via backend proxy to ensure server-side noped filtering
        const response: BackendDiscoverResponse = await apiClient.discoverMovies(user.id, currentPage)

        console.log(`Loaded ${response.results.length} movies from page ${currentPage}`)

        const movieCards: Card[] = response.results.map((movie: BackendDiscoverMovie) => ({
          id: movie.id,
          title: movie.title ?? 'Untitled',
          year: movie.release_date ? new Date(movie.release_date).getFullYear().toString() : 'N/A',
          rating: Math.round((movie.vote_average ?? 0) * 10) / 10,
          posterUrl: tmdbClient.getPosterUrl(movie.poster_path ?? null, 'w500'),
          backdropUrl: tmdbClient.getBackdropUrl(movie.backdrop_path ?? null, 'w1280'),
          overview: movie.overview ?? 'No description available.',
        }))

        // Filter out duplicates already in deck (server already filters noped)
        const currentIds = new Set(cards.map((c) => c.id))
        const filtered = movieCards.filter((c) => !currentIds.has(c.id))

        // Append new movies, keeping buffer under 25 cards
        setCards((prev) => {
          const combined = [...prev, ...filtered]
          return combined.slice(0, 25)
        })
        setError(null)
      } catch (err) {
        setError(err instanceof Error ? err.message : 'Failed to fetch movies')
        console.error('Error fetching movies:', err)
      } finally {
        setLoading(false)
      }
    }

    fetchMovies()
  }, [currentPage, user])

  // Reset deck and FIRST load noped movies when user changes
  useEffect(() => {
    if (!user?.id) {
      setCards([])
      setCurrentPage(1)
      setError(null)
      return
    }

    // Load excluded (noped) movie IDs FIRST before fetching discover
    apiClient
      .getUserNopedMovies(user.id)
      .then(({ movieIds }) => {
        console.log(`Loaded ${movieIds.length} noped movies`)
        // Reset page to trigger discover fetch with noped movies cached on backend
        setCards([])
        setCurrentPage(1)
      })
      .catch((err) => {
        console.error('Failed to load noped movies:', err)
        // Still reset deck even if nope list fails to load
        setCards([])
        setCurrentPage(1)
      })
  }, [user?.id])

  // Load next page when down to 8 cards (keep ~20 cards buffered)
  useEffect(() => {
    if (cards.length <= 8 && cards.length > 0 && !loading) {
      console.log(`Low on cards (${cards.length}), fetching next page...`)
      setCurrentPage((prev) => prev + 1)
    }
  }, [cards.length, loading])

  const handleDragEnd = (_: MouseEvent | TouchEvent | PointerEvent, info: PanInfo) => {
    if (!user || isAnimating) return

    const threshold = 150
    
    if (Math.abs(info.offset.x) > threshold) {
      setIsAnimating(true)
      
      const direction = info.offset.x > 0 ? 'right' : 'left'
      const action = direction === 'right' ? '❤️ LIKE' : '👎 NOPE'
      const interactionType = direction === 'right' ? 'Like' : 'Nope'
      
      console.log(`${action}:`, cards[0].title)

      // Record interaction
      apiClient.recordInteraction({
        userId: user.id,
        movieId: cards[0].id,
        type: interactionType as 'Like' | 'Nope',
      }).catch(err => console.error('Failed to record interaction:', err))
      
      setExitX(info.offset.x)
      
      setTimeout(() => {
        setCards((prev) => prev.slice(1))
        setExitX(0)
        setIsAnimating(false)
      }, 300)
    }
  }

  const handleButtonAction = (type: 'Like' | 'Nope') => {
    if (!user || cards.length === 0 || isAnimating) return

    setIsAnimating(true)

    const action = type === 'Like' ? '❤️ LIKE' : '👎 NOPE'
    console.log(`${action}:`, cards[0].title)

    // Record interaction
    apiClient.recordInteraction({
      userId: user.id,
      movieId: cards[0].id,
      type,
    }).catch(err => console.error('Failed to record interaction:', err))

    setExitX(type === 'Like' ? 1000 : -1000)
    setTimeout(() => {
      setCards((prev) => prev.slice(1))
      setExitX(0)
      setIsAnimating(false)
    }, 300)
  }

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
            onClick={() => {
              setError(null)
              setCurrentPage(1)
            }}
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
          <p className="text-2xl text-gray-400">Loading next page...</p>
          <button
            onClick={() => {
              console.log('Manually requesting next page')
              setCurrentPage((prev) => prev + 1)
            }}
            className="mt-4 rounded-full bg-gradient-to-r from-pink-500 to-rose-500 px-6 py-3 font-semibold text-white shadow-lg transition-transform hover:scale-105"
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
          <Card
            key={card.id}
            card={card}
            index={index}
            totalCards={Math.min(cards.length, 3)}
            active={index === 0 && !isAnimating}
            exitX={exitX}
            onDragEnd={handleDragEnd}
          />
        ))}
      </div>

      {/* Action Buttons */}
      <div className="mt-8 flex justify-center gap-6">
        <button
          onClick={() => handleButtonAction('Nope')}
          disabled={isAnimating}
          className="flex h-16 w-16 items-center justify-center rounded-full border-4 border-red-500 bg-white text-3xl shadow-lg transition-transform hover:scale-110 active:scale-95 disabled:opacity-50 disabled:hover:scale-100"
          title="Nope"
        >
          ✕
        </button>
        
        <button
          onClick={() => handleButtonAction('Like')}
          disabled={isAnimating}
          className="flex h-16 w-16 items-center justify-center rounded-full border-4 border-green-500 bg-white text-3xl shadow-lg transition-transform hover:scale-110 active:scale-95 disabled:opacity-50 disabled:hover:scale-100"
          title="Like"
        >
          ❤️
        </button>
      </div>
    </div>
  )
}

interface CardProps {
  card: Card
  index: number
  totalCards: number
  active: boolean
  exitX: number
  onDragEnd: (event: MouseEvent | TouchEvent | PointerEvent, info: PanInfo) => void
}

const Card = ({ card, index, totalCards, active, exitX, onDragEnd }: CardProps) => {
  const x = useMotionValue(0)
  const rotate = useTransform(x, [-200, 200], [-25, 25])
  const opacity = useTransform(x, [-200, -100, 0, 100, 200], [0, 1, 1, 1, 0])

  // Visual feedback overlays
  const likeOpacity = useTransform(x, [0, 150], [0, 1])
  const nopeOpacity = useTransform(x, [-150, 0], [1, 0])

  // Fallback gradient if no backdrop
  const fallbackGradient = `bg-gradient-to-br from-${['pink', 'purple', 'blue', 'green', 'orange'][index % 5]}-500 to-${['rose', 'indigo', 'cyan', 'emerald', 'yellow'][index % 5]}-500`

  return (
    <motion.div
      className="absolute flex h-[550px] w-full max-w-md cursor-grab flex-col justify-end overflow-hidden rounded-3xl shadow-2xl active:cursor-grabbing"
      style={{
        x: active ? x : 0,
        rotate: active ? rotate : 0,
        opacity: active ? opacity : 1 - index * 0.1,
        zIndex: totalCards - index,
        scale: 1 - index * 0.05,
        backgroundImage: card.backdropUrl 
          ? `url(${card.backdropUrl})` 
          : undefined,
        backgroundSize: 'cover',
        backgroundPosition: 'center',
      }}
      animate={
        active && exitX !== 0
          ? { x: exitX > 0 ? 1000 : -1000, opacity: 0, rotate: exitX > 0 ? 45 : -45 }
          : { x: 0, y: index * -10 }
      }
      drag={active ? 'x' : false}
      dragConstraints={{ left: 0, right: 0 }}
      onDragEnd={active ? onDragEnd : undefined}
      transition={{
        type: 'spring',
        stiffness: 300,
        damping: 30,
      }}
    >
      {/* Fallback gradient background if no backdrop */}
      {!card.backdropUrl && (
        <div className={`absolute inset-0 ${fallbackGradient}`} />
      )}

      {/* NOPE Overlay */}
      {active && (
        <motion.div
          className="absolute left-8 top-8 z-10 rotate-[-20deg] rounded-lg border-4 border-red-500 bg-transparent px-4 py-2"
          style={{ opacity: nopeOpacity }}
        >
          <span className="text-5xl font-bold text-red-500">NOPE</span>
        </motion.div>
      )}

      {/* LIKE Overlay */}
      {active && (
        <motion.div
          className="absolute right-8 top-8 z-10 rotate-[20deg] rounded-lg border-4 border-green-500 bg-transparent px-4 py-2"
          style={{ opacity: likeOpacity }}
        >
          <span className="text-5xl font-bold text-green-500">LIKE</span>
        </motion.div>
      )}

      {/* Card Content */}
      <div className="relative z-20 bg-gradient-to-t from-black via-black/80 to-transparent p-6 text-left">
        <div className="mb-2 flex items-center gap-2">
          <span className="rounded-full bg-yellow-500 px-2 py-1 text-xs font-bold text-black">
            ⭐ {card.rating}
          </span>
          <span className="text-sm text-white/70">{card.year}</span>
        </div>
        <h2 className="mb-2 text-3xl font-bold text-white">{card.title}</h2>
        <p className="line-clamp-3 text-sm text-white/90">{card.overview}</p>
      </div>
    </motion.div>
  )
}

export default SwipeDeck
