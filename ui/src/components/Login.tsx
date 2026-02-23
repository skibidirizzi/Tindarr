import { useState, useEffect } from 'react'
import { useLocation } from 'react-router-dom'
import { useAuth } from '../contexts/AuthContext'
import { apiClient } from '../lib/api'
import SetupWizard from './SetupWizard'

const SETUP_WIZARD_KEY = 'tindarr_setup_wizard'

export default function Login() {
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [newPassword, setNewPassword] = useState('')
  const [confirmPassword, setConfirmPassword] = useState('')
  const [needsPassword, setNeedsPassword] = useState(false)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [successMessage, setSuccessMessage] = useState<string | null>(null)
  const [showCreateAccount, setShowCreateAccount] = useState(false)
  const [registerUserId, setRegisterUserId] = useState('')
  const [registerDisplayName, setRegisterDisplayName] = useState('')
  const [registerPassword, setRegisterPassword] = useState('')
  const [registerConfirmPassword, setRegisterConfirmPassword] = useState('')
  const [setupComplete, setSetupComplete] = useState<boolean | null>(null)
  const { login, register, guestLogin, setSessionFromAuthResponse } = useAuth()
  // Derive room ID from URL so "Continue as guest" shows when joining a room. Use window as source of truth
  // (path, then query ?joinRoom= / ?room=, then hash #/rooms/xyz) so it works even if router location lags.
  const [joinRoomId, setJoinRoomId] = useState<string | null>(() => getJoinRoomIdFromWindow())
  function getJoinRoomIdFromWindow(): string | null {
    if (typeof window === 'undefined') return null
    const pathname = window.location.pathname || ''
    const pathMatch = pathname.match(/\/rooms\/([^/]+)/)
    if (pathMatch) return pathMatch[1]
    const params = new URLSearchParams(window.location.search)
    const fromQuery = params.get('joinRoom') || params.get('room')
    if (fromQuery) return fromQuery
    const hash = window.location.hash || ''
    const hashPathMatch = hash.match(/\/rooms\/([^/]+)/)
    if (hashPathMatch) return hashPathMatch[1]
    const hashParams = new URLSearchParams(hash.replace(/^#/, '').split('?')[1] || '')
    const fromHash = hashParams.get('joinRoom') || hashParams.get('room')
    if (fromHash) return fromHash
    return null
  }
  // Recompute when location might change (e.g. after navigation)
  const location = useLocation()
  useEffect(() => {
    setJoinRoomId(getJoinRoomIdFromWindow())
  }, [location.pathname, location.search, location.hash])

  // Check for first-time setup on mount so we can show setup wizard or create-account when no users exist
  useEffect(() => {
    apiClient
      .getSetupStatus()
      .then((r) => setSetupComplete(r.setupComplete))
      .catch(() => setSetupComplete(null))
  }, [])

  // When setup is not complete, show create-account (for legacy flow) or full wizard
  useEffect(() => {
    if (setupComplete === false && !showCreateAccount) {
      setShowCreateAccount(true)
    }
  }, [setupComplete])

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    const uid = username.trim()
    if (uid && /[^a-zA-Z0-9_-]/.test(uid)) {
      setError('Username must contain only letters, digits, hyphens, and underscores')
      return
    }
    try {
      setLoading(true)
      setError(null)
      const result = await login(uid, password.trim())
      if (result.needPassword) {
        setNeedsPassword(true)
        setError('Please set a new password')
      }
    } catch (err) {
      setError(
        err instanceof Error ? err.message : 'Failed to login. Please try again.'
      )
      console.error(err)
    } finally {
      setLoading(false)
    }
  }

  const handleSetPassword = async (e: React.FormEvent) => {
    e.preventDefault()
    const uid = username.trim()
    if (!uid || !newPassword.trim()) {
      setError('New password is required')
      return
    }
    if (/[^a-zA-Z0-9_-]/.test(uid)) {
      setError('Username must contain only letters, digits, hyphens, and underscores')
      return
    }
    if (newPassword !== confirmPassword) {
      setError('Passwords do not match')
      return
    }
    try {
      setLoading(true)
      setError(null)
      await apiClient.setPassword({
        username: uid,
        currentPassword: password.trim(),
        newPassword: newPassword.trim(),
      })
      await login(uid, newPassword.trim())
      setNeedsPassword(false)
    } catch (err) {
      setError('Failed to set password. Please try again.')
      console.error(err)
    } finally {
      setLoading(false)
    }
  }

  const handleRegister = async (e: React.FormEvent) => {
    e.preventDefault()
    const userId = registerUserId.trim().toLowerCase()
    const displayName = registerDisplayName.trim() || registerUserId.trim()
    if (!userId) {
      setError('Username is required')
      return
    }
    if (userId.includes(' ')) {
      setError('Username must not contain spaces')
      return
    }
    if (/[^a-zA-Z0-9_-]/.test(userId)) {
      setError('Username must contain only letters, digits, hyphens, and underscores')
      return
    }
    if (!registerPassword) {
      setError('Password is required')
      return
    }
    if (registerPassword !== registerConfirmPassword) {
      setError('Passwords do not match')
      return
    }
    try {
      setLoading(true)
      setError(null)
      setSuccessMessage(null)
      const result = await register(userId, displayName, registerPassword)
      if (result?.pendingApproval) {
        setSuccessMessage('Account created. An admin must approve your account before you can log in.')
      }
    } catch (err) {
      setError(
        err instanceof Error ? err.message : 'Registration failed. Please try again.'
      )
      console.error(err)
    } finally {
      setLoading(false)
    }
  }

  if (setupComplete === false) {
    const joinRoomIdForWizard = getJoinRoomIdFromWindow() || joinRoomId
    return (
      <div className="flex min-h-screen items-center justify-center bg-gradient-to-br from-slate-900 via-purple-900 to-slate-900">
        <SetupWizard
          joinRoomId={joinRoomIdForWizard}
          onGuestJoin={joinRoomIdForWizard ? () => guestLogin(joinRoomIdForWizard) : undefined}
          onAdminCreated={async (resp) => {
            await setSessionFromAuthResponse(resp)
            try {
              sessionStorage.setItem(SETUP_WIZARD_KEY, 'active')
            } catch {
              // ignore
            }
          }}
          onFinish={() => {}}
        />
      </div>
    )
  }

  return (
    <div className="flex min-h-screen items-center justify-center bg-gradient-to-br from-slate-900 via-purple-900 to-slate-900">
      <div className="w-full max-w-md rounded-2xl bg-white/10 p-8 backdrop-blur-lg">
        <div className="mb-8 text-center">
          <h1 className="mb-2 bg-gradient-to-r from-pink-400 to-purple-400 bg-clip-text text-4xl font-bold text-transparent">
            Tindarr
          </h1>
          <p className="text-gray-300">Swipe movies, find favorites</p>
        </div>

        {!needsPassword && !showCreateAccount ? (
          <>
            <form onSubmit={handleSubmit} className="space-y-6">
              <div>
                <label
                  htmlFor="username"
                  className="mb-2 block text-sm font-medium text-gray-200"
                >
                  Username
                </label>
                <input
                  type="text"
                  id="username"
                  value={username}
                  onChange={(e) => setUsername(e.target.value)}
                  placeholder="Enter your username"
                  className="w-full rounded-lg border border-white/20 bg-white/5 px-4 py-3 text-white placeholder-gray-400 focus:border-pink-400 focus:outline-none focus:ring-2 focus:ring-pink-400/50"
                  disabled={loading}
                />
              </div>

              <div>
                <label
                  htmlFor="password"
                  className="mb-2 block text-sm font-medium text-gray-200"
                >
                  Password
                </label>
                <input
                  type="password"
                  id="password"
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  placeholder="Enter your password"
                  className="w-full rounded-lg border border-white/20 bg-white/5 px-4 py-3 text-white placeholder-gray-400 focus:border-pink-400 focus:outline-none focus:ring-2 focus:ring-pink-400/50"
                  disabled={loading}
                />
              </div>

              {error && (
                <div className="rounded-lg border border-red-500/20 bg-red-500/10 p-3 text-sm text-red-400">
                  {error}
                </div>
              )}

              <button
                type="submit"
                disabled={loading}
                className="w-full rounded-lg bg-gradient-to-r from-pink-500 to-purple-500 px-6 py-3 font-semibold text-white shadow-lg transition-transform hover:scale-105 disabled:opacity-50 disabled:hover:scale-100"
              >
                {loading ? 'Logging in...' : 'Continue'}
              </button>
            </form>
            <div className="mt-4 flex flex-col items-center gap-2">
              <button
                type="button"
                disabled={loading}
                onClick={async () => {
                  const roomId = getJoinRoomIdFromWindow() || joinRoomId
                  if (!roomId) {
                    setError('Open the room link from the QR code to join as guest (the URL should contain the room).')
                    return
                  }
                  try {
                    setLoading(true)
                    setError(null)
                    await guestLogin(roomId)
                  } catch (err) {
                    setError(err instanceof Error ? err.message : 'Could not join as guest.')
                  } finally {
                    setLoading(false)
                  }
                }}
                className="w-full rounded-lg border border-white/30 bg-white/10 px-6 py-3 font-medium text-white hover:bg-white/20 disabled:opacity-50"
              >
                {loading ? 'Joining…' : 'Continue as guest'}
              </button>
              <p className="text-xs text-gray-400">Join a room without an account</p>
            </div>
            <p className="mt-6 text-center">
              <button
                type="button"
                onClick={() => {
                  setShowCreateAccount(true)
                  setError(null)
                }}
                className="text-sm text-pink-300 underline hover:text-pink-200"
              >
                Create account
              </button>
            </p>
          </>
        ) : showCreateAccount ? (
          <>
            <form onSubmit={handleRegister} className="space-y-6">
              {setupComplete !== true && (
                <div className="rounded-lg border border-amber-500/30 bg-amber-500/10 p-3 text-sm text-amber-200">
                  You're the first user — you'll be an admin.
                </div>
              )}
              <div>
                <label
                  htmlFor="register-username"
                  className="mb-2 block text-sm font-medium text-gray-200"
                >
                  Username
                </label>
                <input
                  type="text"
                  id="register-username"
                  value={registerUserId}
                  onChange={(e) => setRegisterUserId(e.target.value)}
                  placeholder="Choose a username"
                  className="w-full rounded-lg border border-white/20 bg-white/5 px-4 py-3 text-white placeholder-gray-400 focus:border-pink-400 focus:outline-none focus:ring-2 focus:ring-pink-400/50"
                  disabled={loading}
                />
              </div>
              <div>
                <label
                  htmlFor="register-displayname"
                  className="mb-2 block text-sm font-medium text-gray-200"
                >
                  Display name (optional)
                </label>
                <input
                  type="text"
                  id="register-displayname"
                  value={registerDisplayName}
                  onChange={(e) => setRegisterDisplayName(e.target.value)}
                  placeholder="How you'll appear"
                  className="w-full rounded-lg border border-white/20 bg-white/5 px-4 py-3 text-white placeholder-gray-400 focus:border-pink-400 focus:outline-none focus:ring-2 focus:ring-pink-400/50"
                  disabled={loading}
                />
              </div>
              <div>
                <label
                  htmlFor="register-password"
                  className="mb-2 block text-sm font-medium text-gray-200"
                >
                  Password
                </label>
                <input
                  type="password"
                  id="register-password"
                  value={registerPassword}
                  onChange={(e) => setRegisterPassword(e.target.value)}
                  placeholder="Choose a password"
                  className="w-full rounded-lg border border-white/20 bg-white/5 px-4 py-3 text-white placeholder-gray-400 focus:border-pink-400 focus:outline-none focus:ring-2 focus:ring-pink-400/50"
                  disabled={loading}
                />
              </div>
              <div>
                <label
                  htmlFor="register-confirm"
                  className="mb-2 block text-sm font-medium text-gray-200"
                >
                  Confirm password
                </label>
                <input
                  type="password"
                  id="register-confirm"
                  value={registerConfirmPassword}
                  onChange={(e) => setRegisterConfirmPassword(e.target.value)}
                  placeholder="Confirm password"
                  className="w-full rounded-lg border border-white/20 bg-white/5 px-4 py-3 text-white placeholder-gray-400 focus:border-pink-400 focus:outline-none focus:ring-2 focus:ring-pink-400/50"
                  disabled={loading}
                />
              </div>

              {error && (
                <div className="rounded-lg border border-red-500/20 bg-red-500/10 p-3 text-sm text-red-400">
                  {error}
                </div>
              )}
              {successMessage && (
                <div className="rounded-lg border border-emerald-500/30 bg-emerald-500/10 p-3 text-sm text-emerald-300">
                  {successMessage}
                </div>
              )}

              <button
                type="submit"
                disabled={loading}
                className="w-full rounded-lg bg-gradient-to-r from-pink-500 to-purple-500 px-6 py-3 font-semibold text-white shadow-lg transition-transform hover:scale-105 disabled:opacity-50 disabled:hover:scale-100"
              >
                {loading ? 'Creating account...' : 'Create account'}
              </button>
            </form>
            <div className="mt-4 flex flex-col items-center gap-2">
              <button
                type="button"
                disabled={loading}
                onClick={async () => {
                  const roomId = getJoinRoomIdFromWindow() || joinRoomId
                  if (!roomId) {
                    setError('Open the room link from the QR code to join as guest (the URL should contain the room).')
                    return
                  }
                  try {
                    setLoading(true)
                    setError(null)
                    await guestLogin(roomId)
                  } catch (err) {
                    setError(err instanceof Error ? err.message : 'Could not join as guest.')
                  } finally {
                    setLoading(false)
                  }
                }}
                className="w-full rounded-lg border border-white/30 bg-white/10 px-6 py-3 font-medium text-white hover:bg-white/20 disabled:opacity-50"
              >
                {loading ? 'Joining…' : 'Continue as guest'}
              </button>
              <p className="text-xs text-gray-400">Join a room without an account</p>
            </div>
            <p className="mt-6 text-center">
              <button
                type="button"
                onClick={() => {
                  setShowCreateAccount(false)
                  setError(null)
                  setSuccessMessage(null)
                }}
                className="text-sm text-pink-300 underline hover:text-pink-200"
              >
                Back to login
              </button>
            </p>
          </>
        ) : (
          <form onSubmit={handleSetPassword} className="space-y-6">
            <div>
              <label className="mb-2 block text-sm font-medium text-gray-200">
                Set New Password
              </label>
              <input
                type="password"
                value={newPassword}
                onChange={(e) => setNewPassword(e.target.value)}
                placeholder="New password"
                className="w-full rounded-lg border border-white/20 bg-white/5 px-4 py-3 text-white placeholder-gray-400 focus:border-pink-400 focus:outline-none focus:ring-2 focus:ring-pink-400/50"
                disabled={loading}
              />
            </div>
            <div>
              <label className="mb-2 block text-sm font-medium text-gray-200">
                Confirm Password
              </label>
              <input
                type="password"
                value={confirmPassword}
                onChange={(e) => setConfirmPassword(e.target.value)}
                placeholder="Confirm password"
                className="w-full rounded-lg border border-white/20 bg-white/5 px-4 py-3 text-white placeholder-gray-400 focus:border-pink-400 focus:outline-none focus:ring-2 focus:ring-pink-400/50"
                disabled={loading}
              />
            </div>

            {error && (
              <div className="rounded-lg border border-red-500/20 bg-red-500/10 p-3 text-sm text-red-400">
                {error}
              </div>
            )}

            <button
              type="submit"
              disabled={loading}
              className="w-full rounded-lg bg-gradient-to-r from-pink-500 to-purple-500 px-6 py-3 font-semibold text-white shadow-lg transition-transform hover:scale-105 disabled:opacity-50 disabled:hover:scale-100"
            >
              {loading ? 'Saving...' : 'Set Password'}
            </button>
            <div className="mt-4 flex flex-col items-center gap-2">
              <button
                type="button"
                disabled={loading}
                onClick={async () => {
                  const roomId = getJoinRoomIdFromWindow() || joinRoomId
                  if (!roomId) {
                    setError('Open the room link from the QR code to join as guest (the URL should contain the room).')
                    return
                  }
                  try {
                    setLoading(true)
                    setError(null)
                    await guestLogin(roomId)
                  } catch (err) {
                    setError(err instanceof Error ? err.message : 'Could not join as guest.')
                  } finally {
                    setLoading(false)
                  }
                }}
                className="w-full rounded-lg border border-white/30 bg-white/10 px-6 py-3 font-medium text-white hover:bg-white/20 disabled:opacity-50"
              >
                {loading ? 'Joining…' : 'Continue as guest'}
              </button>
              <p className="text-xs text-gray-400">Join a room without an account</p>
            </div>
          </form>
        )}
      </div>
    </div>
  )
}
