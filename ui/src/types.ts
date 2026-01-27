export type SwipeCard = {
  tmdbId: number;
  title: string;
  overview?: string | null;
  posterUrl?: string | null;
  backdropUrl?: string | null;
  releaseYear?: number | null;
  rating?: number | null;
};

export type SwipeDeckResponse = {
  serviceType: string;
  serverId: string;
  items: SwipeCard[];
};

export type SwipeAction = "Like" | "Nope" | "Skip" | "Superlike";
