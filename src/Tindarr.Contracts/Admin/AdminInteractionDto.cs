using Tindarr.Contracts.Interactions;

namespace Tindarr.Contracts.Admin;

public sealed record AdminInteractionDto(
	string UserId,
	string ServiceType,
	string ServerId,
	int TmdbId,
	SwipeActionDto Action,
	DateTimeOffset CreatedAtUtc);
