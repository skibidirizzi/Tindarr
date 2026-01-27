namespace Tindarr.Contracts.Interactions;

public sealed record InteractionListResponse(
	string ServiceType,
	string ServerId,
	IReadOnlyList<InteractionDto> Items);

