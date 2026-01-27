import { useEffect, useRef, useState, type PointerEvent } from "react";
import type { SwipeAction, SwipeCard } from "../types";

type SwipeCardProps = {
  card: SwipeCard;
  active: boolean;
  onSwipe: (action: SwipeAction) => void;
};

const SWIPE_THRESHOLD = 120;

export default function SwipeCard({ card, active, onSwipe }: SwipeCardProps) {
  const cardRef = useRef<HTMLDivElement | null>(null);
  const [offset, setOffset] = useState({ x: 0, y: 0 });
  const [dragging, setDragging] = useState(false);

  useEffect(() => {
    if (!active) {
      setOffset({ x: 0, y: 0 });
      setDragging(false);
    }
  }, [active]);

  function handlePointerDown(event: PointerEvent<HTMLDivElement>) {
    if (!active) return;
    event.currentTarget.setPointerCapture(event.pointerId);
    setDragging(true);
  }

  function handlePointerMove(event: PointerEvent<HTMLDivElement>) {
    if (!dragging || !active) return;
    setOffset({
      x: offset.x + event.movementX,
      y: offset.y + event.movementY
    });
  }

  function handlePointerUp(event: PointerEvent<HTMLDivElement>) {
    if (!active) return;
    event.currentTarget.releasePointerCapture(event.pointerId);
    setDragging(false);

    if (offset.x > SWIPE_THRESHOLD) {
      onSwipe("Like");
      setOffset({ x: 0, y: 0 });
      return;
    }

    if (offset.x < -SWIPE_THRESHOLD) {
      onSwipe("Nope");
      setOffset({ x: 0, y: 0 });
      return;
    }

    if (offset.y < -SWIPE_THRESHOLD) {
      onSwipe("Superlike");
      setOffset({ x: 0, y: 0 });
      return;
    }

    setOffset({ x: 0, y: 0 });
  }

  const style = {
    transform: `translate(${offset.x}px, ${offset.y}px) rotate(${offset.x / 12}deg)`,
    transition: dragging ? "none" : "transform 0.25s ease"
  };

  return (
    <div
      ref={cardRef}
      className={`swipe-card ${active ? "swipe-card--active" : ""}`}
      style={style}
      onPointerDown={handlePointerDown}
      onPointerMove={handlePointerMove}
      onPointerUp={handlePointerUp}
    >
      <div className="swipe-card__media">
        {card.backdropUrl ? (
          <img src={card.backdropUrl} alt={card.title} />
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
        <div className="swipe-card__meta">
          <span>TMDB #{card.tmdbId}</span>
          {card.rating ? <span>{card.rating.toFixed(1)} ★</span> : null}
        </div>
      </div>
    </div>
  );
}
