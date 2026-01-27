namespace Tindarr.Contracts.Matching;

public sealed record MatchesResponse(
	string ServiceType,
	string ServerId,
	IReadOnlyList<MatchDto> Items);

