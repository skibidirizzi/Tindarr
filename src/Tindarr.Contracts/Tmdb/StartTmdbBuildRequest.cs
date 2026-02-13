namespace Tindarr.Contracts.Tmdb;

public sealed record StartTmdbBuildRequest(
	bool RateLimitOverride,
	int UsersBatchSize = 10,
	int DiscoverLimitPerUser = 50,
	bool PrefetchImages = true);
