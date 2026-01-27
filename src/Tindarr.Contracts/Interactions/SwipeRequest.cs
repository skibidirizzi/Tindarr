namespace Tindarr.Contracts.Interactions;

public sealed record SwipeRequest(
    int TmdbId,
    SwipeActionDto Action,
    string ServiceType,
    string ServerId);
