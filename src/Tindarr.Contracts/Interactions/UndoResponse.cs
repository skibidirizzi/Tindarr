namespace Tindarr.Contracts.Interactions;

public sealed record UndoResponse(
    bool Undone,
    int? TmdbId,
    SwipeActionDto? Action,
    DateTimeOffset? CreatedAtUtc);
