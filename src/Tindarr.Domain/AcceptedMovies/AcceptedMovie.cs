using Tindarr.Domain.Common;

namespace Tindarr.Domain.AcceptedMovies;

public sealed record AcceptedMovie(
	long Id,
	ServiceScope Scope,
	int TmdbId,
	string? AcceptedByUserId,
	DateTimeOffset AcceptedAtUtc);

