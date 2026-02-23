/**
 * TMDB attribution as required by their API terms of use.
 * @see https://www.themoviedb.org/about/logos-attribution
 * @see https://www.themoviedb.org/documentation/api/terms-of-use
 */
export default function TmdbAttribution({ compact = false }: { compact?: boolean }) {
  const className = compact
    ? 'text-xs text-gray-500'
    : 'text-sm text-gray-400'

  return (
    <p className={className}>
      This product uses the{' '}
      <a
        href="https://www.themoviedb.org/"
        target="_blank"
        rel="noopener noreferrer"
        className="text-blue-400 hover:underline"
      >
        TMDB API
      </a>
      {' '}but is not endorsed or certified by{' '}
      <a
        href="https://www.themoviedb.org/"
        target="_blank"
        rel="noopener noreferrer"
        className="text-blue-400 hover:underline"
      >
        TMDB
      </a>
      .
    </p>
  )
}
