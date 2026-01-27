import { useEffect, useMemo, useState } from "react";
import { useLocation, useNavigate, type Location } from "react-router-dom";
import { ApiError } from "../api/http";
import { fetchMatches, fetchMovieDetails } from "../api/client";
import type { MovieDetailsDto } from "../api/contracts";
import MovieDetailsModal from "../components/MovieDetailsModal";

export default function MatchListPage() {
  const navigate = useNavigate();
  const location = useLocation();
  const state = location.state as { backgroundLocation?: Location } | null;
  const backgroundLocation = state?.backgroundLocation;

  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [tmdbIds, setTmdbIds] = useState<number[]>([]);
  const [selectedTmdbId, setSelectedTmdbId] = useState<number | null>(null);
  const [minUsers, setMinUsers] = useState(2);
  const [detailsByTmdbId, setDetailsByTmdbId] = useState<Record<number, MovieDetailsDto>>({});

  function handleClose() {
    if (backgroundLocation) {
      navigate(-1);
      return;
    }
    navigate("/swipe", { replace: true });
  }

  async function load() {
    try {
      setLoading(true);
      setError(null);
      const resp = await fetchMatches({ minUsers });
      setTmdbIds(resp.items.map((m) => m.tmdbId));
    } catch (err) {
      if (err instanceof ApiError) setError(err.message);
      else if (err instanceof Error) setError(err.message);
      else setError("Failed to load matches.");
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [minUsers]);

  useEffect(() => {
    const missing = tmdbIds.filter((id) => detailsByTmdbId[id] === undefined).slice(0, 40);
    if (missing.length === 0) return;

    let cancelled = false;
    (async () => {
      const concurrency = 6;
      for (let i = 0; i < missing.length; i += concurrency) {
        const batch = missing.slice(i, i + concurrency);
        const results = await Promise.allSettled(batch.map((id) => fetchMovieDetails(id)));
        if (cancelled) return;

        setDetailsByTmdbId((prev) => {
          const next = { ...prev };
          for (const r of results) {
            if (r.status === "fulfilled") {
              next[r.value.tmdbId] = r.value;
            }
          }
          return next;
        });
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [detailsByTmdbId, tmdbIds]);

  const countLabel = useMemo(() => (tmdbIds.length === 1 ? "1 match" : `${tmdbIds.length} matches`), [tmdbIds.length]);

  useEffect(() => {
    function handleKeyDown(event: KeyboardEvent) {
      if (event.key !== "Escape") return;
      if (selectedTmdbId) {
        setSelectedTmdbId(null);
        return;
      }
      handleClose();
    }

    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [backgroundLocation, selectedTmdbId]);

  return (
    <div className="modal" aria-hidden={false}>
      <div className="modal__backdrop" onClick={handleClose} />
      <div className="modal__panel" role="dialog" aria-modal="true" aria-label="Matches" onClick={(e) => e.stopPropagation()}>
        <div className="modal__header">
          <div>
            <h2 className="modal__title">Matches</h2>
            <p className="modal__subtitle">Movies liked by all (within the selected service scope).</p>
          </div>
          <button type="button" className="button button--ghost modal__close" onClick={handleClose}>
            Close
          </button>
        </div>

        <div className="modal__body">
          <div className="deck__toolbar" style={{ justifyContent: "space-between", flexWrap: "wrap" }}>
            <label className="field" style={{ flexDirection: "row", alignItems: "center", gap: "0.75rem" }}>
              <span className="field__label" style={{ margin: 0 }}>
                Min users
              </span>
              <select className="input" value={String(minUsers)} onChange={(e) => setMinUsers(Number(e.target.value))}>
                {Array.from({ length: 9 }, (_, i) => i + 2).map((n) => (
                  <option key={n} value={n}>
                    {n}+
                  </option>
                ))}
              </select>
            </label>
            <div style={{ color: "#8c93a6", fontWeight: 600 }}>{countLabel}</div>
          </div>

          {loading ? <div className="deck__state">Loading matches…</div> : null}
          {!loading && error ? <div className="deck__state deck__state--error">{error}</div> : null}

          {!loading && !error && tmdbIds.length === 0 ? <div className="deck__state">No matches yet.</div> : null}

          {!loading && !error && tmdbIds.length > 0 ? (
            <div className="list">
              {tmdbIds.map((id) => (
                <button key={id} className="listItem" type="button" onClick={() => setSelectedTmdbId(id)}>
                  {detailsByTmdbId[id]?.posterUrl ? (
                    <img className="listPoster" src={detailsByTmdbId[id].posterUrl!} alt={detailsByTmdbId[id].title} />
                  ) : (
                    <div className="listPoster listPoster--placeholder">No poster</div>
                  )}
                  <div className="listItem__main">
                    <div className="listItem__title">
                      {detailsByTmdbId[id]?.title ?? `TMDB #${id}`}{" "}
                      {detailsByTmdbId[id]?.releaseYear ? <span className="listItem__year">({detailsByTmdbId[id].releaseYear})</span> : null}
                    </div>
                    <div className="listItem__sub">
                      <span className="badge badge--neutral">Match</span>
                      {detailsByTmdbId[id]?.mpaaRating ? <span className="badge badge--neutral">{detailsByTmdbId[id].mpaaRating}</span> : null}
                    </div>
                  </div>
                  <div className="listItem__chev">›</div>
                </button>
              ))}
            </div>
          ) : null}
        </div>
      </div>

      {selectedTmdbId ? <MovieDetailsModal tmdbId={selectedTmdbId} onClose={() => setSelectedTmdbId(null)} /> : null}
    </div>
  );
}

