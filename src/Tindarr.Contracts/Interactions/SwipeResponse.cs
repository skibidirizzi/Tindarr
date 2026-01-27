namespace Tindarr.Contracts.Interactions;

public sealed record SwipeResponse(
    int TmdbId,
    SwipeActionDto Action,
    DateTimeOffset CreatedAtUtc);
