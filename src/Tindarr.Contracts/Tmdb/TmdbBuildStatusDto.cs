namespace Tindarr.Contracts.Tmdb;

public sealed record TmdbBuildStatusDto(
	string State,
	DateTimeOffset? StartedAtUtc,
	DateTimeOffset? FinishedAtUtc,
	bool RateLimitOverride,
	string? CurrentUserId,
	int UsersProcessed,
	int UsersTotal,
	int MoviesDiscovered,
	int DetailsFetched,
	int ImagesFetched,
	string? LastMessage,
	string? LastError);
