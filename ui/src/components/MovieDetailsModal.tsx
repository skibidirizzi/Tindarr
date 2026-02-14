import { useEffect, useMemo, useState } from "react";
import { ApiError } from "../api/http";
import { castMovie, fetchMovieDetails, listCastDevices } from "../api/client";
import type { CastDeviceDto, MovieDetailsDto } from "../api/contracts";
import { getServiceScope, SERVICE_SCOPE_UPDATED_EVENT, type ServiceScope } from "../serviceScope";

type MovieDetailsModalProps = {
  tmdbId: number;
  onClose: () => void;
};

export default function MovieDetailsModal({ tmdbId, onClose }: MovieDetailsModalProps) {
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [details, setDetails] = useState<MovieDetailsDto | null>(null);

  const [currentScope, setCurrentScope] = useState<ServiceScope>(() => getServiceScope());

  const [castDevices, setCastDevices] = useState<CastDeviceDto[]>([]);
  const [castDeviceId, setCastDeviceId] = useState<string>("");
  const [castLoading, setCastLoading] = useState(false);
  const [casting, setCasting] = useState(false);
  const [castMessage, setCastMessage] = useState<string | null>(null);

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
    function handleScopeUpdated() {
      setCurrentScope(getServiceScope());
    }

    window.addEventListener(SERVICE_SCOPE_UPDATED_EVENT, handleScopeUpdated);
    return () => window.removeEventListener(SERVICE_SCOPE_UPDATED_EVENT, handleScopeUpdated);
  }, []);

  useEffect(() => {
    // Reset cast state when scope changes.
    setCastDevices([]);
    setCastDeviceId("");
    setCastMessage(null);
  }, [currentScope.serviceType, currentScope.serverId]);

  const isMediaServerScope = useMemo(() => {
    return currentScope.serviceType === "plex" || currentScope.serviceType === "jellyfin" || currentScope.serviceType === "emby";
  }, [currentScope.serviceType]);

  async function onLoadCastDevices() {
    setCastLoading(true);
    setCastMessage(null);
    setError(null);
    try {
      const devices = await listCastDevices();
      setCastDevices(devices);
      if (devices.length && !castDeviceId) {
        setCastDeviceId(devices[0].id);
      }
      if (!devices.length) {
        setCastMessage("No cast devices found.");
      }
    } catch (e) {
      if (e instanceof ApiError) {
        setError(e.message);
      } else {
        setError("Failed to discover cast devices.");
      }
    } finally {
      setCastLoading(false);
    }
  }

  async function onCastMovie() {
    if (!castDeviceId) return;

    setCasting(true);
    setCastMessage(null);
    setError(null);
    try {
      await castMovie({
        deviceId: castDeviceId,
        serviceType: currentScope.serviceType,
        serverId: currentScope.serverId,
        tmdbId,
        title: details?.title ?? null
      });
      setCastMessage("Casting…");
    } catch (e) {
      if (e instanceof ApiError) {
        setError(e.message);
      } else {
        setError("Failed to cast movie.");
      }
    } finally {
      setCasting(false);
    }
  }

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

                  {isMediaServerScope ? (
                    <div style={{ marginTop: "1rem" }}>
                      <div className="field__label">Cast</div>
                      <div style={{ marginTop: "0.25rem", display: "flex", gap: "0.75rem", flexWrap: "wrap", alignItems: "center" }}>
                        <button type="button" className="button button--ghost" onClick={onLoadCastDevices} disabled={castLoading || casting}>
                          {castLoading ? "Finding devices…" : "Find devices"}
                        </button>
                        <select
                          className="field__input"
                          style={{ minWidth: 220 }}
                          value={castDeviceId}
                          onChange={(e) => setCastDeviceId(e.target.value)}
                          disabled={castLoading || casting || castDevices.length === 0}
                        >
                          {castDevices.length === 0 ? <option value="">No devices</option> : null}
                          {castDevices.map((d) => (
                            <option key={d.id} value={d.id}>
                              {d.name}
                            </option>
                          ))}
                        </select>
                        <button type="button" className="button" onClick={onCastMovie} disabled={casting || castLoading || !castDeviceId}>
                          {casting ? "Casting…" : "Cast"}
                        </button>
                      </div>
                      {castMessage ? <div style={{ marginTop: "0.5rem" }}>{castMessage}</div> : null}
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

