import { useEffect, useMemo, useState } from "react";
import { useLocation, useNavigate, type Location } from "react-router-dom";
import { ApiError } from "../api/http";
import { fetchMatches, fetchMovieDetails } from "../api/client";
import type { MovieDetailsDto } from "../api/contracts";
import MovieDetailsModal from "../components/MovieDetailsModal";
import PosterGallery from "../components/PosterGallery";
import { SERVICE_SCOPE_UPDATED_EVENT } from "../serviceScope";

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
    void load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
    void load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [minUsers]);

  useEffect(() => {
    function handleScopeUpdated() {
      setSelectedTmdbId(null);
      void load();
    }

    window.addEventListener(SERVICE_SCOPE_UPDATED_EVENT, handleScopeUpdated);
    return () => window.removeEventListener(SERVICE_SCOPE_UPDATED_EVENT, handleScopeUpdated);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

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

  const galleryItems = useMemo(() => {
    return tmdbIds.map((tmdbId) => {
      const details = detailsByTmdbId[tmdbId];
      const title = details?.title ?? `TMDB #${tmdbId}`;

      return {
        key: String(tmdbId),
        tmdbId,
        title,
        year: details?.releaseYear ?? null,
        posterUrl: details?.posterUrl ?? null,
        ribbon: { label: "Match", variant: "matched" as const },
      };
    });
  }, [detailsByTmdbId, tmdbIds]);

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
            <p className="modal__subtitle">Movies liked by at least {minUsers} users (click for details).</p>
          </div>
          <button type="button" className="button button--ghost modal__close" onClick={handleClose}>
            Close
          </button>
        </div>

        <div className="modal__body">
          <div className="deck__toolbar" style={{ justifyContent: "space-between", flexWrap: "wrap" }}>
            <div style={{ display: "flex", gap: "0.5rem", flexWrap: "wrap" }}>
              {Array.from({ length: 9 }, (_, i) => i + 2).map((n) => (
                <button
                  key={n}
                  type="button"
                  className={`button button--ghost ${minUsers === n ? "is-active" : ""}`}
                  onClick={() => setMinUsers(n)}
                >
                  {n}+
                </button>
              ))}
            </div>
            <div style={{ color: "#8c93a6", fontWeight: 600 }}>{tmdbIds.length} items</div>
          </div>

          {loading ? <div className="deck__state">Loading matches…</div> : null}
          {!loading && error ? <div className="deck__state deck__state--error">{error}</div> : null}

          {!loading && !error && tmdbIds.length === 0 ? (
            <div className="deck__state">No matches yet. When {minUsers}+ users like the same movie, it will show up here.</div>
          ) : null}

          {!loading && !error && tmdbIds.length > 0 ? (
            <PosterGallery items={galleryItems} onSelect={(id) => setSelectedTmdbId(id)} />
          ) : null}
        </div>
      </div>

      {selectedTmdbId ? <MovieDetailsModal tmdbId={selectedTmdbId} onClose={() => setSelectedTmdbId(null)} /> : null}
    </div>
  );
}

