namespace Tindarr.Contracts.Interactions;

public sealed record InteractionDto(
	int TmdbId,
	SwipeActionDto Action,
	DateTimeOffset CreatedAtUtc);

