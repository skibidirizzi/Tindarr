import { useState, useEffect, useCallback } from 'react'
import { useNavigate } from 'react-router-dom'
import { apiClient, type ServiceScopeOptionDto } from '../lib/api'

export default function Rooms() {
  const navigate = useNavigate()
  const [scopes, setScopes] = useState<ServiceScopeOptionDto[]>([])
  const [scopeIndex, setScopeIndex] = useState(0)
  const [customName, setCustomName] = useState('')
  const [useRandomName, setUseRandomName] = useState(true)
  const [creating, setCreating] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const loadScopes = useCallback(async () => {
    try {
      const list = await apiClient.getScopes()
      setScopes(list)
      if (list.length > 0 && scopeIndex >= list.length) setScopeIndex(0)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load media servers')
    }
  }, [scopeIndex])

  useEffect(() => {
    loadScopes()
  }, [loadScopes])

  const handleCreate = async () => {
    if (scopes.length === 0) return
    const scope = scopes[scopeIndex]
    setCreating(true)
    setError(null)
    try {
      const res = await apiClient.createRoom({
        serviceType: scope.serviceType,
        serverId: scope.serverId,
        roomName: useRandomName ? null : (customName.trim() || null),
      })
      navigate(`/rooms/${encodeURIComponent(res.roomId)}`, { replace: true })
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to create room')
    } finally {
      setCreating(false)
    }
  }

  return (
    <div className="min-h-screen bg-gradient-to-br from-slate-900 via-purple-900 to-slate-900">
      <div className="container mx-auto max-w-lg px-4 py-8">
        <h1 className="mb-8 bg-gradient-to-r from-pink-400 to-purple-400 bg-clip-text text-center text-3xl font-bold text-transparent">
          Create a room
        </h1>

        {error && (
          <div className="mb-6 rounded-xl border border-red-500/50 bg-red-500/10 px-4 py-3 text-red-200">
            {error}
          </div>
        )}

        <div className="space-y-6 rounded-2xl border border-pink-500/30 bg-slate-800/90 p-6 shadow-xl">
          <div>
            <label className="mb-2 block text-sm font-medium text-gray-300">
              Media server to swipe through
            </label>
            <select
              value={scopeIndex}
              onChange={(e) => setScopeIndex(Number(e.target.value))}
              className="w-full rounded-xl border border-pink-500/40 bg-slate-800 px-4 py-3 text-white focus:border-pink-400 focus:outline-none focus:ring-2 focus:ring-pink-400/50"
            >
              {scopes.map((s, i) => (
                <option key={`${s.serviceType}-${s.serverId}`} value={i}>
                  {s.displayName}
                </option>
              ))}
            </select>
          </div>

          <div>
            <p className="mb-3 text-sm font-medium text-gray-300">Room name</p>
            <label className="flex items-center gap-2 text-gray-200">
              <input
                type="radio"
                checked={useRandomName}
                onChange={() => setUseRandomName(true)}
                className="rounded border-pink-500 text-pink-500 focus:ring-pink-500"
              />
              Random name
            </label>
            <label className="mt-2 flex items-center gap-2 text-gray-200">
              <input
                type="radio"
                checked={!useRandomName}
                onChange={() => setUseRandomName(false)}
                className="rounded border-pink-500 text-pink-500 focus:ring-pink-500"
              />
              Custom name
            </label>
            {!useRandomName && (
              <input
                type="text"
                value={customName}
                onChange={(e) => setCustomName(e.target.value)}
                placeholder="e.g. movie-night"
                className="mt-2 w-full rounded-xl border border-pink-500/40 bg-slate-800 px-4 py-2 text-white placeholder-gray-500 focus:border-pink-400 focus:outline-none focus:ring-2 focus:ring-pink-400/50"
              />
            )}
          </div>

          <button
            type="button"
            onClick={handleCreate}
            disabled={creating || scopes.length === 0}
            className="w-full rounded-xl bg-gradient-to-r from-pink-500 to-rose-500 px-6 py-4 font-semibold text-white shadow-lg transition hover:from-pink-600 hover:to-rose-600 disabled:opacity-50"
          >
            {creating ? 'Creating…' : 'Create room'}
          </button>
        </div>

        <p className="mt-6 text-center text-sm text-gray-400">
          After creating, share the join QR so others can join. When everyone is in, close the room to start swiping.
        </p>
      </div>
    </div>
  )
}
