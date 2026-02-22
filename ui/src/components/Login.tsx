import { useState, useEffect } from 'react'
import { useAuth } from '../contexts/AuthContext'
import { apiClient } from '../lib/api'

export default function Login() {
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [newPassword, setNewPassword] = useState('')
  const [confirmPassword, setConfirmPassword] = useState('')
  const [needsPassword, setNeedsPassword] = useState(false)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [showCreateAccount, setShowCreateAccount] = useState(false)
  const [registerUserId, setRegisterUserId] = useState('')
  const [registerDisplayName, setRegisterDisplayName] = useState('')
  const [registerPassword, setRegisterPassword] = useState('')
  const [registerConfirmPassword, setRegisterConfirmPassword] = useState('')
  const [setupComplete, setSetupComplete] = useState<boolean | null>(null)
  const { login, register } = useAuth()

  useEffect(() => {
    if (showCreateAccount) {
      apiClient
        .getSetupStatus()
        .then((r) => setSetupComplete(r.setupComplete))
        .catch(() => setSetupComplete(null))
    }
  }, [showCreateAccount])

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()

    try {
      setLoading(true)
      setError(null)
      const result = await login(username.trim(), password.trim())
      if (result.needPassword) {
        setNeedsPassword(true)
        setError('Please set a new password')
      }
    } catch (err) {
      setError(
        'Failed to login. Please try again. Password may be required.'
      )
      console.error(err)
    } finally {
      setLoading(false)
    }
  }

  const handleSetPassword = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!username.trim() || !newPassword.trim()) {
      setError('New password is required')
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
        username: username.trim(),
        currentPassword: password.trim(),
        newPassword: newPassword.trim(),
      })
      await login(username.trim(), newPassword.trim())
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
      await register(userId, displayName, registerPassword)
    } catch (err) {
      setError(
        err instanceof Error ? err.message : 'Registration failed. Please try again.'
      )
      console.error(err)
    } finally {
      setLoading(false)
    }
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
              {setupComplete === false && (
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

              <button
                type="submit"
                disabled={loading}
                className="w-full rounded-lg bg-gradient-to-r from-pink-500 to-purple-500 px-6 py-3 font-semibold text-white shadow-lg transition-transform hover:scale-105 disabled:opacity-50 disabled:hover:scale-100"
              >
                {loading ? 'Creating account...' : 'Create account'}
              </button>
            </form>
            <p className="mt-6 text-center">
              <button
                type="button"
                onClick={() => {
                  setShowCreateAccount(false)
                  setError(null)
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
          </form>
        )}
      </div>
    </div>
  )
}
