import { useCallback, useEffect, useMemo, useState } from "react";
import { Link } from "react-router-dom";
import SwipeCardComponent from "../components/SwipeCard";
import { fetchSwipeDeck, sendSwipe, undoSwipe } from "../api/client";
import { ApiError } from "../api/http";
import type { SwipeAction, SwipeCard } from "../types";

const ACTION_LABELS: Record<SwipeAction, string> = {
  Like: "Like",
  Nope: "Nope",
  Skip: "Skip",
  Superlike: "Superlike"
};

function sleep(ms: number) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

export default function SwipeDeckPage() {
  const [cards, setCards] = useState<SwipeCard[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [lastAction, setLastAction] = useState<SwipeAction | null>(null);

  const activeCard = cards[0];

  const loadDeck = useCallback(async () => {
    try {
      setLoading(true);
      setError(null);
      const maxEmptyRetries = 2; // "refresh twice" when we get 0 items

      for (let attempt = 0; attempt <= maxEmptyRetries; attempt++) {
        const response = await fetchSwipeDeck();

        if (response.items.length > 0 || attempt === maxEmptyRetries) {
          setCards(response.items);
          break;
        }

        // Short backoff before retrying an empty deck.
        await sleep(250 * (attempt + 1));
      }
    } catch (err) {
      if (err instanceof ApiError && err.status === 401) {
        setError("You’re not logged in. Please login and try again.");
      } else {
        setError(err instanceof Error ? err.message : "Failed to load swipedeck");
      }
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    loadDeck();
  }, [loadDeck]);

  // Live-reload swipedeck when preferences are saved (Preferences modal dispatches this).
  useEffect(() => {
    function handlePreferencesUpdated() {
      void loadDeck();
    }

    window.addEventListener("tindarr:preferencesUpdated", handlePreferencesUpdated);
    return () => window.removeEventListener("tindarr:preferencesUpdated", handlePreferencesUpdated);
  }, [loadDeck]);

  useEffect(() => {
    function handleKey(event: KeyboardEvent) {
      if (!activeCard) return;
      if (event.key === "ArrowRight") {
        handleSwipe("Like");
      } else if (event.key === "ArrowLeft") {
        handleSwipe("Nope");
      } else if (event.key === "ArrowUp") {
        handleSwipe("Superlike");
      } else if (event.key === "ArrowDown") {
        handleSwipe("Skip");
      } else if (event.key === "Backspace") {
        handleUndo();
      }
    }

    window.addEventListener("keydown", handleKey);
    return () => window.removeEventListener("keydown", handleKey);
  }, [activeCard]);

  async function handleSwipe(action: SwipeAction) {
    if (!activeCard) return;
    try {
      await sendSwipe(activeCard.tmdbId, action);
      setLastAction(action);
      setCards((prev: SwipeCard[]) => prev.slice(1));
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to send swipe");
    }
  }

  async function handleUndo() {
    try {
      const response = (await undoSwipe()) as { undone: boolean };
      if (response.undone) {
        await loadDeck();
        setLastAction(null);
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to undo swipe");
    }
  }

  const actionButtons = useMemo(
    () => [
      { action: "Nope" as SwipeAction, className: "button button--nope" },
      { action: "Skip" as SwipeAction, className: "button button--skip" },
      { action: "Superlike" as SwipeAction, className: "button button--super" },
      { action: "Like" as SwipeAction, className: "button button--like" }
    ],
    []
  );

  const lastActionLabel = lastAction ? ACTION_LABELS[lastAction as SwipeAction] : null;

  return (
    <section className="deck">
      <div className="deck__toolbar">
        <button className="button button--ghost" onClick={loadDeck} disabled={loading}>
          Refresh deck
        </button>
        <button className="button button--ghost" onClick={handleUndo}>
          Undo
        </button>
        {lastActionLabel ? <span className="deck__last">Last: {lastActionLabel}</span> : null}
      </div>

      {loading ? <div className="deck__state">Loading swipedeck…</div> : null}
      {error ? (
        <div className="deck__state deck__state--error">
          {error} {error.includes("login") ? <Link to="/login">Go to login</Link> : null}
        </div>
      ) : null}

      {!loading && !error && cards.length === 0 ? (
        <div className="deck__state">
          No movies returned.
          <div style={{ marginTop: "0.5rem", color: "#8c93a6" }}>
            If this is unexpected, check your TMDB configuration (TMDB_API_KEY) and preferences, then refresh.
          </div>
        </div>
      ) : null}

      <div className="deck__stack">
        {cards
          .slice(0, 3)
          .map((card: SwipeCard, index: number) => (
            <SwipeCardComponent
              key={card.tmdbId}
              card={card}
              active={index === 0}
              onSwipe={handleSwipe}
            />
          ))}
      </div>

      <div className="deck__actions">
        {actionButtons.map((button) => (
          <button
            key={button.action}
            className={button.className}
            onClick={() => handleSwipe(button.action)}
            disabled={!activeCard}
          >
            {ACTION_LABELS[button.action]}
          </button>
        ))}
      </div>
    </section>
  );
}
