namespace Tindarr.Contracts.Matching;

public sealed record MatchDto(
	int TmdbId,
	IReadOnlyList<string> MatchedWithDisplayNames);

