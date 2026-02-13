import { useCallback, useEffect, useMemo, useState } from "react";
import { Link } from "react-router-dom";
import SwipeCardComponent from "../components/SwipeCard";
import MovieDetailsModal from "../components/MovieDetailsModal";
import { fetchSwipeDeck, sendSwipe, undoSwipe } from "../api/client";
import { ApiError } from "../api/http";
import { SERVICE_SCOPE_UPDATED_EVENT } from "../serviceScope";
import { isMobileDevice } from "../device";
import { useAuth } from "../auth/AuthContext";
import { hasCompletedSwipeTutorial, setSwipeTutorialCompleted } from "../onboarding/swipeTutorial";
import type { SwipeAction, SwipeCard } from "../types";

const ACTION_LABELS: Record<SwipeAction, string> = {
  Like: "Like",
  Nope: "Nope",
  Skip: "Skip",
  Superlike: "Superlike"
};

const TUTORIAL_STEPS: Array<
  | { kind: "tapDetails"; instruction: string }
  | { kind: "closeDetails"; instruction: string }
  | { kind: "swipe"; action: SwipeAction; instruction: string }
  | { kind: "undo"; instruction: string }
> = [
  { kind: "tapDetails", instruction: "Tap the card to see details" },
  { kind: "closeDetails", instruction: "Close the details" },
  { kind: "swipe", action: "Like", instruction: "Swipe right to Like (→)" },
  { kind: "swipe", action: "Nope", instruction: "Swipe left to Nope (←)" },
  { kind: "swipe", action: "Superlike", instruction: "Swipe up to Superlike (↑)" },
  { kind: "swipe", action: "Skip", instruction: "Swipe down to Skip (↓)" },
  { kind: "undo", instruction: "Press Undo / Backspace" }
];

function sleep(ms: number) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

export default function SwipeDeckPage() {
  const { user } = useAuth();
  const isMobile = isMobileDevice();
  const [cards, setCards] = useState<SwipeCard[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [lastAction, setLastAction] = useState<SwipeAction | null>(null);
  const [leaving, setLeaving] = useState<{ card: SwipeCard; action: SwipeAction; fromOffset?: { x: number; y: number } } | null>(null);
  const [selectedTmdbId, setSelectedTmdbId] = useState<number | null>(null);

  const [tutorialActive, setTutorialActive] = useState<boolean>(() => !hasCompletedSwipeTutorial(user.userId));
  const [tutorialStepIndex, setTutorialStepIndex] = useState<number>(0);
  const [tutorialHistory, setTutorialHistory] = useState<Array<{ card: SwipeCard; action: SwipeAction }>>([]);
  const [tutorialNote, setTutorialNote] = useState<string | null>(null);

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

  const requestSwipe = useCallback(
    (action: SwipeAction, fromOffset?: { x: number; y: number }) => {
      if (!activeCard) return;
      if (leaving) return;
      if (selectedTmdbId) return;

      setError(null);
      if (tutorialActive) {
        const step = TUTORIAL_STEPS[tutorialStepIndex];
        if (!step) return;
        if (step.kind !== "swipe") {
          setTutorialNote(`Tutorial: ${tutorialStepIndex + 1}/${TUTORIAL_STEPS.length} — ${step.instruction}`);
          return;
        }

        if (step.action !== action) {
          setTutorialNote(`Tutorial: ${tutorialStepIndex + 1}/${TUTORIAL_STEPS.length} — ${step.instruction}`);
          return;
        }

        setTutorialNote(null);
        setTutorialHistory((prev) => [...prev, { card: activeCard, action }]);
        setTutorialStepIndex((prev) => Math.min(prev + 1, TUTORIAL_STEPS.length));
      }
      setLeaving({ card: activeCard, action, fromOffset });
      setCards((prev: SwipeCard[]) => prev.slice(1));
      setLastAction(action);

      void (async () => {
        if (tutorialActive) return;
        try {
          await sendSwipe(activeCard.tmdbId, action);
        } catch (err) {
          setError(err instanceof Error ? err.message : "Failed to send swipe");
          // Re-sync with server in case we got out of sync.
          void loadDeck();
        }
      })();
    },
    [activeCard, leaving, loadDeck, selectedTmdbId, tutorialActive, tutorialStepIndex]
  );

  const handleUndo = useCallback(async () => {
    if (leaving) return;
    if (selectedTmdbId) return;

    if (tutorialActive) {
      const step = TUTORIAL_STEPS[tutorialStepIndex];
      if (!step) return;
      if (step.kind !== "undo") {
        setTutorialNote(`Tutorial: ${tutorialStepIndex + 1}/${TUTORIAL_STEPS.length} — ${step.instruction}`);
        return;
      }

      setTutorialNote(null);
      setLastAction(null);

      setTutorialHistory((prev) => {
        const last = prev[prev.length - 1];
        if (!last) {
          setTutorialNote(`Tutorial: ${tutorialStepIndex + 1}/${TUTORIAL_STEPS.length} — Swipe a card first`);
          return prev;
        }
        setCards((cardsPrev) => [last.card, ...cardsPrev]);
        setTutorialStepIndex((idxPrev) => Math.min(idxPrev + 1, TUTORIAL_STEPS.length));
        return prev.slice(0, -1);
      });

      return;
    }

    try {
      const response = (await undoSwipe()) as { undone: boolean };
      if (response.undone) {
        await loadDeck();
        setLastAction(null);
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to undo swipe");
    }
  }, [leaving, loadDeck, selectedTmdbId, tutorialActive, tutorialStepIndex]);

  const completeTutorial = useCallback(() => {
    setSwipeTutorialCompleted(user.userId);
    setTutorialActive(false);
    setTutorialNote(null);
    setTutorialStepIndex(0);
    setTutorialHistory([]);
    setLastAction(null);
    setLeaving(null);
    setSelectedTmdbId(null);
    void loadDeck();
  }, [loadDeck, user.userId]);

  useEffect(() => {
    if (!tutorialActive) return;
    const step = TUTORIAL_STEPS[tutorialStepIndex];
    if (!step) return;

    if (step.kind === "tapDetails") {
      if (selectedTmdbId) {
        setTutorialNote(null);
        setTutorialStepIndex((prev) => Math.min(prev + 1, TUTORIAL_STEPS.length));
      }
      return;
    }

    if (step.kind === "closeDetails") {
      if (!selectedTmdbId) {
        setTutorialNote(null);
        setTutorialStepIndex((prev) => Math.min(prev + 1, TUTORIAL_STEPS.length));
      }
      return;
    }
  }, [selectedTmdbId, tutorialActive, tutorialStepIndex]);

  useEffect(() => {
    if (!tutorialActive) return;
    if (tutorialStepIndex < TUTORIAL_STEPS.length) return;
    completeTutorial();
  }, [completeTutorial, tutorialActive, tutorialStepIndex]);

  useEffect(() => {
    const html = document.documentElement;
    const body = document.body;

    const prevHtmlOverflow = html.style.overflow;
    const prevBodyOverflow = body.style.overflow;
    const prevHtmlOverscroll = (html.style as CSSStyleDeclaration & { overscrollBehavior?: string }).overscrollBehavior;
    const prevBodyOverscroll = (body.style as CSSStyleDeclaration & { overscrollBehavior?: string }).overscrollBehavior;

    html.style.overflow = "hidden";
    body.style.overflow = "hidden";
    (html.style as CSSStyleDeclaration & { overscrollBehavior?: string }).overscrollBehavior = "none";
    (body.style as CSSStyleDeclaration & { overscrollBehavior?: string }).overscrollBehavior = "none";
    body.classList.add("is-swipe-page");

    return () => {
      body.classList.remove("is-swipe-page");
      html.style.overflow = prevHtmlOverflow;
      body.style.overflow = prevBodyOverflow;
      (html.style as CSSStyleDeclaration & { overscrollBehavior?: string }).overscrollBehavior = prevHtmlOverscroll ?? "";
      (body.style as CSSStyleDeclaration & { overscrollBehavior?: string }).overscrollBehavior = prevBodyOverscroll ?? "";
    };
  }, []);

  useEffect(() => {
    loadDeck();
  }, [loadDeck]);

  // In tutorial mode we don't persist swipes, so the server deck can still be full while the local deck drains.
  // Auto-refresh to keep the tutorial moving.
  useEffect(() => {
    if (!tutorialActive) return;
    if (loading) return;
    if (error) return;
    if (cards.length > 0) return;
    void loadDeck();
  }, [cards.length, error, loadDeck, loading, tutorialActive]);

  // Live-reload swipedeck when preferences are saved (Preferences modal dispatches this).
  useEffect(() => {
    function handlePreferencesUpdated() {
      void loadDeck();
    }

    window.addEventListener("tindarr:preferencesUpdated", handlePreferencesUpdated);
    return () => window.removeEventListener("tindarr:preferencesUpdated", handlePreferencesUpdated);
  }, [loadDeck]);

  // Live-reload when service scope changes (e.g., tmdb -> plex/server).
  useEffect(() => {
    function handleScopeUpdated() {
      setLastAction(null);
      void loadDeck();
    }

    window.addEventListener(SERVICE_SCOPE_UPDATED_EVENT, handleScopeUpdated);
    return () => window.removeEventListener(SERVICE_SCOPE_UPDATED_EVENT, handleScopeUpdated);
  }, [loadDeck]);

  useEffect(() => {
    function handleKey(event: KeyboardEvent) {
      if (!activeCard) return;
      if (event.key === "ArrowRight") {
        requestSwipe("Like");
      } else if (event.key === "ArrowLeft") {
        requestSwipe("Nope");
      } else if (event.key === "ArrowUp") {
        requestSwipe("Superlike");
      } else if (event.key === "ArrowDown") {
        requestSwipe("Skip");
      } else if (event.key === "Backspace") {
        handleUndo();
      }
    }

    window.addEventListener("keydown", handleKey);
    return () => window.removeEventListener("keydown", handleKey);
  }, [activeCard, requestSwipe, handleUndo]);

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
  const deckBusy = Boolean(leaving);

  return (
    <section className="deck deck--swipe">
      <div className="deck__toolbar">
        <button className="button button--ghost" onClick={loadDeck} disabled={loading}>
          Refresh deck
        </button>
        <button className="button button--ghost" onClick={handleUndo}>
          Undo
        </button>
        {lastActionLabel ? <span className="deck__last">Last: {lastActionLabel}</span> : null}
      </div>

      {tutorialActive ? (
        <div className="deck__state" role="region" aria-label="Swipe tutorial" style={{ padding: "0.75rem 1rem" }}>
          <div style={{ fontWeight: 700, color: "#cbd0de" }}>
            {tutorialNote
              ? tutorialNote
              : (() => {
                  const step = TUTORIAL_STEPS[tutorialStepIndex];
                  const safeStepIndex = Math.min(tutorialStepIndex, TUTORIAL_STEPS.length - 1);
                  const instruction = step ? step.instruction : TUTORIAL_STEPS[TUTORIAL_STEPS.length - 1].instruction;
                  return `Tutorial: ${safeStepIndex + 1}/${TUTORIAL_STEPS.length} — ${instruction} (not saved)`;
                })()}
          </div>
        </div>
      ) : null}

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
        {
          cards
            .slice(0, 3)
            .map((card: SwipeCard, index: number) => ({ card, index }))
            .reverse()
            .map(({ card, index }) => (
              <SwipeCardComponent
                key={card.tmdbId}
                card={card}
                active={index === 0 && !deckBusy}
                onSwipe={requestSwipe}
                onOpenDetails={(id) => setSelectedTmdbId(id)}
              />
            ))
        }
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

      {!isMobile ? (
        <div className="deck__actions">
          {actionButtons.map((button) => (
            <button
              key={button.action}
              className={button.className}
              onClick={() => requestSwipe(button.action)}
              disabled={!activeCard || deckBusy}
            >
              {ACTION_LABELS[button.action]}
            </button>
          ))}
        </div>
      ) : null}

      {selectedTmdbId ? <MovieDetailsModal tmdbId={selectedTmdbId} onClose={() => setSelectedTmdbId(null)} /> : null}
    </section>
  );
}
