namespace Tindarr.Contracts.Interactions;

public sealed record SwipeCardDto(
    int TmdbId,
    string Title,
    string? Overview,
    string? PosterUrl,
    string? BackdropUrl,
    int? ReleaseYear,
    double? Rating);
