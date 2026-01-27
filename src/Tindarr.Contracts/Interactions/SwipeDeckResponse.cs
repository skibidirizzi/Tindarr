namespace Tindarr.Contracts.Interactions;

public sealed record SwipeDeckResponse(
    string ServiceType,
    string ServerId,
    IReadOnlyList<SwipeCardDto> Items);
