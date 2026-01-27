import { useEffect, useMemo, useState } from "react";
import { useLocation, useNavigate, type Location } from "react-router-dom";
import { ApiError } from "../api/http";
import { fetchMovieDetails, listInteractions } from "../api/client";
import type { InteractionDto, MovieDetailsDto } from "../api/contracts";
import MovieDetailsModal from "../components/MovieDetailsModal";
import type { SwipeAction } from "../types";

type Filter = "Liked" | "Superliked" | "AllPositive";

export default function MyLikedMoviesPage() {
  const navigate = useNavigate();
  const location = useLocation();
  const state = location.state as { backgroundLocation?: Location } | null;
  const backgroundLocation = state?.backgroundLocation;

  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [items, setItems] = useState<InteractionDto[]>([]);
  const [filter, setFilter] = useState<Filter>("AllPositive");
  const [selectedTmdbId, setSelectedTmdbId] = useState<number | null>(null);
  const [detailsByTmdbId, setDetailsByTmdbId] = useState<Record<number, MovieDetailsDto>>({});

  function handleClose() {
    if (backgroundLocation) {
      navigate(-1);
      return;
    }
    navigate("/swipe", { replace: true });
  }

  useEffect(() => {
    (async () => {
      try {
        setLoading(true);
        setError(null);
        const resp = await listInteractions({ limit: 200 });
        // Keep only positive interactions for this view.
        setItems(resp.items.filter((x) => x.action === "Like" || x.action === "Superlike"));
      } catch (err) {
        if (err instanceof ApiError) setError(err.message);
        else if (err instanceof Error) setError(err.message);
        else setError("Failed to load likes.");
      } finally {
        setLoading(false);
      }
    })();
  }, []);

  const filtered = useMemo(() => {
    const want: SwipeAction[] =
      filter === "Liked" ? ["Like"] : filter === "Superliked" ? ["Superlike"] : ["Like", "Superlike"];

    return [...items]
      .filter((x) => want.includes(x.action))
      .sort((a, b) => (a.createdAtUtc < b.createdAtUtc ? 1 : -1));
  }, [filter, items]);

  useEffect(() => {
    // Fetch movie details for visible items (bounded) so we can show poster/title/year/mpaa.
    const ids = filtered.map((x) => x.tmdbId);
    const missing = ids.filter((id) => detailsByTmdbId[id] === undefined).slice(0, 40);
    if (missing.length === 0) return;

    let cancelled = false;
    (async () => {
      // Simple concurrency limit.
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
  }, [detailsByTmdbId, filtered]);

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
      <div className="modal__panel" role="dialog" aria-modal="true" aria-label="My liked movies" onClick={(e) => e.stopPropagation()}>
        <div className="modal__header">
          <div>
            <h2 className="modal__title">My Likes</h2>
            <p className="modal__subtitle">Liked and superliked movies (click for details).</p>
          </div>
          <button type="button" className="button button--ghost modal__close" onClick={handleClose}>
            Close
          </button>
        </div>

        <div className="modal__body">
          <div className="deck__toolbar" style={{ justifyContent: "space-between", flexWrap: "wrap" }}>
            <div style={{ display: "flex", gap: "0.5rem", flexWrap: "wrap" }}>
              <button
                type="button"
                className={`button button--ghost ${filter === "AllPositive" ? "is-active" : ""}`}
                onClick={() => setFilter("AllPositive")}
              >
                All
              </button>
              <button
                type="button"
                className={`button button--ghost ${filter === "Liked" ? "is-active" : ""}`}
                onClick={() => setFilter("Liked")}
              >
                Likes
              </button>
              <button
                type="button"
                className={`button button--ghost ${filter === "Superliked" ? "is-active" : ""}`}
                onClick={() => setFilter("Superliked")}
              >
                Superlikes
              </button>
            </div>
            <div style={{ color: "#8c93a6", fontWeight: 600 }}>{filtered.length} items</div>
          </div>

          {loading ? <div className="deck__state">Loading likes…</div> : null}
          {!loading && error ? <div className="deck__state deck__state--error">{error}</div> : null}

          {!loading && !error && filtered.length === 0 ? (
            <div className="deck__state">No likes yet. Swipe right (Like) or up (Superlike) to add some.</div>
          ) : null}

          {!loading && !error && filtered.length > 0 ? (
            <div className="list">
              {filtered.map((x) => (
                <button key={`${x.tmdbId}:${x.createdAtUtc}`} className="listItem" type="button" onClick={() => setSelectedTmdbId(x.tmdbId)}>
                  {detailsByTmdbId[x.tmdbId]?.posterUrl ? (
                    <img className="listPoster" src={detailsByTmdbId[x.tmdbId].posterUrl!} alt={detailsByTmdbId[x.tmdbId].title} />
                  ) : (
                    <div className="listPoster listPoster--placeholder">No poster</div>
                  )}
                  <div className="listItem__main">
                    <div className="listItem__title">
                      {detailsByTmdbId[x.tmdbId]?.title ?? `TMDB #${x.tmdbId}`}{" "}
                      {detailsByTmdbId[x.tmdbId]?.releaseYear ? <span className="listItem__year">({detailsByTmdbId[x.tmdbId].releaseYear})</span> : null}
                    </div>
                    <div className="listItem__sub">
                      <span className={x.action === "Superlike" ? "badge badge--super" : "badge badge--like"}>{x.action}</span>
                      {detailsByTmdbId[x.tmdbId]?.mpaaRating ? <span className="badge badge--neutral">{detailsByTmdbId[x.tmdbId].mpaaRating}</span> : null}
                      <span>{new Date(x.createdAtUtc).toLocaleDateString()}</span>
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

