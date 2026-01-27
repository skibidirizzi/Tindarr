namespace Tindarr.Infrastructure.Integrations.Tmdb.Http;

internal sealed record TmdbCachedResponse(
	int StatusCode,
	string? ContentType,
	string Body);

