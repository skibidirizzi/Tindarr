import { useEffect, useMemo, useState } from "react";
import { ApiError } from "../api/http";
import { fetchMovieDetails } from "../api/client";
import type { MovieDetailsDto } from "../api/contracts";

type MovieDetailsModalProps = {
  tmdbId: number;
  onClose: () => void;
};

export default function MovieDetailsModal({ tmdbId, onClose }: MovieDetailsModalProps) {
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [details, setDetails] = useState<MovieDetailsDto | null>(null);

  const posterUrl = details?.posterUrl ?? null;

  const subtitle = useMemo(() => {
    if (!details) return null;
    const parts: string[] = [];
    if (details.releaseYear) parts.push(String(details.releaseYear));
    if (details.mpaaRating) parts.push(details.mpaaRating);
    if (details.runtimeMinutes) parts.push(`${details.runtimeMinutes} min`);
    if (details.originalLanguage) parts.push(details.originalLanguage.toUpperCase());
    return parts.length ? parts.join(" • ") : null;
  }, [details]);

  useEffect(() => {
    (async () => {
      try {
        setLoading(true);
        setError(null);
        const d = await fetchMovieDetails(tmdbId);
        setDetails(d);
      } catch (err) {
        if (err instanceof ApiError) setError(err.message);
        else if (err instanceof Error) setError(err.message);
        else setError("Failed to load movie details.");
      } finally {
        setLoading(false);
      }
    })();
  }, [tmdbId]);

  useEffect(() => {
    function handleKeyDown(event: KeyboardEvent) {
      if (event.key === "Escape") {
        onClose();
      }
    }
    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [onClose]);

  return (
    <div className="modal modal--above" aria-hidden={false}>
      <div className="modal__backdrop" onClick={onClose} />
      <div className="modal__panel" role="dialog" aria-modal="true" aria-label="Movie details" onClick={(e) => e.stopPropagation()}>
        <div className="modal__header">
          <div style={{ minWidth: 0 }}>
            <h2 className="modal__title" style={{ whiteSpace: "nowrap", overflow: "hidden", textOverflow: "ellipsis" }}>
              {details?.title ?? `TMDB #${tmdbId}`}
            </h2>
            {subtitle ? <p className="modal__subtitle">{subtitle}</p> : null}
          </div>
          <button type="button" className="button button--ghost modal__close" onClick={onClose}>
            Close
          </button>
        </div>

        <div className="modal__body">
          {loading ? <div className="deck__state">Loading details…</div> : null}
          {!loading && error ? <div className="deck__state deck__state--error">{error}</div> : null}

          {!loading && !error && details ? (
            <div className="details">
              <div className="details__grid">
                {posterUrl ? (
                  <div className="details__poster">
                    <img src={posterUrl} alt={details.title} />
                  </div>
                ) : null}

                <div className="details__content">
                  {details.overview ? <p className="details__overview">{details.overview}</p> : <p className="details__overview">No overview.</p>}

                  <div className="details__meta">
                    <span>TMDB #{details.tmdbId}</span>
                    {details.rating ? <span>{details.rating.toFixed(1)} ★</span> : null}
                    {details.voteCount ? <span>{details.voteCount.toLocaleString()} votes</span> : null}
                  </div>

                  {details.genres?.length ? (
                    <div className="pickerRow" style={{ marginTop: "0.75rem" }}>
                      {details.genres.map((g) => (
                        <span key={g} className="pill pill--neutral is-on" style={{ cursor: "default" }}>
                          <span className="pill__label">{g}</span>
                        </span>
                      ))}
                    </div>
                  ) : null}
                </div>
              </div>
            </div>
          ) : null}
        </div>
      </div>
    </div>
  );
}

