namespace Tindarr.Api.Middleware;

public static class SensitiveRedaction
{
	private static readonly HashSet<string> SensitiveHeaders = new(StringComparer.OrdinalIgnoreCase)
	{
		"Authorization",
		"Cookie",
		"Set-Cookie",

		// Common token/API key headers across providers and proxies
		"X-Api-Key",
		"Api-Key",
		"X-Auth-Token",
		"X-Access-Token",
		"X-Refresh-Token",

		// Plex + playback gateway
		"X-Plex-Token",
		"X-Playback-Token",

		// Radarr
		"X-Radarr-Api-Key",

		// Emby/Jellyfin (some clients use these variations)
		"X-Emby-Token",
		"X-Jellyfin-Token",
		"X-MediaBrowser-Token",
		"X-Emby-Authorization",
		"X-Jellyfin-Authorization"
	};

	private static readonly HashSet<string> SensitiveQueryKeys = new(StringComparer.OrdinalIgnoreCase)
	{
		"token",
		"access_token",
		"refresh_token",
		"api_key",
		"apikey",
		"x-plex-token",
		"plex_token",
		"playback_token",
		"jwt",
		"signature",
		"sig"
	};

	public static string RedactHeader(string headerName, string value)
	{
		return IsSensitiveHeaderName(headerName) ? "[REDACTED]" : value;
	}

	public static string RedactPathAndQuery(PathString path, QueryString queryString)
	{
		if (!queryString.HasValue)
		{
			return path.Value ?? "/";
		}

		var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(queryString.Value ?? string.Empty);
		if (query.Count == 0)
		{
			return (path.Value ?? "/") + (queryString.Value ?? string.Empty);
		}

		var kvps = new List<KeyValuePair<string, string?>>(query.Count);
		foreach (var (key, values) in query)
		{
			if (SensitiveQueryKeys.Contains(key))
			{
				kvps.Add(new KeyValuePair<string, string?>(key, "[REDACTED]"));
				continue;
			}

			if (values.Count == 0)
			{
				kvps.Add(new KeyValuePair<string, string?>(key, null));
				continue;
			}

			foreach (var value in values)
			{
				kvps.Add(new KeyValuePair<string, string?>(key, value));
			}
		}

		var redacted = QueryString.Create(kvps);
		return (path.Value ?? "/") + redacted.Value;
	}

	private static bool IsSensitiveHeaderName(string headerName)
	{
		if (SensitiveHeaders.Contains(headerName))
		{
			return true;
		}

		// Pattern-based defaults: tokens/keys/cookies/authorization should never be logged.
		return headerName.Contains("authorization", StringComparison.OrdinalIgnoreCase)
			|| headerName.Contains("cookie", StringComparison.OrdinalIgnoreCase)
			|| headerName.EndsWith("token", StringComparison.OrdinalIgnoreCase)
			|| headerName.Contains("api-key", StringComparison.OrdinalIgnoreCase)
			|| headerName.Contains("apikey", StringComparison.OrdinalIgnoreCase)
			|| headerName.Contains("key", StringComparison.OrdinalIgnoreCase) && headerName.Contains("api", StringComparison.OrdinalIgnoreCase);
	}
}
