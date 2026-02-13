export type PosterGalleryRibbonVariant = "liked" | "matched" | "superliked";

export type PosterGalleryItem = {
  key: string;
  tmdbId: number;
  title: string;
  year?: number | null;
  posterUrl?: string | null;
  ribbon: {
    label: string;
    variant: PosterGalleryRibbonVariant;
  };
};

export default function PosterGallery({ items, onSelect }: { items: PosterGalleryItem[]; onSelect: (tmdbId: number) => void }) {
  return (
    <div className="gallery" role="list">
      {items.map((item) => (
        <button
          key={item.key}
          type="button"
          className="galleryItem"
          onClick={() => onSelect(item.tmdbId)}
          aria-label={`${item.title} — ${item.ribbon.label}`}
          role="listitem"
        >
          <div className={`ribbon ribbon--${item.ribbon.variant}`}>{item.ribbon.label}</div>

          <div className="galleryPosterWrap">
            {item.posterUrl ? (
              <img className="galleryPoster" src={item.posterUrl} alt={item.title} loading="lazy" />
            ) : (
              <div className="galleryPoster galleryPoster--placeholder">No poster</div>
            )}

            <div className="galleryOverlay" aria-hidden={true}>
              <div className="galleryOverlayTitle">{item.title}</div>
              {item.year ? <div className="galleryOverlayYear">{item.year}</div> : null}
            </div>
          </div>
        </button>
      ))}
    </div>
  );
}
