import { useState, useEffect, useCallback, useRef } from 'react'
import { useParams, useNavigate } from 'react-router-dom'
import { QRCodeSVG } from 'qrcode.react'
import { useAuth } from '../contexts/AuthContext'
import {
  apiClient,
  type RoomStateResponse,
  type RoomJoinUrlResponse,
  type RoomMatchesResponse,
  type RoomMemberDto,
  type MovieDetailsDto,
} from '../lib/api'
import RoomSwipeDeck from '../components/RoomSwipeDeck'

const ROOM_POLL_MS = 3000
const MATCHES_POLL_MS = 5000

type JoinUrlVariant = 'lan' | 'wan'

export default function Room() {
  const { roomId } = useParams<{ roomId: string }>()
  const navigate = useNavigate()
  const { user } = useAuth()
  const [room, setRoom] = useState<RoomStateResponse | null>(null)
  const [joinUrl, setJoinUrl] = useState<RoomJoinUrlResponse | null>(null)
  const [joinUrlVariant, setJoinUrlVariant] = useState<JoinUrlVariant>('lan')
  const [matches, setMatches] = useState<RoomMatchesResponse | null>(null)
  const [matchDetails, setMatchDetails] = useState<Map<number, MovieDetailsDto | null>>(new Map())
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [closing, setClosing] = useState(false)
  const [castModalOpen, setCastModalOpen] = useState(false)
  const [castDevices, setCastDevices] = useState<{ id: string; name: string }[]>([])
  const [castVariant, setCastVariant] = useState<'lan' | 'wan'>('lan')
  const [castingTo, setCastingTo] = useState<string | null>(null)
  const [lastCastDeviceId, setLastCastDeviceId] = useState<string | null>(null)
  const [updatingCast, setUpdatingCast] = useState(false)
  const joinedOnce = useRef(false)

  const effectiveRoomId = roomId ?? ''

  const isOwner = user && room && room.ownerUserId === user.id

  const displayJoinUrl = ((): string | null => {
    if (!joinUrl) return null
    if (joinUrlVariant === 'lan' && joinUrl.lanUrl) return joinUrl.lanUrl
    if (joinUrlVariant === 'wan' && joinUrl.wanUrl) return joinUrl.wanUrl
    return joinUrl.url
  })()

  const loadRoom = useCallback(async () => {
    if (!effectiveRoomId) return
    try {
      const r = await apiClient.getRoom(effectiveRoomId)
      setRoom(r)
      setError(null)
      return r
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Room not found')
      setRoom(null)
      return null
    }
  }, [effectiveRoomId])

  const ensureJoined = useCallback(async () => {
    if (!user || !room || joinedOnce.current) return
    const alreadyMember = room.members.some((m) => m.userId === user.id)
    if (alreadyMember || room.isClosed) {
      joinedOnce.current = true
      return
    }
    try {
      const updated = await apiClient.joinRoom(effectiveRoomId)
      setRoom(updated)
      joinedOnce.current = true
    } catch {
      // ignore; may already be member or room closed
    }
  }, [user, room, effectiveRoomId])

  const loadJoinUrl = useCallback(async () => {
    if (!effectiveRoomId) return
    try {
      const res = await apiClient.getRoomJoinUrl(effectiveRoomId)
      setJoinUrl(res)
    } catch {
      setJoinUrl(null)
    }
  }, [effectiveRoomId])

  useEffect(() => {
    if (!effectiveRoomId || !user) {
      setLoading(false)
      return
    }
    setLoading(true)
    loadRoom().then((r) => {
      setLoading(false)
      if (r) loadJoinUrl()
    })
  }, [effectiveRoomId, user, loadRoom, loadJoinUrl])

  useEffect(() => {
    if (!room) return
    ensureJoined()
  }, [room, ensureJoined])

  const getMatchIds = useCallback((res: RoomMatchesResponse | null): number[] => {
    if (!res) return []
    const r = res as { tmdbIds?: number[]; TmdbIds?: number[] }
    return r.tmdbIds ?? r.TmdbIds ?? []
  }, [])

  useEffect(() => {
    if (!room || !room.isClosed) return
    const fetchMatches = async () => {
      try {
        const res = await apiClient.getRoomMatches(effectiveRoomId)
        setMatches(res)
      } catch {
        setMatches(null)
      }
    }
    fetchMatches()
    const t = setInterval(fetchMatches, MATCHES_POLL_MS)
    return () => clearInterval(t)
  }, [room?.isClosed, effectiveRoomId])

  useEffect(() => {
    if (!room) return
    const t = setInterval(loadRoom, ROOM_POLL_MS)
    return () => clearInterval(t)
  }, [loadRoom, room?.roomId])

  const handleCloseRoom = async () => {
    if (!effectiveRoomId || !isOwner) return
    setClosing(true)
    try {
      const updated = await apiClient.closeRoom(effectiveRoomId)
      setRoom(updated)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to close room')
    } finally {
      setClosing(false)
    }
  }

  const openCastModal = async () => {
    setCastModalOpen(true)
    setCastingTo(null)
    setUpdatingCast(false)
    try {
      const list = await apiClient.listCastDevices()
      setCastDevices(list.map((d) => ({ id: d.id, name: d.name })))
    } catch {
      setCastDevices([])
    }
  }

  const handleCastToDevice = async (deviceId: string) => {
    try {
      setCastingTo(deviceId)
      await apiClient.castRoomQr(effectiveRoomId, deviceId, castVariant)
      setLastCastDeviceId(deviceId)
      // Keep modal open so user can hotswap variant (LAN/WAN)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to cast')
    } finally {
      setCastingTo(null)
    }
  }

  const hotswapCast = async (variant: 'lan' | 'wan') => {
    if (!lastCastDeviceId) return
    try {
      setUpdatingCast(true)
      await apiClient.castRoomQr(effectiveRoomId, lastCastDeviceId, variant)
      setCastVariant(variant)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to update cast')
    } finally {
      setUpdatingCast(false)
    }
  }

  const closeCastModal = () => {
    setCastModalOpen(false)
    setLastCastDeviceId(null)
  }

  useEffect(() => {
    const ids = getMatchIds(matches ?? null)
    if (!ids.length) {
      setMatchDetails(new Map())
      return
    }
    const batch = 10
    const map = new Map<number, MovieDetailsDto | null>()
    let cancelled = false
    ;(async () => {
      for (let i = 0; i < ids.length && !cancelled; i += batch) {
        const chunk = ids.slice(i, i + batch)
        const results = await Promise.allSettled(
          chunk.map((id) => apiClient.getMovieDetails(id))
        )
        chunk.forEach((id, j) => {
          const r = results[j]
          map.set(id, r?.status === 'fulfilled' ? r.value : null)
        })
        if (!cancelled) setMatchDetails(new Map(map))
      }
    })()
    return () => {
      cancelled = true
    }
  }, [matches, getMatchIds])

  if (!effectiveRoomId) {
    navigate('/rooms', { replace: true })
    return null
  }

  if (loading && !room) {
    return (
      <div className="flex min-h-screen items-center justify-center bg-gradient-to-br from-slate-900 via-purple-900 to-slate-900">
        <div className="text-center">
          <div className="mb-4 inline-block h-12 w-12 animate-spin rounded-full border-4 border-pink-500 border-t-transparent"></div>
          <p className="text-xl text-gray-300">Loading room…</p>
        </div>
      </div>
    )
  }

  if (error && !room) {
    return (
      <div className="min-h-screen bg-gradient-to-br from-slate-900 via-purple-900 to-slate-900 px-4 py-8">
        <div className="mx-auto max-w-md rounded-2xl border border-red-500/50 bg-slate-800/90 p-6 text-center">
          <p className="text-red-300">{error}</p>
          <button
            type="button"
            onClick={() => navigate('/rooms')}
            className="mt-4 rounded-xl bg-pink-500/80 px-4 py-2 text-white hover:bg-pink-500"
          >
            Back to Rooms
          </button>
        </div>
      </div>
    )
  }

  if (!room) return null

  const showLobby = !room.isClosed
  const canToggleJoinUrl = Boolean(joinUrl?.lanUrl && joinUrl?.wanUrl)

  return (
    <div className="min-h-screen bg-gradient-to-br from-slate-900 via-purple-900 to-slate-900">
      <div className="container mx-auto max-w-2xl px-4 py-6">
        <div className="mb-4 flex flex-wrap items-center justify-between gap-2">
          <div className="flex gap-2">
            <button
              type="button"
              onClick={() => navigate('/')}
              className="rounded-xl border border-pink-500/40 bg-slate-800/90 px-3 py-2 text-sm text-gray-200 hover:bg-slate-700"
            >
              Home
            </button>
            <button
              type="button"
              onClick={() => navigate('/rooms')}
              className="rounded-xl border border-pink-500/40 bg-slate-800/90 px-3 py-2 text-sm text-gray-200 hover:bg-slate-700"
            >
              ← Rooms
            </button>
          </div>
          <h1 className="bg-gradient-to-r from-pink-400 to-purple-400 bg-clip-text text-xl font-bold text-transparent">
            Room: {room.roomId}
          </h1>
        </div>

        {error && (
          <div className="mb-4 rounded-xl border border-red-500/50 bg-red-500/10 px-4 py-2 text-red-200 text-sm">
            {error}
          </div>
        )}

        {showLobby && (
          <div className="mb-8 space-y-6 rounded-2xl border border-pink-500/30 bg-slate-800/90 p-6">
            {joinUrl && (
              <div>
                <p className="mb-2 text-sm font-medium text-gray-300">Join QR</p>
                {canToggleJoinUrl && (
                  <div className="mb-2 flex gap-2">
                    <button
                      type="button"
                      onClick={() => setJoinUrlVariant('lan')}
                      className={`rounded-lg px-3 py-1.5 text-sm ${joinUrlVariant === 'lan' ? 'bg-pink-500/50 text-white' : 'bg-slate-700 text-gray-300'}`}
                    >
                      LAN
                    </button>
                    <button
                      type="button"
                      onClick={() => setJoinUrlVariant('wan')}
                      className={`rounded-lg px-3 py-1.5 text-sm ${joinUrlVariant === 'wan' ? 'bg-pink-500/50 text-white' : 'bg-slate-700 text-gray-300'}`}
                    >
                      WAN
                    </button>
                  </div>
                )}
                {displayJoinUrl && (
                  <div className="inline-block rounded-xl bg-white p-3">
                    <QRCodeSVG value={displayJoinUrl} size={200} level="M" />
                  </div>
                )}
              </div>
            )}

            {room.members.length > 0 && (
              <div>
                <p className="mb-2 text-sm font-medium text-gray-300">Members ({room.members.length})</p>
                <ul className="flex flex-wrap gap-2">
                  {room.members.map((m: RoomMemberDto) => (
                    <li
                      key={m.userId}
                      className="rounded-lg bg-slate-700/80 px-3 py-1.5 text-sm text-gray-200"
                    >
                      {m.userId}
                    </li>
                  ))}
                </ul>
              </div>
            )}

            <div className="flex flex-wrap gap-3">
              {isOwner && (
                <>
                  <button
                    type="button"
                    onClick={handleCloseRoom}
                    disabled={closing}
                    className="rounded-xl bg-rose-600 px-4 py-2 font-medium text-white hover:bg-rose-700 disabled:opacity-50"
                  >
                    {closing ? 'Closing…' : 'Close room'}
                  </button>
                  <button
                    type="button"
                    onClick={openCastModal}
                    className="rounded-xl border border-pink-500/50 bg-pink-500/20 px-4 py-2 font-medium text-pink-200 hover:bg-pink-500/30"
                  >
                    Cast QR
                  </button>
                </>
              )}
            </div>
            <p className="text-xs text-gray-500">
              Scope: {room.serviceType} / {room.serverId}
            </p>
          </div>
        )}

        {room.isClosed && (
          <>
            <RoomSwipeDeck
              roomId={effectiveRoomId}
              serviceType={room.serviceType}
              serverId={room.serverId}
              matchedTmdbIds={getMatchIds(matches ?? null)}
            />
            <div className="mt-8">
              <h2 className="mb-4 bg-gradient-to-r from-pink-400 to-purple-400 bg-clip-text text-xl font-bold text-transparent">
                Matches (live)
              </h2>
              {matches && getMatchIds(matches).length > 0 ? (
                <ul className="space-y-2">
                  {getMatchIds(matches).map((tmdbId) => {
                    const details = matchDetails.get(tmdbId)
                    return (
                      <li
                        key={tmdbId}
                        className="flex items-center gap-3 rounded-xl border border-pink-500/20 bg-slate-800/80 p-3"
                      >
                        {details?.posterUrl ? (
                          <img
                            src={details.posterUrl}
                            alt=""
                            className="h-16 w-11 rounded object-cover"
                          />
                        ) : (
                          <div className="h-16 w-11 rounded bg-slate-700" />
                        )}
                        <div className="min-w-0 flex-1">
                          <p className="font-medium text-white truncate">
                            {details?.title ?? `TMDB ${tmdbId}`}
                          </p>
                          {details?.releaseYear && (
                            <p className="text-sm text-gray-400">{details.releaseYear}</p>
                          )}
                        </div>
                      </li>
                    )
                  })}
                </ul>
              ) : (
                <p className="text-gray-400">No matches yet. Keep swiping!</p>
              )}
            </div>
          </>
        )}
      </div>

      {castModalOpen && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4 backdrop-blur-sm">
          <div className="w-full max-w-md rounded-2xl border border-pink-500/30 bg-slate-800 p-6 shadow-xl">
            <h3 className="mb-4 text-lg font-semibold text-white">Cast room QR to device</h3>
            <div className="mb-4 flex gap-2">
              <button
                type="button"
                onClick={() => {
                  setCastVariant('lan')
                  if (lastCastDeviceId) hotswapCast('lan')
                }}
                disabled={updatingCast}
                className={`flex-1 rounded-lg py-2 text-sm font-medium ${castVariant === 'lan' ? 'bg-pink-500 text-white' : 'bg-slate-700 text-gray-300'} disabled:opacity-50`}
              >
                LAN
              </button>
              <button
                type="button"
                onClick={() => {
                  setCastVariant('wan')
                  if (lastCastDeviceId) hotswapCast('wan')
                }}
                disabled={updatingCast}
                className={`flex-1 rounded-lg py-2 text-sm font-medium ${castVariant === 'wan' ? 'bg-pink-500 text-white' : 'bg-slate-700 text-gray-300'} disabled:opacity-50`}
              >
                WAN
              </button>
            </div>
            {lastCastDeviceId && (
              <p className="mb-4 text-sm text-gray-400">
                Casting to:{' '}
                <span className="font-medium text-white">
                  {castDevices.find((d) => d.id === lastCastDeviceId)?.name ?? 'Device'}
                </span>
              </p>
            )}
            <ul className="mb-4 max-h-48 space-y-2 overflow-y-auto">
              {castDevices.length === 0 ? (
                <li className="text-gray-400 text-sm">No devices found.</li>
              ) : (
                castDevices.map((d) => (
                  <li key={d.id}>
                    <button
                      type="button"
                      onClick={() => handleCastToDevice(d.id)}
                      disabled={castingTo !== null}
                      className="w-full rounded-xl bg-slate-700 px-4 py-3 text-left text-white hover:bg-slate-600 disabled:opacity-50"
                    >
                      {d.name}
                    </button>
                  </li>
                ))
              )}
            </ul>
            <button
              type="button"
              onClick={closeCastModal}
              className="w-full rounded-xl border border-gray-600 bg-slate-700 py-2 text-gray-200 hover:bg-slate-600"
            >
              {lastCastDeviceId ? 'Done' : 'Cancel'}
            </button>
          </div>
        </div>
      )}
    </div>
  )
}
