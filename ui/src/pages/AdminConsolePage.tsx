import { useEffect, useMemo, useRef, useState } from "react";
import { ApiError } from "../api/http";
import {
  adminCreateUser,
  adminDeleteUser,
  adminListUsers,
  adminSetUserPassword,
  adminSetUserRoles,
  adminUpdateUser,
  embyListServers,
  embySyncLibrary,
  embyTestConnection,
  embyUpsertSettings,
  jellyfinListServers,
  jellyfinSyncLibrary,
  jellyfinTestConnection,
  jellyfinUpsertSettings,
  plexCreatePin,
  plexGetAuthStatus,
  plexListServers,
  plexSyncLibrary,
  plexSyncServers,
  plexVerifyPin,
  radarrGetQualityProfiles,
  radarrGetRootFolders,
  radarrGetSettings,
  radarrSyncLibrary,
  radarrTestConnection,
  radarrUpsertSettings,
  tmdbGetCacheSettings,
  tmdbUpdateCacheSettings,
  tmdbCancelBuild,
  tmdbGetBuildStatus,
  tmdbListStoredMovies,
  tmdbFillMovieDetails,
  tmdbFetchMovieImages,
  tmdbStartBuild
} from "../api/client";
import type {
  EmbyServerDto,
  JellyfinServerDto,
  PlexServerDto,
  RadarrQualityProfileDto,
  RadarrRootFolderDto,
  RadarrSettingsDto,
  TmdbCacheSettingsDto,
  TmdbBuildStatusDto,
  TmdbStoredMovieAdminDto,
  UserDto
} from "../api/contracts";
import {
  getTmdbBulkJob,
  startTmdbBulkFetchAllDetails,
  startTmdbBulkFetchAllImages,
  subscribeTmdbBulkJob,
  type TmdbBulkJob
} from "../tmdb/tmdbBulkJob";

type AdminUserRowState = {
  displayName: string;
  newPassword: string;
  passwordSetFlash: boolean;
  saving: boolean;
  error: string | null;
};

type AppRole = "Admin" | "Curator" | "Contributor";

const ROLE_ORDER: AppRole[] = ["Admin", "Curator", "Contributor"];

function getPrimaryRole(roles: string[]): AppRole {
  for (const r of ROLE_ORDER) {
    if (roles.includes(r)) return r;
  }
  return "Contributor";
}

function nextRole(current: AppRole): AppRole {
  const idx = ROLE_ORDER.indexOf(current);
  return ROLE_ORDER[(idx + 1) % ROLE_ORDER.length] ?? "Contributor";
}

function roleButtonClass(role: AppRole) {
  const base = "pill is-on";
  if (role === "Admin") return `${base} pill--admin`;
  if (role === "Curator") return `${base} pill--curator`;
  return `${base} pill--contributor`;
}

export default function AdminConsolePage() {
  const [tab, setTab] = useState<"users" | "plex" | "radarr" | "jellyfin" | "emby" | "tmdb">("users");

  return (
    <section className="deck">
      <div className="deck__toolbar" style={{ justifyContent: "space-between", gap: "0.75rem", flexWrap: "wrap" }}>
        <h2 style={{ margin: 0 }}>Admin Console</h2>
        <div style={{ display: "flex", gap: "0.5rem", flexWrap: "wrap" }}>
          <button type="button" className={tabButtonClass(tab === "users")} onClick={() => setTab("users")}>Users</button>
          <button type="button" className={tabButtonClass(tab === "plex")} onClick={() => setTab("plex")}>Plex</button>
          <button type="button" className={tabButtonClass(tab === "radarr")} onClick={() => setTab("radarr")}>Radarr</button>
          <button type="button" className={tabButtonClass(tab === "jellyfin")} onClick={() => setTab("jellyfin")}>Jellyfin</button>
          <button type="button" className={tabButtonClass(tab === "emby")} onClick={() => setTab("emby")}>Emby</button>
          <button type="button" className={tabButtonClass(tab === "tmdb")} onClick={() => setTab("tmdb")}>TMDB</button>
        </div>
      </div>

      {tab === "users" ? <UsersTab /> : null}
      {tab === "plex" ? <PlexTab /> : null}
      {tab === "radarr" ? <RadarrTab /> : null}
      {tab === "jellyfin" ? <JellyfinTab /> : null}
      {tab === "emby" ? <EmbyTab /> : null}
      {tab === "tmdb" ? <TmdbTab /> : null}
    </section>
  );
}

function TmdbTab() {
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [settings, setSettings] = useState<TmdbCacheSettingsDto | null>(null);
  const [maxRows, setMaxRows] = useState("5000");
  const [maxMovies, setMaxMovies] = useState("20000");
  const [imageCacheMaxMb, setImageCacheMaxMb] = useState("512");
  const [posterMode, setPosterMode] = useState("Tmdb");

  const [moviesLoading, setMoviesLoading] = useState(false);
  const [moviesError, setMoviesError] = useState<string | null>(null);
  const [movies, setMovies] = useState<TmdbStoredMovieAdminDto[]>([]);
  const [moviesSkip, setMoviesSkip] = useState(0);
  const [moviesNextSkip, setMoviesNextSkip] = useState(0);
  const [moviesHasMore, setMoviesHasMore] = useState(false);
  const [missingDetailsOnly, setMissingDetailsOnly] = useState(false);
  const [missingImagesOnly, setMissingImagesOnly] = useState(false);
  const [moviesBusyId, setMoviesBusyId] = useState<number | null>(null);
  const [tmdbBulkJob, setTmdbBulkJob] = useState<TmdbBulkJob | null>(() => getTmdbBulkJob());
  const previousBulkRunning = useRef<boolean>(Boolean(getTmdbBulkJob()?.running));

  const [buildStatus, setBuildStatus] = useState<TmdbBuildStatusDto | null>(null);
  const [buildLoading, setBuildLoading] = useState(false);
  const [buildError, setBuildError] = useState<string | null>(null);
  const [buildStarting, setBuildStarting] = useState(false);
  const [buildCanceling, setBuildCanceling] = useState(false);
  const [rateLimitOverride, setRateLimitOverride] = useState(false);
  const [discoverLimitPerUser, setDiscoverLimitPerUser] = useState("200");

  const isRunning = buildStatus?.state === "running";

  const load = async () => {
    setLoading(true);
    setError(null);
    try {
      const s = await tmdbGetCacheSettings();
      setSettings(s);
      setMaxRows(String(s.maxRows));
      setMaxMovies(String(s.maxMovies));
      setImageCacheMaxMb(String(s.imageCacheMaxMb));
      setPosterMode(String(s.posterMode || "Tmdb"));
    } catch (e) {
      const msg = e instanceof ApiError ? e.message : "Failed to load TMDB cache settings.";
      setError(msg);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const loadBuild = async () => {
    setBuildLoading(true);
    setBuildError(null);
    try {
      const s = await tmdbGetBuildStatus();
      setBuildStatus(s);
      setRateLimitOverride(Boolean(s.rateLimitOverride));
    } catch (e) {
      const msg = e instanceof ApiError ? e.message : "Failed to load TMDB build status.";
      setBuildError(msg);
    } finally {
      setBuildLoading(false);
    }
  };

  useEffect(() => {
    loadBuild();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
    if (!isRunning) return;

    let alive = true;
    const tick = async () => {
      try {
        const s = await tmdbGetBuildStatus();
        if (alive) setBuildStatus(s);
      } catch (e) {
        if (!alive) return;
        const msg = e instanceof ApiError ? e.message : "Failed to refresh TMDB build status.";
        setBuildError(msg);
      }
    };

    const id = window.setInterval(tick, 1000);
    void tick();
    return () => {
      alive = false;
      window.clearInterval(id);
    };
  }, [isRunning]);

  const onStartBuild = async () => {
    setBuildStarting(true);
    setBuildError(null);
    try {
      const parsedDiscoverLimit = Math.max(1, Math.floor(Number(discoverLimitPerUser)));
      const status = await tmdbStartBuild({
        rateLimitOverride,
        usersBatchSize: 25,
        discoverLimitPerUser: parsedDiscoverLimit,
        prefetchImages: true
      });
      setBuildStatus(status);
    } catch (e) {
      const msg = e instanceof ApiError ? e.message : "Failed to start TMDB build.";
      setBuildError(msg);
    } finally {
      setBuildStarting(false);
    }
  };

  const onCancelBuild = async () => {
    setBuildCanceling(true);
    setBuildError(null);
    try {
      const status = await tmdbCancelBuild("Canceled by admin");
      setBuildStatus(status);
    } catch (e) {
      const msg = e instanceof ApiError ? e.message : "Failed to cancel TMDB build.";
      setBuildError(msg);
    } finally {
      setBuildCanceling(false);
    }
  };

  const onSave = async () => {
    setSaving(true);
    setError(null);
    try {
      const parsed = Math.max(0, Math.floor(Number(maxRows)));
      const parsedMaxMovies = Math.max(0, Math.floor(Number(maxMovies)));
      const parsedImageMax = Math.max(0, Math.floor(Number(imageCacheMaxMb)));
      const updated = await tmdbUpdateCacheSettings({
        maxRows: parsed,
        maxMovies: parsedMaxMovies,
        imageCacheMaxMb: parsedImageMax,
        posterMode
      });
      setSettings(updated);
      setMaxRows(String(updated.maxRows));
      setMaxMovies(String(updated.maxMovies));
      setImageCacheMaxMb(String(updated.imageCacheMaxMb));
      setPosterMode(String(updated.posterMode || "Tmdb"));
    } catch (e) {
      const msg = e instanceof ApiError ? e.message : "Failed to update TMDB cache settings.";
      setError(msg);
    } finally {
      setSaving(false);
    }
  };

  const imageUsedMb = settings ? Math.round(settings.imageCacheBytes / (1024 * 1024)) : 0;
  const localProxyEnabled = settings?.posterMode === "LocalProxy" && (settings?.imageCacheMaxMb ?? 0) > 0;
  const moviesBulkRunning = Boolean(tmdbBulkJob?.running);

  const loadMovies = async (skip: number) => {
    setMoviesLoading(true);
    setMoviesError(null);
    try {
      const res = await tmdbListStoredMovies({
        skip,
        take: 50,
        missingDetailsOnly,
        missingImagesOnly
      });
      setMovies(res.items);
      setMoviesSkip(res.skip);
      setMoviesNextSkip(res.nextSkip);
      setMoviesHasMore(res.hasMore);
    } catch (e) {
      const msg = e instanceof ApiError ? e.message : "Failed to load TMDB cache contents.";
      setMoviesError(msg);
    } finally {
      setMoviesLoading(false);
    }
  };

  useEffect(() => {
    // Reset to first page whenever toggles change.
    void loadMovies(0);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [missingDetailsOnly, missingImagesOnly]);

  useEffect(() => {
    return subscribeTmdbBulkJob(setTmdbBulkJob);
  }, []);

  useEffect(() => {
    const wasRunning = previousBulkRunning.current;
    const isNowRunning = Boolean(tmdbBulkJob?.running);
    if (wasRunning && !isNowRunning) {
      void loadMovies(moviesSkip);
    }
    previousBulkRunning.current = isNowRunning;
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tmdbBulkJob?.running, moviesSkip]);

  const onFillDetails = async (tmdbId: number) => {
    setMoviesBusyId(tmdbId);
    setMoviesError(null);
    try {
      await tmdbFillMovieDetails(tmdbId, false);
      await loadMovies(moviesSkip);
    } catch (e) {
      const msg = e instanceof ApiError ? e.message : "Failed to fill movie details.";
      setMoviesError(msg);
    } finally {
      setMoviesBusyId(null);
    }
  };

  const onFetchImages = async (tmdbId: number) => {
    setMoviesBusyId(tmdbId);
    setMoviesError(null);
    try {
      await tmdbFetchMovieImages(tmdbId, { includePoster: true, includeBackdrop: true });
      await loadMovies(moviesSkip);
    } catch (e) {
      const msg = e instanceof ApiError ? e.message : "Failed to fetch movie images.";
      setMoviesError(msg);
    } finally {
      setMoviesBusyId(null);
    }
  };

  const onFetchAllMissingDetails = async () => {
    setMoviesError(null);
    try {
      await startTmdbBulkFetchAllDetails();
    } catch (e) {
      const msg = e instanceof ApiError ? e.message : "Failed to start bulk details fetch.";
      setMoviesError(msg);
    }
  };

  const onFetchAllMissingImages = async () => {
    setMoviesError(null);
    try {
      await startTmdbBulkFetchAllImages(localProxyEnabled);
    } catch (e) {
      const msg = e instanceof ApiError ? e.message : "Failed to start bulk images fetch.";
      setMoviesError(msg);
    }
  };

  return (
    <section style={{ marginTop: "1rem" }}>
      <h3 style={{ marginTop: 0 }}>TMDB Cache</h3>

      <div className="deck__state" style={{ textAlign: "left" }}>
        <p style={{ marginTop: 0 }}>
          Controls the local TMDB metadata pool and optional poster caching. The workers service keeps this warm for near-instant swipe decks.
        </p>

        {loading ? <div className="deck__state">Loading…</div> : null}
        {error ? <div className="deck__state deck__state--error">{error}</div> : null}

        {!loading && settings ? (
          <div style={{ display: "flex", gap: "0.75rem", flexWrap: "wrap", alignItems: "flex-end" }}>
            <label style={{ display: "grid", gap: "0.25rem" }}>
              <span>Max cache rows</span>
              <input
                className="input"
                inputMode="numeric"
                value={maxRows}
                onChange={(e) => setMaxRows(e.target.value)}
                placeholder="5000"
              />
            </label>

            <div style={{ display: "grid", gap: "0.25rem" }}>
              <span>Current rows</span>
              <div className="pill pill--neutral is-on" style={{ width: "fit-content" }}>{settings.currentRows}</div>
            </div>

            <label style={{ display: "grid", gap: "0.25rem" }}>
              <span>Max movies</span>
              <input
                className="input"
                inputMode="numeric"
                value={maxMovies}
                onChange={(e) => setMaxMovies(e.target.value)}
                placeholder="20000"
              />
            </label>

            <div style={{ display: "grid", gap: "0.25rem" }}>
              <span>Current movies</span>
              <div className="pill pill--neutral is-on" style={{ width: "fit-content" }}>{settings.currentMovies}</div>
            </div>

            <label style={{ display: "grid", gap: "0.25rem" }}>
              <span>Poster mode</span>
              <select className="input" value={posterMode} onChange={(e) => setPosterMode(e.target.value)}>
                <option value="Tmdb">Use TMDB image URLs</option>
                <option value="LocalProxy">Cache images locally (proxy)</option>
              </select>
            </label>

            <label style={{ display: "grid", gap: "0.25rem" }}>
              <span>Max image cache (MB)</span>
              <input
                className="input"
                inputMode="numeric"
                value={imageCacheMaxMb}
                onChange={(e) => setImageCacheMaxMb(e.target.value)}
                placeholder="512"
              />
            </label>

            <div style={{ display: "grid", gap: "0.25rem" }}>
              <span>Image cache used (MB)</span>
              <div className="pill pill--neutral is-on" style={{ width: "fit-content" }}>{imageUsedMb}</div>
            </div>

            <button type="button" className="button" onClick={onSave} disabled={saving}>
              {saving ? "Saving…" : "Save"}
            </button>

            <button type="button" className="button button--neutral" onClick={load} disabled={saving}>
              Refresh
            </button>
          </div>
        ) : null}
      </div>

      <h3 style={{ marginTop: "1rem" }}>Build DB now</h3>
      <div className="deck__state" style={{ textAlign: "left" }}>
        <p style={{ marginTop: 0 }}>
          Runs an on-demand TMDB pull to populate the local metadata pool for all users. Use this after changing TMDB settings, or if you want to warm the pool immediately.
        </p>

        {buildLoading ? <div className="deck__state">Loading status…</div> : null}
        {buildError ? <div className="deck__state deck__state--error">{buildError}</div> : null}

        <div style={{ display: "flex", gap: "0.75rem", flexWrap: "wrap", alignItems: "flex-end" }}>
          <label style={{ display: "grid", gap: "0.25rem" }}>
            <span>Rate limit override</span>
            <label style={{ display: "flex", gap: "0.5rem", alignItems: "center" }}>
              <input
                type="checkbox"
                checked={rateLimitOverride}
                disabled={isRunning}
                onChange={(e) => setRateLimitOverride(e.target.checked)}
              />
              <span>Bypass TMDB rate limiting (use carefully)</span>
            </label>
          </label>

          <label style={{ display: "grid", gap: "0.25rem" }}>
            <span>Discover limit per user</span>
            <input
              className="input"
              inputMode="numeric"
              value={discoverLimitPerUser}
              disabled={isRunning}
              onChange={(e) => setDiscoverLimitPerUser(e.target.value)}
              placeholder="200"
            />
          </label>

          <button type="button" className="button" onClick={onStartBuild} disabled={buildStarting || buildCanceling}>
            {buildStarting ? "Starting…" : isRunning ? "Build running" : "Build DB now"}
          </button>

          <button
            type="button"
            className="button button--neutral"
            onClick={onCancelBuild}
            disabled={!isRunning || buildStarting || buildCanceling}
          >
            {buildCanceling ? "Canceling…" : "Cancel"}
          </button>

          <button type="button" className="button button--neutral" onClick={loadBuild} disabled={buildStarting || buildCanceling}>
            Refresh status
          </button>
        </div>

        {buildStatus ? (
          <div style={{ marginTop: "0.75rem", display: "flex", gap: "0.75rem", flexWrap: "wrap", alignItems: "flex-end" }}>
            <div style={{ display: "grid", gap: "0.25rem" }}>
              <span>Status</span>
              <div className="pill pill--neutral is-on" style={{ width: "fit-content" }}>{buildStatus.state}</div>
            </div>

            <div style={{ display: "grid", gap: "0.25rem" }}>
              <span>Users</span>
              <div className="pill pill--neutral is-on" style={{ width: "fit-content" }}>
                {buildStatus.usersProcessed}/{buildStatus.usersTotal}
              </div>
            </div>

            <div style={{ display: "grid", gap: "0.25rem" }}>
              <span>Current user</span>
              <div className="pill pill--neutral is-on" style={{ width: "fit-content" }}>{buildStatus.currentUserId ?? "—"}</div>
            </div>

            <div style={{ display: "grid", gap: "0.25rem" }}>
              <span>Discovered</span>
              <div className="pill pill--neutral is-on" style={{ width: "fit-content" }}>{buildStatus.moviesDiscovered}</div>
            </div>

            <div style={{ display: "grid", gap: "0.25rem" }}>
              <span>Details</span>
              <div className="pill pill--neutral is-on" style={{ width: "fit-content" }}>{buildStatus.detailsFetched}</div>
            </div>

            <div style={{ display: "grid", gap: "0.25rem" }}>
              <span>Images</span>
              <div className="pill pill--neutral is-on" style={{ width: "fit-content" }}>{buildStatus.imagesFetched}</div>
            </div>

            <div style={{ display: "grid", gap: "0.25rem", minWidth: 240 }}>
              <span>Message</span>
              <div className="pill pill--neutral is-on" style={{ width: "fit-content", maxWidth: "100%", overflow: "hidden", textOverflow: "ellipsis" }}>
                {buildStatus.lastMessage ?? "—"}
              </div>
            </div>

            {buildStatus.lastError ? (
              <div className="deck__state deck__state--error" style={{ width: "100%" }}>{buildStatus.lastError}</div>
            ) : null}
          </div>
        ) : null}
      </div>

      <h3 style={{ marginTop: "1rem" }}>Cache contents</h3>
      <div className="deck__state" style={{ textAlign: "left" }}>
        <p style={{ marginTop: 0 }}>
          Browse the stored TMDB movie records and repair missing details or cached images.
        </p>

        {moviesLoading ? <div className="deck__state">Loading movies…</div> : null}
        {moviesError ? <div className="deck__state deck__state--error">{moviesError}</div> : null}

        <div style={{ display: "flex", gap: "0.75rem", flexWrap: "wrap", alignItems: "flex-end" }}>
          <label style={{ display: "flex", gap: "0.5rem", alignItems: "center" }}>
            <input type="checkbox" checked={missingDetailsOnly} onChange={(e) => setMissingDetailsOnly(e.target.checked)} disabled={moviesBulkRunning} />
            <span>Missing details only</span>
          </label>

          <label style={{ display: "flex", gap: "0.5rem", alignItems: "center" }}>
            <input type="checkbox" checked={missingImagesOnly} onChange={(e) => setMissingImagesOnly(e.target.checked)} disabled={moviesBulkRunning} />
            <span>Missing images only</span>
          </label>

          <button type="button" className="button" onClick={onFetchAllMissingDetails} disabled={moviesLoading || moviesBulkRunning}>
            Fetch all details
          </button>

          <button
            type="button"
            className="button"
            onClick={onFetchAllMissingImages}
            disabled={moviesLoading || moviesBulkRunning || !localProxyEnabled}
          >
            Fetch all images
          </button>

          <button type="button" className="button button--neutral" onClick={() => loadMovies(0)} disabled={moviesLoading}>
            Refresh list
          </button>

          <button
            type="button"
            className="button button--neutral"
            onClick={() => loadMovies(Math.max(0, moviesSkip - 50))}
            disabled={moviesLoading || moviesSkip <= 0}
          >
            Prev
          </button>

          <button
            type="button"
            className="button button--neutral"
            onClick={() => loadMovies(moviesNextSkip)}
            disabled={moviesLoading || !moviesHasMore}
          >
            Next
          </button>
        </div>

        <div style={{ marginTop: "0.75rem", overflowX: "auto" }}>
          <table style={{ width: "100%", borderCollapse: "collapse" }}>
            <thead>
              <tr>
                <th style={{ textAlign: "left", padding: "0.5rem" }}>TMDB</th>
                <th style={{ textAlign: "left", padding: "0.5rem" }}>Title</th>
                <th style={{ textAlign: "left", padding: "0.5rem" }}>Details</th>
                <th style={{ textAlign: "left", padding: "0.5rem" }}>Poster</th>
                <th style={{ textAlign: "left", padding: "0.5rem" }}>Backdrop</th>
                <th style={{ textAlign: "left", padding: "0.5rem" }}>Actions</th>
              </tr>
            </thead>
            <tbody>
              {movies.map((m) => {
                const hasDetails = Boolean(m.detailsFetchedAtUtc);
                const hasPoster = Boolean(m.posterPath);
                const hasBackdrop = Boolean(m.backdropPath);
                const busy = moviesBusyId === m.tmdbId;
                return (
                  <tr key={m.tmdbId} style={{ borderTop: "1px solid rgba(255,255,255,0.08)" }}>
                    <td style={{ padding: "0.5rem", whiteSpace: "nowrap" }}>{m.tmdbId}</td>
                    <td style={{ padding: "0.5rem" }}>{m.title}{m.releaseYear ? ` (${m.releaseYear})` : ""}</td>
                    <td style={{ padding: "0.5rem" }}>{hasDetails ? "Yes" : "No"}</td>
                    <td style={{ padding: "0.5rem" }}>
                      {hasPoster ? (localProxyEnabled ? (m.posterCached ? "Cached" : "Missing") : "N/A") : "—"}
                    </td>
                    <td style={{ padding: "0.5rem" }}>
                      {hasBackdrop ? (localProxyEnabled ? (m.backdropCached ? "Cached" : "Missing") : "N/A") : "—"}
                    </td>
                    <td style={{ padding: "0.5rem", whiteSpace: "nowrap" }}>
                      <button
                        type="button"
                        className="button button--neutral"
                        onClick={() => onFillDetails(m.tmdbId)}
                        disabled={busy}
                      >
                        {busy ? "Working…" : "Fill details"}
                      </button>
                      <button
                        type="button"
                        className="button button--neutral"
                        onClick={() => onFetchImages(m.tmdbId)}
                        disabled={busy || !localProxyEnabled}
                        style={{ marginLeft: "0.5rem" }}
                      >
                        {busy ? "Working…" : "Fetch images"}
                      </button>
                    </td>
                  </tr>
                );
              })}

              {!moviesLoading && movies.length === 0 ? (
                <tr>
                  <td style={{ padding: "0.5rem" }} colSpan={6}>No movies found.</td>
                </tr>
              ) : null}
            </tbody>
          </table>
        </div>
      </div>
    </section>
  );
}

function tabButtonClass(active: boolean) {
  return `pill pill--neutral ${active ? "is-on" : ""}`.trim();
}

function UsersTab() {
  const [users, setUsers] = useState<UserDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const [createUserId, setCreateUserId] = useState("");
  const [createDisplayName, setCreateDisplayName] = useState("");
  const [createPassword, setCreatePassword] = useState("");
  const [createRole, setCreateRole] = useState<AppRole>("Admin");
  const [createSaving, setCreateSaving] = useState(false);

  const [rowState, setRowState] = useState<Record<string, AdminUserRowState>>({});

  const load = async () => {
    setLoading(true);
    setError(null);
    try {
      const list = await adminListUsers(0, 500);
      setUsers(list);
      setRowState((prev) => {
        const next: Record<string, AdminUserRowState> = { ...prev };
        for (const u of list) {
          if (!next[u.userId]) {
            next[u.userId] = {
              displayName: u.displayName,
              newPassword: "",
              passwordSetFlash: false,
              saving: false,
              error: null
            };
          } else {
            next[u.userId] = {
              ...next[u.userId],
              displayName: next[u.userId].displayName || u.displayName,
              newPassword: next[u.userId].newPassword
            };
          }
        }
        return next;
      });
    } catch (e) {
      setError(e instanceof ApiError ? e.message : "Failed to load users.");
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    void load();
  }, []);

  const usersSorted = useMemo(() => {
    return [...users].sort((a, b) => a.userId.localeCompare(b.userId));
  }, [users]);

  const onCreate = async () => {
    setCreateSaving(true);
    setError(null);
    try {
      const created = await adminCreateUser({
        userId: createUserId,
        displayName: createDisplayName,
        password: createPassword,
        roles: [createRole]
      });

      setUsers((prev) => {
        const next = prev.filter((u) => u.userId !== created.userId);
        next.push(created);
        return next;
      });

      setCreateUserId("");
      setCreateDisplayName("");
      setCreatePassword("");
    } catch (e) {
      setError(e instanceof ApiError ? e.message : "Failed to create user.");
    } finally {
      setCreateSaving(false);
    }
  };

  const patchRow = (userId: string, patch: Partial<AdminUserRowState>) => {
    setRowState((prev) => ({
      ...prev,
      [userId]: {
        ...(prev[userId] ?? {
          displayName: "",
          newPassword: "",
          passwordSetFlash: false,
          saving: false,
          error: null
        }),
        ...patch
      }
    }));
  };

  const withRowSaving = async (userId: string, fn: () => Promise<void>) => {
    patchRow(userId, { saving: true, error: null });
    try {
      await fn();
      await load();
    } catch (e) {
      patchRow(userId, { error: e instanceof ApiError ? e.message : "Request failed." });
    } finally {
      patchRow(userId, { saving: false });
    }
  };

  return (
    <>
      <div className="deck__toolbar" style={{ justifyContent: "flex-end" }}>
        <button type="button" className="app__navLink" onClick={() => void load()} disabled={loading}>
          Refresh
        </button>
      </div>

      {error ? <div className="deck__state deck__state--error">{error}</div> : null}

      <div className="deck__state" style={{ textAlign: "left" }}>
        <h3 style={{ marginTop: 0, marginBottom: "0.75rem" }}>Add user</h3>

        <div style={{ overflowX: "auto" }}>
          <table className="adminTable">
            <thead>
              <tr>
                <th>User Id</th>
                <th>Display name</th>
                <th>Role</th>
                <th>Password</th>
                <th style={{ width: 1 }} />
              </tr>
            </thead>
            <tbody>
              <tr>
                <td>
                  <input
                    className="input"
                    style={{ minWidth: 160 }}
                    placeholder="New user id"
                    value={createUserId}
                    onChange={(e) => setCreateUserId(e.target.value)}
                  />
                </td>
                <td>
                  <input
                    className="input"
                    style={{ minWidth: 200 }}
                    placeholder="Display name"
                    value={createDisplayName}
                    onChange={(e) => setCreateDisplayName(e.target.value)}
                  />
                </td>
                <td>
                  <button
                    type="button"
                    className={roleButtonClass(createRole)}
                    onClick={() => setCreateRole(nextRole(createRole))}
                  >
                    {createRole}
                  </button>
                </td>
                <td>
                  <input
                    className="input"
                    style={{ minWidth: 110 }}
                    type="password"
                    placeholder="Password"
                    value={createPassword}
                    onChange={(e) => setCreatePassword(e.target.value)}
                    autoComplete="new-password"
                  />
                </td>
                <td style={{ textAlign: "right" }}>
                  <button
                    type="button"
                    className="pill pill--neutral is-on"
                    onClick={() => void onCreate()}
                    disabled={createSaving || !createUserId.trim() || !createDisplayName.trim() || !createPassword}
                    style={{ whiteSpace: "nowrap" }}
                  >
                    {createSaving ? "Adding…" : "Add user"}
                  </button>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>

      <div className="deck__state" style={{ textAlign: "left" }}>
        <h3 style={{ marginTop: 0, marginBottom: "0.75rem" }}>Users</h3>

        {loading ? <div className="deck__state">Loading…</div> : null}
        {!loading && usersSorted.length === 0 ? <div className="deck__state">No users found.</div> : null}

        {!loading && usersSorted.length ? (
          <div style={{ overflowX: "auto" }}>
            <table className="adminTable">
              <thead>
                <tr>
                  <th>User Id</th>
                  <th>Display name</th>
                  <th>Role</th>
                  <th>Change password</th>
                  <th style={{ width: 1 }}>Delete User</th>
                </tr>
              </thead>
              <tbody>
                {usersSorted.map((u) => {
                  const s = rowState[u.userId];
                  const role = getPrimaryRole(u.roles);
                  const displayName = s?.displayName ?? u.displayName;

                  return (
                    <tr key={u.userId}>
                      <td className="adminTable__mono">{u.userId}</td>
                      <td>
                        <div style={{ display: "flex", flexDirection: "column", gap: "0.5rem" }}>
                          <div style={{ display: "flex", gap: "0.5rem", alignItems: "center", flexWrap: "wrap" }}>
                            <input
                              className="input"
                              style={{ minWidth: 200 }}
                              value={displayName}
                              onChange={(e) => patchRow(u.userId, { displayName: e.target.value })}
                            />
                            <button
                              type="button"
                              className="pill pill--neutral is-on"
                              disabled={s?.saving || !displayName.trim() || displayName.trim() === u.displayName}
                              onClick={() =>
                                void withRowSaving(u.userId, async () => {
                                  await adminUpdateUser(u.userId, { displayName: displayName.trim() });
                                })
                              }
                            >
                              Save
                            </button>
                          </div>
                        </div>
                      </td>
                      <td>
                        <button
                          type="button"
                          className={roleButtonClass(role)}
                          disabled={s?.saving}
                          onClick={() =>
                            void withRowSaving(u.userId, async () => {
                              const next = nextRole(role);
                              await adminSetUserRoles(u.userId, { roles: [next] });
                            })
                          }
                        >
                          {role}
                        </button>
                      </td>
                      <td>
                        <div style={{ display: "flex", gap: "0.5rem", alignItems: "center", flexWrap: "wrap" }}>
                          <input
                            className="input"
                            style={{ minWidth: 110 }}
                            type="password"
                            placeholder="New password"
                            value={s?.newPassword ?? ""}
                            onChange={(e) => patchRow(u.userId, { newPassword: e.target.value })}
                            autoComplete="new-password"
                          />
                          <button
                            type="button"
                            className="pill pill--neutral is-on"
                            disabled={s?.saving || !(s?.newPassword ?? "")}
                            onClick={() =>
                              void withRowSaving(u.userId, async () => {
                                const newPassword = s?.newPassword ?? "";
                                if (!newPassword) return;
                                await adminSetUserPassword(u.userId, { newPassword });
                                patchRow(u.userId, { newPassword: "", passwordSetFlash: true });
                                window.setTimeout(() => patchRow(u.userId, { passwordSetFlash: false }), 4000);
                              })
                            }
                          >
                            Set
                          </button>
                        </div>
                        {s?.passwordSetFlash ? (
                          <div style={{ marginTop: "0.4rem", color: "#8ce99a", fontSize: "0.85rem", fontWeight: 700 }}>
                            Password set
                          </div>
                        ) : null}
                        {s?.error ? <div className="adminTable__error">{s.error}</div> : null}
                      </td>
                      <td style={{ textAlign: "right" }}>
                        <button
                          type="button"
                          className="app__navLink"
                          disabled={s?.saving}
                          style={{ borderColor: "rgba(255, 107, 107, 0.4)" }}
                          onClick={() =>
                            void withRowSaving(u.userId, async () => {
                              if (!confirm(`Delete user '${u.userId}'?`)) return;
                              await adminDeleteUser(u.userId);
                            })
                          }
                        >
                          Delete
                        </button>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        ) : null}
      </div>
    </>
  );
}

function PlexTab() {
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [authStatus, setAuthStatus] = useState<{ hasClientIdentifier: boolean; hasAuthToken: boolean } | null>(null);
  const [pin, setPin] = useState<{ pinId: number; code: string; expiresAtUtc: string; authUrl: string } | null>(null);
  const [servers, setServers] = useState<PlexServerDto[]>([]);

  const load = async () => {
    setLoading(true);
    setError(null);
    try {
      const [status, list] = await Promise.all([plexGetAuthStatus(), plexListServers()]);
      setAuthStatus(status);
      setServers(list);
    } catch (e) {
      setError(e instanceof ApiError ? e.message : "Failed to load Plex status.");
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    void load();
  }, []);

  const onCreatePin = async () => {
    setError(null);
    try {
      const created = await plexCreatePin();
      setPin(created);
    } catch (e) {
      setError(e instanceof ApiError ? e.message : "Failed to create pin.");
    }
  };

  const onVerifyPin = async () => {
    if (!pin) return;
    setError(null);
    try {
      const status = await plexVerifyPin(pin.pinId);
      if (status.authorized) {
        await load();
      }
    } catch (e) {
      setError(e instanceof ApiError ? e.message : "Failed to verify pin.");
    }
  };

  const onSyncServers = async () => {
    setError(null);
    try {
      const list = await plexSyncServers();
      setServers(list);
    } catch (e) {
      setError(e instanceof ApiError ? e.message : "Failed to sync Plex servers.");
    }
  };

  const onSyncLibrary = async (serverId: string) => {
    setError(null);
    try {
      await plexSyncLibrary("plex", serverId);
      await load();
    } catch (e) {
      setError(e instanceof ApiError ? e.message : "Failed to sync Plex library.");
    }
  };

  return (
    <>
      <div className="deck__toolbar" style={{ justifyContent: "flex-end" }}>
        <button type="button" className="app__navLink" onClick={() => void load()} disabled={loading}>
          Refresh
        </button>
      </div>

      {error ? <div className="deck__state deck__state--error">{error}</div> : null}
      {loading ? <div className="deck__state">Loading…</div> : null}

      <div className="deck__state" style={{ textAlign: "left" }}>
        <h3 style={{ marginTop: 0, marginBottom: "0.75rem" }}>Plex</h3>

        <div style={{ display: "flex", gap: "0.75rem", flexWrap: "wrap", alignItems: "center" }}>
          <div style={{ color: "#8c93a6", fontWeight: 700 }}>
            Auth token: {authStatus?.hasAuthToken ? "configured" : "missing"}
          </div>
          <button type="button" className="pill pill--neutral is-on" onClick={() => void onCreatePin()}>
            Create pin
          </button>
          {pin ? (
            <>
              <button type="button" className="pill pill--neutral is-on" onClick={() => void onVerifyPin()}>
                Check pin
              </button>
              <div style={{ marginTop: "0.75rem" }}>
                <div style={{ color: "#8c93a6", fontWeight: 700 }}>Authorize</div>
                <a href={pin.authUrl} target="_blank" rel="noreferrer" style={{ color: "#ffd43b" }}>
                  {pin.authUrl}
                </a>
                <div style={{ marginTop: "0.25rem", color: "#8c93a6" }}>Code: {pin.code}</div>
              </div>
            </>
          ) : null}
        </div>

        <div style={{ marginTop: "1rem", display: "flex", justifyContent: "space-between", alignItems: "center" }}>
          <h4 style={{ margin: 0 }}>Servers</h4>
          <button type="button" className="pill pill--neutral is-on" onClick={() => void onSyncServers()}>
            Sync servers
          </button>
        </div>

        {servers.length === 0 ? <div className="deck__state">No Plex servers found.</div> : null}

        {servers.length ? (
          <div style={{ overflowX: "auto" }}>
            <table className="adminTable">
              <thead>
                <tr>
                  <th>Server</th>
                  <th>Version</th>
                  <th>Last sync</th>
                  <th style={{ width: 1 }} />
                </tr>
              </thead>
              <tbody>
                {servers.map((s) => (
                  <tr key={s.serverId}>
                    <td className="adminTable__mono">{s.name}</td>
                    <td>{s.version ?? ""}</td>
                    <td>{s.lastLibrarySyncUtc ?? ""}</td>
                    <td style={{ textAlign: "right" }}>
                      <button type="button" className="pill pill--neutral is-on" onClick={() => void onSyncLibrary(s.serverId)}>
                        Sync library
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : null}
      </div>
    </>
  );
}

function RadarrTab() {
  const [serverId, setServerId] = useState("default");
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [settings, setSettings] = useState<RadarrSettingsDto | null>(null);

  const [baseUrl, setBaseUrl] = useState("");
  const [apiKey, setApiKey] = useState("");
  const [tagLabel, setTagLabel] = useState("");
  const [autoAddEnabled, setAutoAddEnabled] = useState(false);
  const [qualityProfileId, setQualityProfileId] = useState<number | null>(null);
  const [rootFolderPath, setRootFolderPath] = useState<string | null>(null);

  const [profiles, setProfiles] = useState<RadarrQualityProfileDto[]>([]);
  const [folders, setFolders] = useState<RadarrRootFolderDto[]>([]);

  const load = async () => {
    setLoading(true);
    setError(null);
    try {
      const s = await radarrGetSettings("radarr", serverId.trim() || "default");
      setSettings(s);
      setBaseUrl(s.baseUrl ?? "");
      setApiKey("");
      setTagLabel(s.tagLabel ?? "");
      setAutoAddEnabled(s.autoAddEnabled);
      setQualityProfileId(s.qualityProfileId);
      setRootFolderPath(s.rootFolderPath);

      // Best-effort load for dropdowns; these require configured Radarr.
      if (s.configured) {
        try {
          const [p, f] = await Promise.all([
            radarrGetQualityProfiles("radarr", s.serverId),
            radarrGetRootFolders("radarr", s.serverId)
          ]);
          setProfiles(p);
          setFolders(f);
        } catch {
          setProfiles([]);
          setFolders([]);
        }
      } else {
        setProfiles([]);
        setFolders([]);
      }
    } catch (e) {
      setError(e instanceof ApiError ? e.message : "Failed to load Radarr settings.");
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    void load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const onSave = async () => {
    setLoading(true);
    setError(null);
    try {
      const updated = await radarrUpsertSettings("radarr", serverId.trim() || "default", {
        baseUrl: baseUrl.trim(),
        apiKey: apiKey.trim() || null,
        qualityProfileId,
        rootFolderPath,
        tagLabel: tagLabel.trim() || null,
        autoAddEnabled
      });
      setSettings(updated);
      setApiKey("");
      await load();
    } catch (e) {
      setError(e instanceof ApiError ? e.message : "Failed to save Radarr settings.");
    } finally {
      setLoading(false);
    }
  };

  const onTest = async () => {
    setLoading(true);
    setError(null);
    try {
      const result = await radarrTestConnection("radarr", serverId.trim() || "default");
      if (!result.ok) {
        setError(result.message ?? "Radarr test failed.");
      }
    } catch (e) {
      setError(e instanceof ApiError ? e.message : "Failed to test Radarr.");
    } finally {
      setLoading(false);
    }
  };

  const onSyncLibrary = async () => {
    setLoading(true);
    setError(null);
    try {
      await radarrSyncLibrary("radarr", serverId.trim() || "default");
      await load();
    } catch (e) {
      setError(e instanceof ApiError ? e.message : "Failed to sync Radarr library.");
    } finally {
      setLoading(false);
    }
  };

  return (
    <>
      <div className="deck__toolbar" style={{ justifyContent: "flex-end" }}>
        <button type="button" className="app__navLink" onClick={() => void load()} disabled={loading}>
          Refresh
        </button>
      </div>

      {error ? <div className="deck__state deck__state--error">{error}</div> : null}

      <div className="deck__state" style={{ textAlign: "left" }}>
        <h3 style={{ marginTop: 0, marginBottom: "0.75rem" }}>Radarr</h3>

        <div style={{ display: "flex", gap: "0.5rem", alignItems: "center", flexWrap: "wrap" }}>
          <span style={{ color: "#8c93a6", fontWeight: 700 }}>ServerId</span>
          <input className="input" style={{ minWidth: 180 }} value={serverId} onChange={(e) => setServerId(e.target.value)} />
          <button type="button" className="pill pill--neutral is-on" onClick={() => void load()} disabled={loading}>
            Load
          </button>
        </div>

        <div style={{ marginTop: "1rem", display: "grid", gridTemplateColumns: "1fr 1fr", gap: "0.75rem" }}>
          <div>
            <div style={{ color: "#8c93a6", fontWeight: 700, marginBottom: "0.25rem" }}>BaseUrl</div>
            <input className="input" value={baseUrl} onChange={(e) => setBaseUrl(e.target.value)} placeholder="http://localhost:7878" />
          </div>
          <div>
            <div style={{ color: "#8c93a6", fontWeight: 700, marginBottom: "0.25rem" }}>ApiKey</div>
            <input className="input" type="password" value={apiKey} onChange={(e) => setApiKey(e.target.value)} placeholder={settings?.hasApiKey ? "(saved)" : ""} />
          </div>

          <div>
            <div style={{ color: "#8c93a6", fontWeight: 700, marginBottom: "0.25rem" }}>Quality Profile</div>
            <select
              className="input"
              value={qualityProfileId ?? ""}
              onChange={(e) => setQualityProfileId(e.target.value ? Number(e.target.value) : null)}
            >
              <option value="">(none)</option>
              {profiles.map((p) => (
                <option key={p.id} value={p.id}>
                  {p.name}
                </option>
              ))}
            </select>
          </div>

          <div>
            <div style={{ color: "#8c93a6", fontWeight: 700, marginBottom: "0.25rem" }}>Root Folder</div>
            <select
              className="input"
              value={rootFolderPath ?? ""}
              onChange={(e) => setRootFolderPath(e.target.value ? e.target.value : null)}
            >
              <option value="">(none)</option>
              {folders.map((f) => (
                <option key={f.id} value={f.path}>
                  {f.path}
                </option>
              ))}
            </select>
          </div>

          <div>
            <div style={{ color: "#8c93a6", fontWeight: 700, marginBottom: "0.25rem" }}>Tag Label</div>
            <input className="input" value={tagLabel} onChange={(e) => setTagLabel(e.target.value)} placeholder="tindarr" />
          </div>

          <div style={{ display: "flex", alignItems: "end" }}>
            <label style={{ display: "flex", gap: "0.5rem", alignItems: "center" }}>
              <input type="checkbox" checked={autoAddEnabled} onChange={(e) => setAutoAddEnabled(e.target.checked)} />
              <span style={{ color: "#8c93a6", fontWeight: 700 }}>Auto-add accepted</span>
            </label>
          </div>
        </div>

        <div style={{ marginTop: "1rem", display: "flex", gap: "0.5rem", flexWrap: "wrap" }}>
          <button type="button" className="pill pill--neutral is-on" onClick={() => void onSave()} disabled={loading || !baseUrl.trim()}>
            Save
          </button>
          <button type="button" className="pill pill--neutral is-on" onClick={() => void onTest()} disabled={loading}>
            Test connection
          </button>
          <button type="button" className="pill pill--neutral is-on" onClick={() => void onSyncLibrary()} disabled={loading}>
            Sync library
          </button>
          <div style={{ marginLeft: "auto", color: "#8c93a6", fontWeight: 700 }}>
            Last sync: {settings?.lastLibrarySyncUtc ?? ""}
          </div>
        </div>
      </div>
    </>
  );
}

function JellyfinTab() {
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [servers, setServers] = useState<JellyfinServerDto[]>([]);
  const [selectedServerId, setSelectedServerId] = useState<string>("");
  const [baseUrl, setBaseUrl] = useState("");
  const [apiKey, setApiKey] = useState("");

  const load = async () => {
    setLoading(true);
    setError(null);
    try {
      const list = await jellyfinListServers();
      setServers(list);
      if (!selectedServerId) {
        if (list.length === 1) setSelectedServerId(list[0].serverId);
      } else if (!list.some((s) => s.serverId === selectedServerId)) {
        setSelectedServerId(list.length === 1 ? list[0].serverId : "");
      }
    } catch (e) {
      setError(e instanceof ApiError ? e.message : "Failed to load Jellyfin servers.");
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    void load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const onSave = async () => {
    setLoading(true);
    setError(null);
    try {
      try {
        const updated = await jellyfinUpsertSettings({ baseUrl: baseUrl.trim(), apiKey: apiKey.trim() }, false);
        setSelectedServerId(updated.serverId);
      } catch (e) {
        if (e instanceof ApiError && e.status === 400) {
          const ok = confirm(`${e.message}\n\nAdd a second Jellyfin server?`);
          if (!ok) throw e;
          const updated = await jellyfinUpsertSettings({ baseUrl: baseUrl.trim(), apiKey: apiKey.trim() }, true);
          setSelectedServerId(updated.serverId);
        } else {
          throw e;
        }
      }
      setApiKey("");
      await load();
    } catch (e) {
      setError(e instanceof ApiError ? e.message : "Failed to save Jellyfin settings.");
    } finally {
      setLoading(false);
    }
  };

  const onTest = async () => {
    if (!selectedServerId) return;
    setLoading(true);
    setError(null);
    try {
      const result = await jellyfinTestConnection("jellyfin", selectedServerId);
      if (!result.ok) {
        setError(result.message ?? "Jellyfin test failed.");
      }
    } catch (e) {
      setError(e instanceof ApiError ? e.message : "Failed to test Jellyfin.");
    } finally {
      setLoading(false);
    }
  };

  const onSync = async () => {
    if (!selectedServerId) return;
    setLoading(true);
    setError(null);
    try {
      await jellyfinSyncLibrary("jellyfin", selectedServerId);
      await load();
    } catch (e) {
      setError(e instanceof ApiError ? e.message : "Failed to sync Jellyfin library.");
    } finally {
      setLoading(false);
    }
  };

  const selected = servers.find((s) => s.serverId === selectedServerId) ?? null;

  return (
    <>
      <div className="deck__toolbar" style={{ justifyContent: "flex-end" }}>
        <button type="button" className="app__navLink" onClick={() => void load()} disabled={loading}>
          Refresh
        </button>
      </div>
      {error ? <div className="deck__state deck__state--error">{error}</div> : null}
      {loading ? <div className="deck__state">Loading…</div> : null}

      <div className="deck__state" style={{ textAlign: "left" }}>
        <h3 style={{ marginTop: 0, marginBottom: "0.75rem" }}>Jellyfin</h3>

        <div style={{ display: "flex", gap: "0.5rem", alignItems: "center", flexWrap: "wrap" }}>
          <span style={{ color: "#8c93a6", fontWeight: 700 }}>Configured servers</span>
          <select className="input" value={selectedServerId} onChange={(e) => setSelectedServerId(e.target.value)}>
            <option value="">(select)</option>
            {servers.map((s) => (
              <option key={s.serverId} value={s.serverId}>
                {s.name} ({s.serverId})
              </option>
            ))}
          </select>
          <button type="button" className="pill pill--neutral is-on" onClick={() => void onTest()} disabled={loading || !selectedServerId}>
            Test
          </button>
          <button type="button" className="pill pill--neutral is-on" onClick={() => void onSync()} disabled={loading || !selectedServerId}>
            Sync library
          </button>
          <div style={{ marginLeft: "auto", color: "#8c93a6", fontWeight: 700 }}>
            Last sync: {selected?.lastLibrarySyncUtc ?? ""}
          </div>
        </div>

        <div style={{ marginTop: "1rem", display: "grid", gridTemplateColumns: "1fr 1fr", gap: "0.75rem" }}>
          <div>
            <div style={{ color: "#8c93a6", fontWeight: 700, marginBottom: "0.25rem" }}>BaseUrl</div>
            <input className="input" value={baseUrl} onChange={(e) => setBaseUrl(e.target.value)} placeholder="http://localhost:8096" />
          </div>
          <div>
            <div style={{ color: "#8c93a6", fontWeight: 700, marginBottom: "0.25rem" }}>ApiKey</div>
            <input className="input" type="password" value={apiKey} onChange={(e) => setApiKey(e.target.value)} placeholder="(required to save)" />
          </div>
        </div>

        <div style={{ marginTop: "1rem", display: "flex", gap: "0.5rem" }}>
          <button type="button" className="pill pill--neutral is-on" onClick={() => void onSave()} disabled={loading || !baseUrl.trim() || !apiKey.trim()}>
            Save
          </button>
          <div style={{ color: "#8c93a6", fontWeight: 700, alignSelf: "center" }}>
            Saving auto-populates ServerId from Jellyfin.
          </div>
        </div>
      </div>
    </>
  );
}

function EmbyTab() {
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [servers, setServers] = useState<EmbyServerDto[]>([]);
  const [selectedServerId, setSelectedServerId] = useState<string>("");
  const [baseUrl, setBaseUrl] = useState("");
  const [apiKey, setApiKey] = useState("");

  const load = async () => {
    setLoading(true);
    setError(null);
    try {
      const list = await embyListServers();
      setServers(list);
      if (!selectedServerId) {
        if (list.length === 1) setSelectedServerId(list[0].serverId);
      } else if (!list.some((s) => s.serverId === selectedServerId)) {
        setSelectedServerId(list.length === 1 ? list[0].serverId : "");
      }
    } catch (e) {
      setError(e instanceof ApiError ? e.message : "Failed to load Emby servers.");
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    void load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const onSave = async () => {
    setLoading(true);
    setError(null);
    try {
      try {
        const updated = await embyUpsertSettings({ baseUrl: baseUrl.trim(), apiKey: apiKey.trim() }, false);
        setSelectedServerId(updated.serverId);
      } catch (e) {
        if (e instanceof ApiError && e.status === 400) {
          const ok = confirm(`${e.message}\n\nAdd a second Emby server?`);
          if (!ok) throw e;
          const updated = await embyUpsertSettings({ baseUrl: baseUrl.trim(), apiKey: apiKey.trim() }, true);
          setSelectedServerId(updated.serverId);
        } else {
          throw e;
        }
      }
      setApiKey("");
      await load();
    } catch (e) {
      setError(e instanceof ApiError ? e.message : "Failed to save Emby settings.");
    } finally {
      setLoading(false);
    }
  };

  const onTest = async () => {
    if (!selectedServerId) return;
    setLoading(true);
    setError(null);
    try {
      const result = await embyTestConnection("emby", selectedServerId);
      if (!result.ok) {
        setError(result.message ?? "Emby test failed.");
      }
    } catch (e) {
      setError(e instanceof ApiError ? e.message : "Failed to test Emby.");
    } finally {
      setLoading(false);
    }
  };

  const onSync = async () => {
    if (!selectedServerId) return;
    setLoading(true);
    setError(null);
    try {
      await embySyncLibrary("emby", selectedServerId);
      await load();
    } catch (e) {
      setError(e instanceof ApiError ? e.message : "Failed to sync Emby library.");
    } finally {
      setLoading(false);
    }
  };

  const selected = servers.find((s) => s.serverId === selectedServerId) ?? null;

  return (
    <>
      <div className="deck__toolbar" style={{ justifyContent: "flex-end" }}>
        <button type="button" className="app__navLink" onClick={() => void load()} disabled={loading}>
          Refresh
        </button>
      </div>
      {error ? <div className="deck__state deck__state--error">{error}</div> : null}
      {loading ? <div className="deck__state">Loading…</div> : null}

      <div className="deck__state" style={{ textAlign: "left" }}>
        <h3 style={{ marginTop: 0, marginBottom: "0.75rem" }}>Emby</h3>

        <div style={{ display: "flex", gap: "0.5rem", alignItems: "center", flexWrap: "wrap" }}>
          <span style={{ color: "#8c93a6", fontWeight: 700 }}>Configured servers</span>
          <select className="input" value={selectedServerId} onChange={(e) => setSelectedServerId(e.target.value)}>
            <option value="">(select)</option>
            {servers.map((s) => (
              <option key={s.serverId} value={s.serverId}>
                {s.name} ({s.serverId})
              </option>
            ))}
          </select>
          <button type="button" className="pill pill--neutral is-on" onClick={() => void onTest()} disabled={loading || !selectedServerId}>
            Test
          </button>
          <button type="button" className="pill pill--neutral is-on" onClick={() => void onSync()} disabled={loading || !selectedServerId}>
            Sync library
          </button>
          <div style={{ marginLeft: "auto", color: "#8c93a6", fontWeight: 700 }}>
            Last sync: {selected?.lastLibrarySyncUtc ?? ""}
          </div>
        </div>

        <div style={{ marginTop: "1rem", display: "grid", gridTemplateColumns: "1fr 1fr", gap: "0.75rem" }}>
          <div>
            <div style={{ color: "#8c93a6", fontWeight: 700, marginBottom: "0.25rem" }}>BaseUrl</div>
            <input className="input" value={baseUrl} onChange={(e) => setBaseUrl(e.target.value)} placeholder="http://localhost:8096/emby" />
          </div>
          <div>
            <div style={{ color: "#8c93a6", fontWeight: 700, marginBottom: "0.25rem" }}>ApiKey</div>
            <input className="input" type="password" value={apiKey} onChange={(e) => setApiKey(e.target.value)} placeholder="(required to save)" />
          </div>
        </div>

        <div style={{ marginTop: "1rem", display: "flex", gap: "0.5rem" }}>
          <button type="button" className="pill pill--neutral is-on" onClick={() => void onSave()} disabled={loading || !baseUrl.trim() || !apiKey.trim()}>
            Save
          </button>
          <div style={{ color: "#8c93a6", fontWeight: 700, alignSelf: "center" }}>
            Saving auto-populates ServerId from Emby.
          </div>
        </div>
      </div>
    </>
  );
}
