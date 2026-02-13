namespace Tindarr.Contracts.Admin;

public sealed record AdminInteractionSearchResponse(
	IReadOnlyList<AdminInteractionDto> Items);
