namespace Tindarr.Domain.Interactions;

public sealed record SwipeCard(
    int TmdbId,
    string Title,
    string? Overview,
    string? PosterUrl,
    string? BackdropUrl,
    int? ReleaseYear,
    double? Rating);
