namespace Tindarr.Application.Options;

public sealed class TmdbOptions
{
	public const string SectionName = "Tmdb";

	/// <summary>
	/// TMDB API key (v3). Must be supplied via environment variables / runtime config.
	/// Do not commit real values to source control.
	/// </summary>
	public string ApiKey { get; init; } = "";

	/// <summary>
	/// TMDB API Read Access Token (Bearer token; works for v3 and v4).
	/// Prefer this over <see cref="ApiKey"/> to avoid query-string secrets.
	/// Do not commit real values to source control.
	/// </summary>
	public string ReadAccessToken { get; init; } = "";

	/// <summary>
	/// Base URL for TMDB API v3.
	/// </summary>
	public string BaseUrl { get; init; } = "https://api.themoviedb.org/3/";

	/// <summary>
	/// Base URL for TMDB images (path-only in API responses).
	/// </summary>
	public string ImageBaseUrl { get; init; } = "https://image.tmdb.org/t/p/";

	/// <summary>
	/// Poster size segment (e.g. "w500").
	/// </summary>
	public string PosterSize { get; init; } = "w500";

	/// <summary>
	/// Backdrop size segment (e.g. "w780").
	/// </summary>
	public string BackdropSize { get; init; } = "w780";

	/// <summary>
	/// Token bucket rate limiter settings (approximate).
	/// </summary>
	public int RequestsPerSecond { get; init; } = 4;

	/// <summary>
	/// Response cache TTL for discover queries.
	/// </summary>
	public int DiscoverCacheSeconds { get; init; } = 60;

	/// <summary>
	/// Response cache TTL for movie details.
	/// </summary>
	public int DetailsCacheSeconds { get; init; } = 10 * 60;

	/// <summary>
	/// Overall timeout budget (in seconds) for TMDB operations (discover/details), including rate-limiter waits and retries.
	/// This is not just the raw HTTP connect/read timeout.
	/// </summary>
	public int OperationTimeoutSeconds { get; init; } = 12;

	public bool HasApiKey => !string.IsNullOrWhiteSpace(ApiKey);

	public bool HasReadAccessToken => !string.IsNullOrWhiteSpace(ReadAccessToken);

	public bool HasCredentials => HasApiKey || HasReadAccessToken;

	public bool IsValid()
	{
		if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out _))
		{
			return false;
		}

		if (!Uri.TryCreate(ImageBaseUrl, UriKind.Absolute, out _))
		{
			return false;
		}

		return RequestsPerSecond >= 1
			&& RequestsPerSecond <= 50
			&& DiscoverCacheSeconds >= 0
			&& DetailsCacheSeconds >= 0
			&& OperationTimeoutSeconds >= 1
			&& OperationTimeoutSeconds <= 120
			&& !string.IsNullOrWhiteSpace(PosterSize)
			&& !string.IsNullOrWhiteSpace(BackdropSize);
	}
}

