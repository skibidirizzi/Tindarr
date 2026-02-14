using System.Net.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tindarr.Application.Abstractions.Integrations;
using Tindarr.Application.Abstractions.Persistence;
using Tindarr.Application.Interfaces.Playback;
using Tindarr.Application.Options;
using Tindarr.Domain.Common;

namespace Tindarr.Infrastructure.Playback.Providers;

public sealed class PlexPlaybackProvider(
	IServiceSettingsRepository settingsRepo,
	IPlexLibraryCacheRepository plexCache,
	HttpClient httpClient,
	IOptions<PlexOptions> plexOptions,
	ILogger<PlexPlaybackProvider> logger) : IPlaybackProvider
{
	private readonly PlexOptions _plexOptions = plexOptions.Value;

	// For casting, Plex must choose a playback profile that matches the actual receiver capabilities.
	// If we identify as an unknown platform/device, Plex refuses to pick a profile and will not transcode.
	private const string ChromecastPlatform = "Chromecast";
	private const string ChromecastDevice = "Chromecast";
	private const string ChromecastModel = "Chromecast";
	private const string ChromecastPlatformVersion = "1";

	public ServiceType ServiceType => ServiceType.Plex;

	public async Task<UpstreamPlaybackRequest> BuildMovieStreamRequestAsync(ServiceScope scope, int tmdbId, CancellationToken cancellationToken)
	{
		_ = httpClient;

		if (tmdbId <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(tmdbId));
		}

		var server = await settingsRepo.GetAsync(scope, cancellationToken).ConfigureAwait(false);
		if (server is null || string.IsNullOrWhiteSpace(server.PlexServerUri))
		{
			throw new InvalidOperationException("Plex server is not configured.");
		}

		var account = await settingsRepo.GetAsync(new ServiceScope(ServiceType.Plex, PlexConstants.AccountServerId), cancellationToken).ConfigureAwait(false);
		var accessToken = !string.IsNullOrWhiteSpace(server.PlexServerAccessToken)
			? server.PlexServerAccessToken!
			: account?.PlexAuthToken;

		if (string.IsNullOrWhiteSpace(accessToken))
		{
			throw new InvalidOperationException("Plex is not authenticated.");
		}

		var ratingKey = await plexCache.TryGetRatingKeyAsync(scope, tmdbId, cancellationToken).ConfigureAwait(false);
		if (string.IsNullOrWhiteSpace(ratingKey))
		{
			throw new InvalidOperationException("Plex item not found in cache. Sync Plex library first.");
		}

		var clientIdentifier = server.PlexClientIdentifier ?? account?.PlexClientIdentifier;
		if (string.IsNullOrWhiteSpace(clientIdentifier))
		{
			throw new InvalidOperationException("Plex client identifier is not configured.");
		}

		// IMPORTANT:
		// Do NOT stream Plex Part.Key directly (or with download=1).
		// Chromecast frequently can't decode the source audio codec (e.g. DTS), resulting in silent playback.
		// Use Plex's universal transcode endpoint to force a Chromecast-friendly container/audio.
		var baseUrl = NormalizeBaseUrl(server.PlexServerUri!);
		var path = $"/library/metadata/{ratingKey.Trim()}";
		var upstreamUri = new Uri(
			$"{baseUrl}video/:/transcode/universal/start?" +
			$"path={Uri.EscapeDataString(path)}" +
			"&mediaIndex=0" +
			"&partIndex=0" +
			"&protocol=http" +
			"&format=mp4" +
			"&directPlay=0" +
			"&directStream=1" +
			"&fastSeek=1" +
			$"&session={Uri.EscapeDataString(clientIdentifier.Trim())}",
			UriKind.Absolute);

		logger.LogDebug("plex upstream stream request built. TmdbId={TmdbId} RatingKey={RatingKey} Uri={Uri}", tmdbId, ratingKey, upstreamUri);

		return new UpstreamPlaybackRequest(
			Uri: upstreamUri,
			Headers: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
			{
				["X-Plex-Token"] = accessToken,
				["X-Plex-Client-Identifier"] = clientIdentifier.Trim(),
				["X-Plex-Product"] = _plexOptions.Product,
				// Identify as Chromecast so Plex can choose a matching client profile for audio/video compatibility.
				["X-Plex-Platform"] = ChromecastPlatform,
				["X-Plex-Platform-Version"] = ChromecastPlatformVersion,
				["X-Plex-Device"] = ChromecastDevice,
				["X-Plex-Model"] = ChromecastModel,
				["X-Plex-Version"] = _plexOptions.Version,
			});
	}

	private static string NormalizeBaseUrl(string baseUrl)
	{
		var trimmed = (baseUrl ?? string.Empty).Trim().TrimEnd('/');
		return string.IsNullOrWhiteSpace(trimmed) ? string.Empty : trimmed + "/";
	}

	// Intentionally no Plex metadata DTOs here: this provider streams via transcode endpoint.
}
