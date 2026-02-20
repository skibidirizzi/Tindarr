import { useCallback, useEffect, useRef, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import QRCode from "qrcode";
import SwipeCardComponent from "../components/SwipeCard";
import MovieDetailsModal from "../components/MovieDetailsModal";
import { getServiceScope, SERVICE_SCOPE_UPDATED_EVENT, type ServiceScope } from "../serviceScope";
import { castRoomQr, closeRoom, createRoom, endCastingSession, fetchRoomSwipeDeck, fetchSwipeDeck, getRoom, getRoomJoinUrl, getRoomMatches, getRoomQrCastUrl, joinRoom, listCastDevices, sendRoomSwipe } from "../api/client";
import type { CastDeviceDto, RoomJoinUrlResponse, RoomMatchesResponse, RoomStateResponse } from "../api/contracts";
import {
  ensureGoogleCastSdkLoaded,
  getCurrentCastDevice,
  initGoogleCastContext,
  loadMediaToCastSession,
  requestCastSession,
  shouldUseGoogleCastSdk,
  subscribeToCastMediaFinished,
  subscribeToCastSessionEnded
} from "../casting/googleCast";
import { ApiError } from "../api/http";
import { useAuth } from "../auth/AuthContext";
import type { SwipeAction, SwipeCard } from "../types";

const PREFETCH_THRESHOLD = 2;
const PREFETCH_LIMIT = 10;
const PREFETCH_EMPTY_RETRIES = 2;

export default function RoomPage() {
  const { user, loading: authLoading, guestLogin } = useAuth();
  const navigate = useNavigate();
  const { roomId } = useParams();
  const [currentScope, setCurrentScope] = useState<ServiceScope>(() => getServiceScope());

  const [joinInput, setJoinInput] = useState("");
  const [room, setRoom] = useState<RoomStateResponse | null>(null);
  const [joinUrl, setJoinUrl] = useState<RoomJoinUrlResponse | null>(null);
  const [qrDataUrl, setQrDataUrl] = useState<string | null>(null);
  const [qrLanDataUrl, setQrLanDataUrl] = useState<string | null>(null);
  const [qrWanDataUrl, setQrWanDataUrl] = useState<string | null>(null);
  const [joinUrlVariant, setJoinUrlVariant] = useState<"lan" | "wan">("lan");
  const [matches, setMatches] = useState<RoomMatchesResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  const [castDevices, setCastDevices] = useState<CastDeviceDto[]>([]);
  const [castDeviceId, setCastDeviceId] = useState<string>("");
  const [castLoading, setCastLoading] = useState(false);
  const [casting, setCasting] = useState(false);
  const [castMessage, setCastMessage] = useState<string | null>(null);
  const [castUiMode, setCastUiMode] = useState<"sdk" | "fallback">(() => (shouldUseGoogleCastSdk() ? "sdk" : "fallback"));

  const qrCastSessionIdRef = useRef<string | null>(null);
  const qrEndInFlightRef = useRef(false);

  async function endActiveQrCastSessionBestEffort(): Promise<void> {
    const sessionId = qrCastSessionIdRef.current;
    if (!sessionId) return;
    if (qrEndInFlightRef.current) return;

    qrEndInFlightRef.current = true;
    try {
      await endCastingSession(sessionId);
    } catch (e) {
      // best-effort
      void e;
    } finally {
      qrCastSessionIdRef.current = null;
      qrEndInFlightRef.current = false;
    }
  }

  useEffect(() => {
    if (castUiMode !== "sdk") return;

    const unsubEnded = subscribeToCastSessionEnded(() => {
      void endActiveQrCastSessionBestEffort();
    });
    const unsubFinished = subscribeToCastMediaFinished(() => {
      void endActiveQrCastSessionBestEffort();
    });

    return () => {
      unsubEnded();
      unsubFinished();
    };
  }, [castUiMode]);

  // When user hotswaps LAN/WAN and we're already casting the QR (SDK), re-load the cast URL so the TV shows the new QR.
  // Only run when variant actually changes (not on mount) to avoid racing with initial cast.
  const prevJoinUrlVariantRef = useRef<string | null>(null);
  useEffect(() => {
    if (castUiMode !== "sdk" || !roomId || !joinUrl?.lanUrl || !joinUrl?.wanUrl || !qrCastSessionIdRef.current) {
      prevJoinUrlVariantRef.current = joinUrlVariant;
      return;
    }
    const prev = prevJoinUrlVariantRef.current;
    prevJoinUrlVariantRef.current = joinUrlVariant;
    if (prev === null || prev === joinUrlVariant) {
      return;
    }
    let cancelled = false;
    void (async () => {
      try {
        const media = await getRoomQrCastUrl(roomId, joinUrlVariant);
        if (cancelled) return;
        await loadMediaToCastSession({
          url: media.url,
          contentType: media.contentType,
          title: media.title,
          subTitle: media.subTitle,
        });
      } catch {
        // best-effort; user can re-cast manually
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [castUiMode, joinUrlVariant, roomId, joinUrl?.lanUrl, joinUrl?.wanUrl]);

  const [cards, setCards] = useState<SwipeCard[]>([]);
  const [deckLoading, setDeckLoading] = useState(false);
  const [leaving, setLeaving] = useState<{ card: SwipeCard; action: SwipeAction; fromOffset?: { x: number; y: number } } | null>(null);
  const [selectedTmdbId, setSelectedTmdbId] = useState<number | null>(null);

  const prefetchInFlightRef = useRef(false);

  useEffect(() => {
    function handleScopeUpdated() {
      setCurrentScope(getServiceScope());
    }

    window.addEventListener(SERVICE_SCOPE_UPDATED_EVENT, handleScopeUpdated);
    return () => window.removeEventListener(SERVICE_SCOPE_UPDATED_EVENT, handleScopeUpdated);
  }, []);

  useEffect(() => {
    let cancelled = false;

    async function load() {
      if (!roomId) {
        setRoom(null);
        setJoinUrl(null);
        setQrDataUrl(null);
        setQrLanDataUrl(null);
        setQrWanDataUrl(null);
        setError(null);
        return;
      }

      if (authLoading) {
        return;
      }

      // Invite flow: if not logged in, show join prompt.
      if (!user) {
        setRoom(null);
        setJoinUrl(null);
        setQrDataUrl(null);
        setQrLanDataUrl(null);
        setQrWanDataUrl(null);
        setMatches(null);
        setError(null);
        return;
      }

      setLoading(true);
      setError(null);
      setQrDataUrl(null);
      setJoinUrl(null);
      setQrLanDataUrl(null);
      setQrWanDataUrl(null);

      try {
        // Joining is idempotent; do it first so the caller is present in the member list.
        await joinRoom(roomId);
        const state = await getRoom(roomId);

        if (cancelled) return;
        setRoom(state);
        if (state.isClosed) {
          const matchList = await getRoomMatches(roomId);
          if (cancelled) return;
          setMatches(matchList);
        } else {
          setMatches(null);
        }

        const isOwner = state.ownerUserId === user.userId;
        if (isOwner) {
          const url = await getRoomJoinUrl(roomId);
          if (cancelled) return;
          setJoinUrl(url);

          const qrOpts = { width: 240, margin: 1 };
          if (url.lanUrl && url.wanUrl) {
            const [lanQr, wanQr] = await Promise.all([
              QRCode.toDataURL(url.lanUrl, qrOpts),
              QRCode.toDataURL(url.wanUrl, qrOpts)
            ]);
            if (cancelled) return;
            setQrLanDataUrl(lanQr);
            setQrWanDataUrl(wanQr);
            setQrDataUrl(null);
          } else {
            const singleUrl = url.lanUrl ?? url.wanUrl ?? url.url;
            const dataUrl = await QRCode.toDataURL(singleUrl, qrOpts);
            if (cancelled) return;
            setQrDataUrl(dataUrl);
            setQrLanDataUrl(null);
            setQrWanDataUrl(null);
          }
        } else {
          setJoinUrl(null);
          setQrDataUrl(null);
          setQrLanDataUrl(null);
          setQrWanDataUrl(null);
        }
      } catch (e) {
        if (cancelled) return;
        if (e instanceof ApiError) {
          setError(e.message);
        } else {
          setError("Failed to load room.");
        }
      } finally {
        if (!cancelled) setLoading(false);
      }
    }

    void load();
    return () => {
      cancelled = true;
    };
  }, [authLoading, roomId, user]);

  function sleep(ms: number) {
    return new Promise((resolve) => setTimeout(resolve, ms));
  }

  const loadDeck = useCallback(async () => {
    if (!room) return;
    if (!room.isClosed) return;
    if (!user) return;

    setDeckLoading(true);
    setError(null);
    try {
      const maxEmptyRetries = 2;
      for (let attempt = 0; attempt <= maxEmptyRetries; attempt++) {
        try {
          const response = await fetchRoomSwipeDeck(room.roomId, 10);
          if (response.items.length > 0 || attempt === maxEmptyRetries) {
            setCards(response.items);
            break;
          }

          await sleep(250 * (attempt + 1));
        } catch (e) {
          // Backward-compat: older APIs won't have /rooms/{id}/swipedeck yet.
          if (e instanceof ApiError && e.status === 404) {
            const response = await fetchSwipeDeck(10, room.serviceType, room.serverId);
            setCards(response.items);
            break;
          }

          throw e;
        }
      }
    } catch (e) {
      if (e instanceof ApiError) {
        setError(e.message);
      } else {
        setError("Failed to load room swipedeck.");
      }
    } finally {
      setDeckLoading(false);
    }
  }, [room, user]);

  const appendUniqueCards = useCallback((incoming: SwipeCard[]) => {
    if (!incoming.length) return;
    setCards((prev) => {
      if (!prev.length) return incoming;
      const existing = new Set(prev.map((c) => c.tmdbId));
      const deduped = incoming.filter((c) => !existing.has(c.tmdbId));
      return deduped.length ? [...prev, ...deduped] : prev;
    });
  }, []);

  const prefetchMoreCards = useCallback(async () => {
    if (!room) return;
    if (!room.isClosed) return;
    if (!user) return;
    if (deckLoading) return;
    if (error) return;
    if (prefetchInFlightRef.current) return;

    prefetchInFlightRef.current = true;
    try {
      for (let attempt = 0; attempt <= PREFETCH_EMPTY_RETRIES; attempt++) {
        try {
          const response = await fetchRoomSwipeDeck(room.roomId, PREFETCH_LIMIT);
          if (response.items.length > 0) {
            appendUniqueCards(response.items);
            break;
          }
        } catch (e) {
          // Backward-compat: older APIs won't have /rooms/{id}/swipedeck yet.
          if (e instanceof ApiError && e.status === 404) {
            const response = await fetchSwipeDeck(PREFETCH_LIMIT, room.serviceType, room.serverId);
            if (response.items.length > 0) {
              appendUniqueCards(response.items);
              break;
            }
          } else {
            throw e;
          }
        }

        if (attempt !== PREFETCH_EMPTY_RETRIES) {
          await sleep(250 * (attempt + 1));
        }
      }
    } catch (e) {
      if (e instanceof ApiError) {
        setError(e.message);
      } else {
        setError("Failed to load room swipedeck.");
      }
    } finally {
      prefetchInFlightRef.current = false;
    }
  }, [appendUniqueCards, deckLoading, error, room, user]);

  useEffect(() => {
    if (!room || !room.isClosed) return;
    void loadDeck();
  }, [loadDeck, room]);

  useEffect(() => {
    if (!room?.isClosed || cards.length >= PREFETCH_THRESHOLD) return;
    void prefetchMoreCards();
  }, [cards.length, prefetchMoreCards, room?.isClosed]);

  const requestSwipe = useCallback(
    (action: SwipeAction, fromOffset?: { x: number; y: number }) => {
      if (!roomId) return;
      const active = cards[0];
      if (!active) return;
      if (leaving) return;
      if (selectedTmdbId) return;

      setLeaving({ card: active, action, fromOffset });
      setCards((prev) => prev.slice(1));

      void (async () => {
        try {
          await sendRoomSwipe(roomId, active.tmdbId, action);
        } catch (e) {
          setError(e instanceof Error ? e.message : "Failed to send room swipe");
          void loadDeck();
        }
      })();
    },
    [cards, leaving, loadDeck, roomId, selectedTmdbId]
  );

  async function refreshMatches() {
    if (!roomId) return;
    setError(null);
    try {
      const matchList = await getRoomMatches(roomId);
      setMatches(matchList);
    } catch (e) {
      if (e instanceof ApiError) {
        setError(e.message);
      } else {
        setError("Failed to load matches.");
      }
    }
  }

  async function onCloseRoom() {
    if (!roomId) return;
    if (!room) return;

    setError(null);
    setLoading(true);
    try {
      const updated = await closeRoom(roomId);
      setRoom(updated);
      await refreshMatches();

			// Once the room is closed, the invite QR is no longer needed.
			await endActiveQrCastSessionBestEffort();
    } catch (e) {
      if (e instanceof ApiError) {
        setError(e.message);
      } else {
        setError("Failed to close room.");
      }
    } finally {
      setLoading(false);
    }
  }

  async function onLoadCastDevices() {
    if (castUiMode === "sdk") return;
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

  async function onCastQr() {
    if (!roomId) return;
    if (castUiMode === "fallback" && !castDeviceId) return;

    setCasting(true);
    setCastMessage(null);
    setError(null);
    try {
      if (castUiMode === "sdk") {
        const ok = await ensureGoogleCastSdkLoaded();
        if (!ok) {
          setCastUiMode("fallback");
          setCastMessage("Google Cast isn’t available in this browser. Use the device picker below.");
          return;
        }

        initGoogleCastContext();

        const current = getCurrentCastDevice();
        if (!current) {
          await requestCastSession();
        }

        // Don't pass variant on initial cast so server uses default (QR appears on TV). Variant is used when re-loading after LAN/WAN toggle.
        const media = await getRoomQrCastUrl(roomId);

        qrCastSessionIdRef.current = media.sessionId ?? null;
        try {
          await loadMediaToCastSession({
            url: media.url,
            contentType: media.contentType,
            title: media.title,
            subTitle: media.subTitle,
          });
        } catch (e) {
          await endActiveQrCastSessionBestEffort();
          throw e;
        }

        setCastMessage("Casting QR…");
        return;
      }

      // Don't pass variant on initial cast so server uses default (QR appears on TV). Variant is only for hotswap re-cast.
      await castRoomQr(roomId, castDeviceId);
      setCastMessage("Casting QR…");
    } catch (e) {
      if (e instanceof ApiError) {
        setError(e.message);
      } else {
        setError("Failed to cast QR. If you are running on localhost, configure a LAN join address (Admin Console) and ensure the API is reachable from your Chromecast (bind to 0.0.0.0 / LAN IP)." );
      }
    } finally {
      setCasting(false);
    }
  }

  async function onCreate() {
    setLoading(true);
    setError(null);
    try {
      const scope = getServiceScope();
      const created = await createRoom({ serviceType: scope.serviceType, serverId: scope.serverId });
      navigate(`/rooms/${created.roomId}`, { replace: true });
    } catch (e) {
      if (e instanceof ApiError) {
        setError(e.message);
      } else {
        setError("Failed to create room.");
      }
    } finally {
      setLoading(false);
    }
  }

  async function onContinueAsGuest() {
    if (!roomId) return;
    setError(null);
    setLoading(true);
    try {
      await guestLogin(roomId);
      // Reload effect will join+load.
    } catch (e) {
      if (e instanceof ApiError) {
        setError(e.message);
      } else {
        setError("Failed to start guest session.");
      }
    } finally {
      setLoading(false);
    }
  }

  function onJoinNavigate() {
    const id = joinInput.trim();
    if (!id) return;
    navigate(`/rooms/${encodeURIComponent(id)}`);
  }

  if (!roomId) {
    return (
      <section className="deck">
        <div className="deck__state" style={{ textAlign: "left" }}>
          <h2 style={{ marginTop: 0, marginBottom: "0.25rem" }}>Rooms</h2>
          <div className="field__label">Current scope</div>
          <div style={{ marginTop: "0.25rem" }}>{currentScope.serviceType}/{currentScope.serverId}</div>
        </div>

        <div className="deck__state" style={{ textAlign: "left" }}>
          <h3 style={{ marginTop: 0, marginBottom: "0.75rem" }}>Create a room</h3>
          <button type="button" className="button button--like" onClick={onCreate} disabled={loading}>
            Create
          </button>
          {error ? <div style={{ marginTop: "0.75rem" }}>{error}</div> : null}
        </div>

        <div className="deck__state" style={{ textAlign: "left" }}>
          <h3 style={{ marginTop: 0, marginBottom: "0.75rem" }}>Join a room</h3>
          <div className="field" style={{ marginBottom: "0.75rem" }}>
            <label className="field__label" htmlFor="roomId">Room ID</label>
            <input
              id="roomId"
              className="field__input"
              value={joinInput}
              onChange={(e) => setJoinInput(e.target.value)}
              placeholder="Paste a room id"
              inputMode="text"
            />
          </div>
          <button type="button" className="button" onClick={onJoinNavigate} disabled={loading || !joinInput.trim()}>
            Join
          </button>
        </div>
      </section>
    );
  }

  // Invite route without auth: offer guest join or login.
  if (!authLoading && !user) {
    return (
      <section className="deck">
        <div className="deck__state" style={{ textAlign: "left" }}>
          <h2 style={{ marginTop: 0, marginBottom: "0.25rem" }}>Join Room</h2>
          <div className="field__label">Room ID</div>
          <div style={{ marginTop: "0.25rem", wordBreak: "break-all" }}>{roomId}</div>
          {error ? <div style={{ marginTop: "0.75rem" }}>{error}</div> : null}
        </div>

        <div className="deck__state" style={{ textAlign: "left" }}>
          <h3 style={{ marginTop: 0, marginBottom: "0.75rem" }}>Continue</h3>
          <div style={{ display: "flex", gap: "0.75rem", flexWrap: "wrap" }}>
            <button type="button" className="button button--like" onClick={onContinueAsGuest} disabled={loading}>
              Continue as guest
            </button>
            <button type="button" className="button" onClick={() => navigate("/login")} disabled={loading}>
              Login
            </button>
          </div>
        </div>
      </section>
    );
  }

  return (
    <section className="deck">
      <div className="deck__state" style={{ textAlign: "left" }}>
        <h2 style={{ marginTop: 0, marginBottom: "0.25rem" }}>Room</h2>
        <div className="field__label">Room ID</div>
        <div style={{ marginTop: "0.25rem", wordBreak: "break-all" }}>{roomId}</div>

        {loading ? <div style={{ marginTop: "0.75rem" }}>Loading…</div> : null}
        {error ? <div style={{ marginTop: "0.75rem" }}>{error}</div> : null}
      </div>

      {room && user && room.ownerUserId === user.userId && room.isClosed ? (
        <div className="deck__state" style={{ textAlign: "left" }}>
          Room is closed to new users.
        </div>
      ) : null}

      {room ? (
        <div className="deck__state" style={{ textAlign: "left" }}>
          <h3 style={{ marginTop: 0, marginBottom: "0.75rem" }}>Lobby</h3>
          <div className="field__label">Scope</div>
          <div style={{ marginTop: "0.25rem" }}>{room.serviceType}/{room.serverId}</div>
          <div className="field__label" style={{ marginTop: "0.75rem" }}>Owner</div>
          <div style={{ marginTop: "0.25rem" }}>{room.ownerUserId}{user?.userId === room.ownerUserId ? " (you)" : ""}</div>
          <div className="field__label" style={{ marginTop: "0.75rem" }}>Members</div>
          <ul style={{ margin: "0.25rem 0 0", paddingLeft: "1.25rem" }}>
            {room.members.map((m) => (
              <li key={m.userId}>{m.userId}</li>
            ))}
          </ul>
        </div>
      ) : null}

      {room && !room.isClosed ? (
        <div className="deck__state" style={{ textAlign: "left" }}>
          <h3 style={{ marginTop: 0, marginBottom: "0.75rem" }}>Swipe</h3>
          <p style={{ margin: 0 }}>Swipe will be available once the room is closed to new users.</p>
        </div>
      ) : null}

      {room && room.isClosed ? (
        <div className="deck__state deck--swipe" style={{ textAlign: "left" }}>
          <h3 style={{ marginTop: 0, marginBottom: "0.75rem" }}>Swipe</h3>
          <div className="deck__toolbar" style={{ justifyContent: "space-between" }}>
            <button type="button" className="button button--ghost" onClick={loadDeck} disabled={deckLoading}>
              Refresh deck
            </button>
          </div>

          {deckLoading ? <div className="deck__state">Loading swipedeck…</div> : null}
          {!deckLoading && cards.length === 0 ? <div className="deck__state">No movies returned.</div> : null}

          <div className="deck__stack">
            {cards
              .slice(0, 3)
              .map((card, index) => ({ card, index }))
              .reverse()
              .map(({ card, index }) => (
                <SwipeCardComponent
                  key={card.tmdbId}
                  card={card}
                  active={index === 0 && !Boolean(leaving)}
                  onSwipe={requestSwipe}
                  onOpenDetails={(id) => setSelectedTmdbId(id)}
                />
              ))}
            {leaving ? (
              <SwipeCardComponent
                key={`leaving-${leaving.card.tmdbId}-${leaving.action}`}
                card={leaving.card}
                active={false}
                leavingAction={leaving.action}
                leavingFrom={leaving.fromOffset ?? null}
                onLeaveDone={() => setLeaving(null)}
              />
            ) : null}
          </div>

          <div className="deck__actions">
            <button className="button button--nope" onClick={() => requestSwipe("Nope")} disabled={!cards[0] || Boolean(leaving)}>
              Nope
            </button>
            <button className="button button--skip" onClick={() => requestSwipe("Skip")} disabled={!cards[0] || Boolean(leaving)}>
              Skip
            </button>
            <button className="button button--super" onClick={() => requestSwipe("Superlike")} disabled={!cards[0] || Boolean(leaving)}>
              Superlike
            </button>
            <button className="button button--like" onClick={() => requestSwipe("Like")} disabled={!cards[0] || Boolean(leaving)}>
              Like
            </button>
          </div>
        </div>
      ) : null}

      {joinUrl && room && user && room.ownerUserId === user.userId ? (
        <div className="deck__state" style={{ textAlign: "left" }}>
          <h3 style={{ marginTop: 0, marginBottom: "0.75rem" }}>Invite</h3>
          {joinUrl.lanUrl && joinUrl.wanUrl ? (
            <>
              <div className="field__label" style={{ marginBottom: "0.25rem" }}>Join URL (LAN / WAN)</div>
              <div style={{ display: "flex", gap: "0.5rem", marginBottom: "0.5rem" }}>
                <button
                  type="button"
                  className={`button button--ghost ${joinUrlVariant === "lan" ? "is-active" : ""}`.trim()}
                  onClick={() => setJoinUrlVariant("lan")}
                >
                  LAN
                </button>
                <button
                  type="button"
                  className={`button button--ghost ${joinUrlVariant === "wan" ? "is-active" : ""}`.trim()}
                  onClick={() => setJoinUrlVariant("wan")}
                >
                  WAN
                </button>
              </div>
              <div style={{ marginTop: "0.25rem", wordBreak: "break-all" }}>
                {joinUrlVariant === "lan" ? joinUrl.lanUrl : joinUrl.wanUrl}
              </div>
              {(joinUrlVariant === "lan" ? qrLanDataUrl : qrWanDataUrl) ? (
                <div style={{ marginTop: "0.75rem" }}>
                  <img src={joinUrlVariant === "lan" ? qrLanDataUrl : qrWanDataUrl} width={240} height={240} alt={`Room join QR (${joinUrlVariant})`} />
                </div>
              ) : null}
            </>
          ) : (
            <>
              <div className="field__label">Join URL</div>
              <div style={{ marginTop: "0.25rem", wordBreak: "break-all" }}>
                {joinUrl.lanUrl ?? joinUrl.wanUrl ?? joinUrl.url}
              </div>
              {qrDataUrl ? (
                <div style={{ marginTop: "0.75rem" }}>
                  <img src={qrDataUrl} width={240} height={240} alt="Room join QR code" />
                </div>
              ) : null}
            </>
          )}

          {room?.isClosed ? (
            <div style={{ marginTop: "0.75rem", color: "#cbd0de" }}>
              Room is closed to new users.
            </div>
          ) : null}

          {room && user && room.ownerUserId === user.userId && !room.isClosed ? (
            <div style={{ marginTop: "0.75rem" }}>
              <button type="button" className="button button--ghost" onClick={onCloseRoom} disabled={loading}>
                Close room to new users
              </button>
            </div>
          ) : null}

          <div style={{ marginTop: "0.75rem" }}>
            <div className="field__label">Cast QR</div>
            <div style={{ marginTop: "0.25rem", display: "flex", gap: "0.75rem", flexWrap: "wrap", alignItems: "center" }}>
              {castUiMode === "sdk" ? (
                <button type="button" className="button" onClick={onCastQr} disabled={casting}>
                  {casting ? "Casting…" : "Cast QR"}
                </button>
              ) : (
                <>
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
                  <button type="button" className="button" onClick={onCastQr} disabled={casting || castLoading || !castDeviceId}>
                    {casting ? "Casting…" : "Cast QR"}
                  </button>
                </>
              )}
            </div>
            {castMessage ? <div style={{ marginTop: "0.5rem" }}>{castMessage}</div> : null}
          </div>
        </div>
      ) : null}

      {room && !room.isClosed ? (
        <div className="deck__state" style={{ textAlign: "left" }}>
          <h3 style={{ marginTop: 0, marginBottom: "0.75rem" }}>Matches</h3>
          <p style={{ margin: 0 }}>Matches will be available once the room is closed to new users.</p>
        </div>
      ) : null}

      {room && room.isClosed ? (
        <div className="deck__state" style={{ textAlign: "left" }}>
          <h3 style={{ marginTop: 0, marginBottom: "0.75rem" }}>Matches</h3>
          <button type="button" className="button" onClick={refreshMatches} disabled={loading}>
            Refresh
          </button>
          <div style={{ marginTop: "0.75rem" }}>
            {matches?.tmdbIds.length ? (
              <ul style={{ margin: 0, paddingLeft: "1.25rem" }}>
                {matches.tmdbIds.map((id) => (
                  <li key={id}>{id}</li>
                ))}
              </ul>
            ) : (
              <div>No matches yet.</div>
            )}
          </div>
        </div>
      ) : null}

      {selectedTmdbId ? <MovieDetailsModal tmdbId={selectedTmdbId} onClose={() => setSelectedTmdbId(null)} /> : null}
    </section>
  );
}
