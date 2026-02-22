import { useState, useEffect } from 'react'
import { useAuth } from '../contexts/AuthContext'
import { UserPreferences } from '../lib/api'

const GENRE_MAP = [
  { id: 28, name: 'Action' },
  { id: 12, name: 'Adventure' },
  { id: 16, name: 'Animation' },
  { id: 35, name: 'Comedy' },
  { id: 80, name: 'Crime' },
  { id: 99, name: 'Documentary' },
  { id: 18, name: 'Drama' },
  { id: 10751, name: 'Family' },
  { id: 14, name: 'Fantasy' },
  { id: 36, name: 'History' },
  { id: 27, name: 'Horror' },
  { id: 10402, name: 'Music' },
  { id: 9648, name: 'Mystery' },
  { id: 10749, name: 'Romance' },
  { id: 878, name: 'Sci-Fi' },
  { id: 10770, name: 'TV Movie' },
  { id: 53, name: 'Thriller' },
  { id: 10752, name: 'War' },
  { id: 37, name: 'Western' },
]

const LANGUAGES = [
  { code: 'en', name: 'English' },
  { code: 'es', name: 'Spanish' },
  { code: 'fr', name: 'French' },
  { code: 'de', name: 'German' },
  { code: 'it', name: 'Italian' },
  { code: 'ja', name: 'Japanese' },
  { code: 'ko', name: 'Korean' },
  { code: 'zh', name: 'Chinese' },
  { code: 'pt', name: 'Portuguese' },
  { code: 'ru', name: 'Russian' },
  { code: 'hi', name: 'Hindi' },
  { code: 'ar', name: 'Arabic' },
  { code: 'tr', name: 'Turkish' },
  { code: 'pl', name: 'Polish' },
  { code: 'nl', name: 'Dutch' },
]

const REGIONS = [
  { code: 'US', name: 'United States' },
  { code: 'GB', name: 'United Kingdom' },
  { code: 'CA', name: 'Canada' },
  { code: 'AU', name: 'Australia' },
  { code: 'FR', name: 'France' },
  { code: 'DE', name: 'Germany' },
  { code: 'ES', name: 'Spain' },
  { code: 'IT', name: 'Italy' },
  { code: 'JP', name: 'Japan' },
  { code: 'KR', name: 'South Korea' },
  { code: 'CN', name: 'China' },
  { code: 'IN', name: 'India' },
  { code: 'BR', name: 'Brazil' },
  { code: 'MX', name: 'Mexico' },
  { code: 'RU', name: 'Russia' },
]

interface PreferencesModalProps {
  isOpen: boolean
  onClose: () => void
}

export default function PreferencesModal({ isOpen, onClose }: PreferencesModalProps) {
  const { user, updatePreferences } = useAuth()
  const [prefs, setPrefs] = useState<UserPreferences>(
    user?.preferences || {
      preferredGenres: [],
      excludedGenres: [],
      minRating: 0,
      maxRating: 10,
      minYear: undefined,
      maxYear: undefined,
      includeAdult: false,
      sortBy: 'popularity.desc',
      language: 'en-US',
      region: undefined,
      originalLanguage: undefined,
    }
  )
  const [saving, setSaving] = useState(false)

  // Update preferences when modal opens or user changes
  useEffect(() => {
    if (isOpen && user) {
      setPrefs(user.preferences)
    }
  }, [isOpen, user])

  const handleSave = async () => {
    try {
      setSaving(true)
      console.log('Saving preferences:', prefs)
      await updatePreferences(prefs)
      console.log('Preferences saved successfully')
      onClose()
      // Small delay to show modal closing, then reload to fetch new movies
      setTimeout(() => {
        window.location.reload()
      }, 200)
    } catch (error) {
      console.error('Failed to save preferences:', error)
      alert(`Failed to save preferences: ${error instanceof Error ? error.message : 'Unknown error'}`)
      setSaving(false)
    }
  }

  const toggleGenre = (genreId: number, type: 'preferred' | 'excluded') => {
    const key = type === 'preferred' ? 'preferredGenres' : 'excludedGenres'
    const otherKey = type === 'preferred' ? 'excludedGenres' : 'preferredGenres'

    setPrefs((prev) => ({
      ...prev,
      [key]: prev[key].includes(genreId)
        ? prev[key].filter((id) => id !== genreId)
        : [...prev[key], genreId],
      [otherKey]: prev[otherKey].filter((id) => id !== genreId),
    }))
  }

  if (!isOpen) return null

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 p-4 backdrop-blur-sm">
      <div className="max-h-[90vh] w-full max-w-2xl overflow-y-auto rounded-2xl bg-slate-800 p-6 shadow-2xl">
        <div className="mb-6 flex items-center justify-between">
          <h2 className="text-2xl font-bold text-white">Movie Preferences</h2>
          <button
            onClick={onClose}
            className="text-2xl text-gray-400 transition-colors hover:text-white"
          >
            ✕
          </button>
        </div>

        <div className="space-y-6">
          {/* Genres */}
          <div>
            <h3 className="mb-3 text-lg font-semibold text-white">Genres</h3>
            <div className="grid grid-cols-2 gap-2 sm:grid-cols-3">
              {GENRE_MAP.map((genre) => (
                <button
                  key={genre.id}
                  onClick={() => toggleGenre(genre.id, 'preferred')}
                  className={`rounded-lg px-3 py-2 text-sm font-medium transition-colors ${
                    prefs.preferredGenres.includes(genre.id)
                      ? 'bg-green-500 text-white'
                      : prefs.excludedGenres.includes(genre.id)
                      ? 'bg-red-500/20 text-red-400 line-through'
                      : 'bg-slate-700 text-gray-300 hover:bg-slate-600'
                  }`}
                >
                  {genre.name}
                </button>
              ))}
            </div>
            <p className="mt-2 text-xs text-gray-400">Click to prefer genres</p>
          </div>

          {/* Rating Range */}
          <div>
            <h3 className="mb-3 text-lg font-semibold text-white">Rating Range</h3>
            <div className="flex items-center gap-4">
              <div className="flex-1">
                <label className="mb-1 block text-sm text-gray-400">Min Rating</label>
                <input
                  type="number"
                  min="0"
                  max="10"
                  step="0.5"
                  value={prefs.minRating}
                  onChange={(e) => setPrefs({ ...prefs, minRating: parseFloat(e.target.value) })}
                  className="w-full rounded-lg bg-slate-700 px-3 py-2 text-white"
                />
              </div>
              <div className="flex-1">
                <label className="mb-1 block text-sm text-gray-400">Max Rating</label>
                <input
                  type="number"
                  min="0"
                  max="10"
                  step="0.5"
                  value={prefs.maxRating}
                  onChange={(e) => setPrefs({ ...prefs, maxRating: parseFloat(e.target.value) })}
                  className="w-full rounded-lg bg-slate-700 px-3 py-2 text-white"
                />
              </div>
            </div>
          </div>

          {/* Year Range */}
          <div>
            <h3 className="mb-3 text-lg font-semibold text-white">Year Range</h3>
            <div className="flex items-center gap-4">
              <div className="flex-1">
                <label className="mb-1 block text-sm text-gray-400">From Year</label>
                <input
                  type="number"
                  min="1900"
                  max={new Date().getFullYear()}
                  value={prefs.minYear || ''}
                  onChange={(e) =>
                    setPrefs({ ...prefs, minYear: e.target.value ? parseInt(e.target.value) : undefined })
                  }
                  placeholder="Any"
                  className="w-full rounded-lg bg-slate-700 px-3 py-2 text-white"
                />
              </div>
              <div className="flex-1">
                <label className="mb-1 block text-sm text-gray-400">To Year</label>
                <input
                  type="number"
                  min="1900"
                  max={new Date().getFullYear()}
                  value={prefs.maxYear || ''}
                  onChange={(e) =>
                    setPrefs({ ...prefs, maxYear: e.target.value ? parseInt(e.target.value) : undefined })
                  }
                  placeholder="Any"
                  className="w-full rounded-lg bg-slate-700 px-3 py-2 text-white"
                />
              </div>
            </div>
          </div>

          {/* Sort By */}
          <div>
            <h3 className="mb-3 text-lg font-semibold text-white">Sort By</h3>
            <select
              value={prefs.sortBy}
              onChange={(e) => setPrefs({ ...prefs, sortBy: e.target.value })}
              className="w-full rounded-lg bg-slate-700 px-3 py-2 text-white"
            >
              <option value="popularity.desc">Most Popular</option>
              <option value="popularity.asc">Least Popular</option>
              <option value="vote_average.desc">Highest Rated</option>
              <option value="vote_average.asc">Lowest Rated</option>
              <option value="release_date.desc">Newest First</option>
              <option value="release_date.asc">Oldest First</option>
            </select>
          </div>

          {/* Original Language */}
          <div>
            <h3 className="mb-3 text-lg font-semibold text-white">Original Language</h3>
            <select
              value={prefs.originalLanguage || ''}
              onChange={(e) =>
                setPrefs({ ...prefs, originalLanguage: e.target.value || undefined })
              }
              className="w-full rounded-lg bg-slate-700 px-3 py-2 text-white"
            >
              <option value="">Any Language</option>
              {LANGUAGES.map((lang) => (
                <option key={lang.code} value={lang.code}>
                  {lang.name}
                </option>
              ))}
            </select>
            <p className="mt-1 text-xs text-gray-400">Filter movies by their original language</p>
          </div>

          {/* Release Region */}
          <div>
            <h3 className="mb-3 text-lg font-semibold text-white">Release Region</h3>
            <select
              value={prefs.region || ''}
              onChange={(e) => setPrefs({ ...prefs, region: e.target.value || undefined })}
              className="w-full rounded-lg bg-slate-700 px-3 py-2 text-white"
            >
              <option value="">Any Region</option>
              {REGIONS.map((region) => (
                <option key={region.code} value={region.code}>
                  {region.name}
                </option>
              ))}
            </select>
            <p className="mt-1 text-xs text-gray-400">Filter movies by release country</p>
          </div>
        </div>

        {/* Actions */}
        <div className="mt-8 flex gap-3">
          <button
            onClick={onClose}
            className="flex-1 rounded-lg border border-gray-600 px-6 py-3 font-semibold text-gray-300 transition-colors hover:bg-slate-700"
          >
            Cancel
          </button>
          <button
            onClick={handleSave}
            disabled={saving}
            className="flex-1 rounded-lg bg-gradient-to-r from-pink-500 to-purple-500 px-6 py-3 font-semibold text-white shadow-lg transition-transform hover:scale-105 disabled:opacity-50"
          >
            {saving ? 'Saving...' : 'Save Preferences'}
          </button>
        </div>
      </div>
    </div>
  )
}
