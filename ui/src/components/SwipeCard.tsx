import { useEffect, useMemo, useRef, type PointerEvent as ReactPointerEvent } from "react";
import { motion, useMotionValue, useTransform, animate } from "framer-motion";
import { isMobileDevice } from "../device";
import type { SwipeAction, SwipeCard } from "../types";

type SwipeCardProps = {
  card: SwipeCard;
  active: boolean;
  onSwipe?: (action: SwipeAction, fromOffset?: { x: number; y: number }) => void;
  onOpenDetails?: (tmdbId: number) => void;
  leavingAction?: SwipeAction | null;
  leavingFrom?: { x: number; y: number } | null;
  onLeaveDone?: () => void;
};

const SWIPE_THRESHOLD = 120;

function actionToFlyout(action: SwipeAction): { x: number; y: number } {
  const w = typeof window === "undefined" ? 1000 : window.innerWidth;
  const h = typeof window === "undefined" ? 1000 : window.innerHeight;

  if (action === "Like") return { x: w * 1.25, y: -h * 0.05 };
  if (action === "Nope") return { x: -w * 1.25, y: -h * 0.05 };
  if (action === "Superlike") return { x: w * 0.05, y: -h * 1.25 };
  return { x: w * 0.05, y: h * 1.25 };
}

function pickSwipeAction(offset: { x: number; y: number }): SwipeAction | null {
  if (offset.x > SWIPE_THRESHOLD) return "Like";
  if (offset.x < -SWIPE_THRESHOLD) return "Nope";
  if (offset.y < -SWIPE_THRESHOLD) return "Superlike";
  if (offset.y > SWIPE_THRESHOLD) return "Skip";
  return null;
}

export default function SwipeCard({ card, active, onSwipe, onOpenDetails, leavingAction, leavingFrom, onLeaveDone }: SwipeCardProps) {
  const cardRef = useRef<HTMLDivElement | null>(null);
  const leaveDoneRef = useRef(false);
  const dragRef = useRef<{ pointerId: number; startX: number; startY: number } | null>(null);
  const draggedRef = useRef(false);
  const isMobile = isMobileDevice();

  const x = useMotionValue(0);
  const y = useMotionValue(0);
  const rotate = useTransform(x, [-200, 200], [-18, 18]);

  const likeOpacity = useTransform(x, [0, SWIPE_THRESHOLD * 0.6, SWIPE_THRESHOLD * 1.1], [0, 0.6, 1]);
  const nopeOpacity = useTransform(x, [-SWIPE_THRESHOLD * 1.1, -SWIPE_THRESHOLD * 0.6, 0], [1, 0.6, 0]);
  const superlikeOpacity = useTransform(y, [-SWIPE_THRESHOLD * 1.1, -SWIPE_THRESHOLD * 0.6, 0], [1, 0.6, 0]);
  const skipOpacity = useTransform(y, [0, SWIPE_THRESHOLD * 0.6, SWIPE_THRESHOLD * 1.1], [0, 0.6, 1]);

  const isLeaving = Boolean(leavingAction);

  const TAP_MOVE_THRESHOLD_PX = 8;

  useEffect(() => {
    if (!active && !isLeaving) {
      x.set(0);
      y.set(0);
    }
  }, [active, isLeaving]);

  useEffect(() => {
    if (!leavingAction) return;
    leaveDoneRef.current = false;
    const from = leavingFrom ?? { x: x.get(), y: y.get() };
    x.set(from.x);
    y.set(from.y);

    const flyout = actionToFlyout(leavingAction);
    const controlsX = animate(x, flyout.x, { duration: 0.35, ease: "easeOut" });
    const controlsY = animate(y, flyout.y, {
      duration: 0.35,
      ease: "easeOut",
      onComplete: () => {
        if (leaveDoneRef.current) return;
        leaveDoneRef.current = true;
        onLeaveDone?.();
      }
    });

    return () => {
      controlsX.stop();
      controlsY.stop();
    };
  }, [leavingAction, leavingFrom, onLeaveDone, x, y]);

  function snapBack() {
    animate(x, 0, { type: "spring", stiffness: 300, damping: 30 });
    animate(y, 0, { type: "spring", stiffness: 300, damping: 30 });
  }

  function handlePointerDown(event: ReactPointerEvent<HTMLDivElement>) {
    if (!active || isLeaving) return;
    if (event.pointerType !== "touch" && event.button !== 0) return;

    dragRef.current = { pointerId: event.pointerId, startX: event.clientX, startY: event.clientY };
    draggedRef.current = false;
    event.currentTarget.setPointerCapture(event.pointerId);
    event.preventDefault();
  }

  function handlePointerMove(event: ReactPointerEvent<HTMLDivElement>) {
    const state = dragRef.current;
    if (!state) return;
    if (state.pointerId !== event.pointerId) return;

    const nextX = event.clientX - state.startX;
    const nextY = event.clientY - state.startY;
    if (!draggedRef.current && (Math.abs(nextX) > TAP_MOVE_THRESHOLD_PX || Math.abs(nextY) > TAP_MOVE_THRESHOLD_PX)) {
      draggedRef.current = true;
    }

    x.set(nextX);
    y.set(nextY);
    event.preventDefault();
  }

  function handlePointerUp(event: ReactPointerEvent<HTMLDivElement>) {
    const state = dragRef.current;
    if (!state) return;
    if (state.pointerId !== event.pointerId) return;
    dragRef.current = null;

    const offset = { x: x.get(), y: y.get() };

    // Treat a simple tap (no meaningful movement) as a request for details.
    if (!draggedRef.current && Math.abs(offset.x) <= TAP_MOVE_THRESHOLD_PX && Math.abs(offset.y) <= TAP_MOVE_THRESHOLD_PX) {
      x.set(0);
      y.set(0);
      onOpenDetails?.(card.tmdbId);
      return;
    }

    const action = pickSwipeAction(offset);
    if (!action) {
      snapBack();
      return;
    }

    onSwipe?.(action, offset);
    x.set(0);
    y.set(0);
  }

  function handlePointerCancel(event: ReactPointerEvent<HTMLDivElement>) {
    const state = dragRef.current;
    if (!state) return;
    if (state.pointerId !== event.pointerId) return;
    dragRef.current = null;
    draggedRef.current = false;
    snapBack();
  }

  const style = useMemo(() => {
    return {
      x,
      y,
      rotate,
      touchAction: active ? "none" : ("auto" as const),
      willChange: "transform" as const
    };
  }, [active, rotate, x, y]);

  return (
    <motion.div
      ref={cardRef}
      className={`swipe-card ${active ? "swipe-card--active" : ""}`}
      style={style}
      transition={{ type: "spring", stiffness: 300, damping: 30 }}
      onPointerDown={handlePointerDown}
      onPointerMove={handlePointerMove}
      onPointerUp={handlePointerUp}
      onPointerCancel={handlePointerCancel}
    >
      {active && !isLeaving ? (
        <>
          <motion.div className="swipe-card__overlay swipe-card__overlay--nope" style={{ opacity: nopeOpacity }}>
            NOPE
          </motion.div>
          <motion.div className="swipe-card__overlay swipe-card__overlay--like" style={{ opacity: likeOpacity }}>
            LIKE
          </motion.div>
          <motion.div className="swipe-card__overlay swipe-card__overlay--super" style={{ opacity: superlikeOpacity }}>
            SUPERLIKE
          </motion.div>
          <motion.div className="swipe-card__overlay swipe-card__overlay--skip" style={{ opacity: skipOpacity }}>
            SKIP
          </motion.div>
        </>
      ) : null}
      <div className="swipe-card__media">
        {card.backdropUrl ? (
          <img src={card.backdropUrl} alt={card.title} draggable={false} style={{ pointerEvents: "none" }} />
        ) : (
          <div className="swipe-card__placeholder">No image</div>
        )}
      </div>
      <div className="swipe-card__content">
        <div className="swipe-card__title">
          <h2>{card.title}</h2>
          {card.releaseYear ? <span>{card.releaseYear}</span> : null}
        </div>
        <p>{card.overview}</p>
        {!isMobile ? (
          <div className="swipe-card__meta">
            <span>TMDB #{card.tmdbId}</span>
            {card.rating ? <span>{card.rating.toFixed(1)} ★</span> : null}
          </div>
        ) : null}
      </div>
    </motion.div>
  );
}
