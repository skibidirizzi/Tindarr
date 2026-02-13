import { useCallback, useEffect, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import QRCode from "qrcode";
import SwipeCardComponent from "../components/SwipeCard";
import MovieDetailsModal from "../components/MovieDetailsModal";
import { getServiceScope, SERVICE_SCOPE_UPDATED_EVENT, type ServiceScope } from "../serviceScope";
import { closeRoom, createRoom, fetchRoomSwipeDeck, fetchSwipeDeck, getRoom, getRoomJoinUrl, getRoomMatches, joinRoom, sendRoomSwipe } from "../api/client";
import type { RoomJoinUrlResponse, RoomMatchesResponse, RoomStateResponse } from "../api/contracts";
import { ApiError } from "../api/http";
import { useAuth } from "../auth/AuthContext";
import type { SwipeAction, SwipeCard } from "../types";

export default function RoomPage() {
  const { user, loading: authLoading, guestLogin } = useAuth();
  const navigate = useNavigate();
  const { roomId } = useParams();
  const [currentScope, setCurrentScope] = useState<ServiceScope>(() => getServiceScope());

  const [joinInput, setJoinInput] = useState("");
  const [room, setRoom] = useState<RoomStateResponse | null>(null);
  const [joinUrl, setJoinUrl] = useState<RoomJoinUrlResponse | null>(null);
  const [qrDataUrl, setQrDataUrl] = useState<string | null>(null);
  const [matches, setMatches] = useState<RoomMatchesResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  const [cards, setCards] = useState<SwipeCard[]>([]);
  const [deckLoading, setDeckLoading] = useState(false);
  const [leaving, setLeaving] = useState<{ card: SwipeCard; action: SwipeAction; fromOffset?: { x: number; y: number } } | null>(null);
  const [selectedTmdbId, setSelectedTmdbId] = useState<number | null>(null);

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
        setMatches(null);
        setError(null);
        return;
      }

      setLoading(true);
      setError(null);
      setQrDataUrl(null);
      setJoinUrl(null);

      try {
        // Joining is idempotent; do it first so the caller is present in the member list.
        await joinRoom(roomId);
        const state = await getRoom(roomId);
        const matchList = await getRoomMatches(roomId);

        if (cancelled) return;
        setRoom(state);
        setMatches(matchList);

        const isOwner = state.ownerUserId === user.userId;
        if (isOwner) {
          const url = await getRoomJoinUrl(roomId);
          if (cancelled) return;
          setJoinUrl(url);

          const dataUrl = await QRCode.toDataURL(url.url, { width: 240, margin: 1 });
          if (cancelled) return;
          setQrDataUrl(dataUrl);
        } else {
          setJoinUrl(null);
          setQrDataUrl(null);
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

  useEffect(() => {
    if (!room) return;
    void loadDeck();
  }, [loadDeck, room]);

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
      await guestLogin();
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

      {room ? (
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
          <div className="field__label">Join URL</div>
          <div style={{ marginTop: "0.25rem", wordBreak: "break-all" }}>{joinUrl.url}</div>

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

          {qrDataUrl ? (
            <div style={{ marginTop: "0.75rem" }}>
              <img src={qrDataUrl} width={240} height={240} alt="Room join QR code" />
            </div>
          ) : null}
        </div>
      ) : null}

      {matches ? (
        <div className="deck__state" style={{ textAlign: "left" }}>
          <h3 style={{ marginTop: 0, marginBottom: "0.75rem" }}>Matches</h3>
          <button type="button" className="button" onClick={refreshMatches} disabled={loading}>
            Refresh
          </button>
          <div style={{ marginTop: "0.75rem" }}>
            {matches.tmdbIds.length ? (
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
