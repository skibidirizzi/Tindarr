using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Options;
using System.Text;
using Tindarr.Application.Abstractions.Integrations;
using Tindarr.Application.Abstractions.Persistence;
using Tindarr.Application.Abstractions.Security;
using Tindarr.Application.Interfaces.Ops;
using Tindarr.Application.Interfaces.Playback;
using Tindarr.Application.Options;
using Tindarr.Contracts.Playback;
using Tindarr.Domain.Common;
using Tindarr.Api.Hosting;
using Tindarr.Infrastructure.Playback.Hls;

namespace Tindarr.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/v1/playback")]
public sealed class PlaybackController(
	IPlaybackTokenService playbackTokenService,
	IEnumerable<IPlaybackProvider> providers,
	IServiceSettingsRepository settingsRepo,
	IBaseUrlResolver baseUrlResolver,
	IOptions<PlexOptions> plexOptions,
	IOptions<PlaybackOptions> playbackOptions,
	IHttpClientFactory httpClientFactory,
	ILogger<PlaybackController> logger) : ControllerBase
{
	private readonly ManifestRewriter _manifestRewriter = new();
	private readonly PlaybackOptions _playbackOptions = playbackOptions.Value;
	private readonly PlexOptions _plexOptions = plexOptions.Value;

	[HttpPost("movie/prepare")]
	[Authorize]
	public IActionResult PrepareMoviePlayback([FromBody] PrepareMoviePlaybackRequest request)
	{
		if (request is null)
		{
			return BadRequest("Request body is required.");
		}
		if (request.TmdbId <= 0)
		{
			return BadRequest("TmdbId must be positive.");
		}
		if (!ServiceScope.TryCreate(request.ServiceType, request.ServerId, out var scope) || scope is null)
		{
			return BadRequest("Invalid service scope.");
		}

		var now = DateTimeOffset.UtcNow;
		var token = playbackTokenService.IssueMovieToken(scope, request.TmdbId, now);
		var clientIp = HttpContext.Connection.RemoteIpAddress;
		var contentUrl = baseUrlResolver.Combine(
			$"api/v1/playback/movie/{Uri.EscapeDataString(scope.ServiceType.ToString().ToLowerInvariant())}/{Uri.EscapeDataString(scope.ServerId)}/{request.TmdbId}?token={Uri.EscapeDataString(token)}",
			clientIp);

		var exp = now.AddMinutes(Math.Clamp(_playbackOptions.TokenMinutes, 1, 60)).ToUnixTimeSeconds();
		return Ok(new PreparePlaybackResponse(
			ContentUrl: contentUrl.ToString(),
			ContentType: "video/mp4",
			ExpiresAtUnixSeconds: exp));
	}

	[HttpGet("movie/{serviceType}/{serverId}/{tmdbId:int}")]
	public async Task<IActionResult> StreamMovie(
		[FromRoute] string serviceType,
		[FromRoute] string serverId,
		[FromRoute] int tmdbId,
		[FromQuery] string? token,
		CancellationToken cancellationToken)
	{
		if (tmdbId <= 0)
		{
			return BadRequest("Invalid TMDB id.");
		}

		if (!ServiceScope.TryCreate(serviceType, serverId, out var scope) || scope is null)
		{
			return BadRequest("Invalid service scope.");
		}

		token = string.IsNullOrWhiteSpace(token)
			? Request.Headers["X-Tindarr-Playback-Token"].FirstOrDefault()
			: token;

		if (string.IsNullOrWhiteSpace(token) || !playbackTokenService.TryValidateMovieToken(token, scope, tmdbId, DateTimeOffset.UtcNow))
		{
			return Unauthorized();
		}

		var provider = providers.FirstOrDefault(p => p.ServiceType == scope.ServiceType);
		if (provider is null)
		{
			return BadRequest("Unsupported playback provider.");
		}

		UpstreamPlaybackRequest upstream;
		try
		{
			upstream = await provider.BuildMovieStreamRequestAsync(scope, tmdbId, cancellationToken).ConfigureAwait(false);
		}
		catch (InvalidOperationException ex)
		{
			logger.LogWarning(ex, "Failed to build upstream playback request. ServiceType={ServiceType} ServerId={ServerId} TmdbId={TmdbId}", scope.ServiceType, scope.ServerId, tmdbId);
			return BadRequest(ex.Message);
		}

		logger.LogDebug("Proxying playback stream. ServiceType={ServiceType} ServerId={ServerId} TmdbId={TmdbId} UpstreamUri={UpstreamUri}", scope.ServiceType, scope.ServerId, tmdbId, upstream.Uri);

		var client = httpClientFactory.CreateClient();
		using var upstreamRequest = new HttpRequestMessage(HttpMethod.Get, upstream.Uri);
		StreamingProxy.CopyAllowedRequestHeaders(Request, upstreamRequest);
		// If upstream returns a gzip'd playlist, rewriting will break. Always request identity.
		upstreamRequest.Headers.AcceptEncoding.Clear();

		foreach (var header in upstream.Headers)
		{
			upstreamRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
		}

		using HttpResponseMessage upstreamResponse = await client
			.SendAsync(upstreamRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
			.ConfigureAwait(false);

		logger.LogDebug("Upstream playback response. StatusCode={StatusCode} ContentType={ContentType}", (int)upstreamResponse.StatusCode, upstreamResponse.Content.Headers.ContentType?.ToString());

		if (upstreamResponse.IsSuccessStatusCode && IsHlsPlaylistResponse(upstreamResponse))
		{
			var playlistText = await upstreamResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
			var rewritten = _manifestRewriter.Rewrite(playlistText, upstream.Uri, uri => BuildGatewayProxyUrl(scope, tmdbId, token!, uri));
			Response.StatusCode = (int)upstreamResponse.StatusCode;
			StreamingProxy.CopyAllowedResponseHeaders(upstreamResponse, Response);
			Response.Headers.Remove(HeaderNames.ContentLength);
			return Content(rewritten, upstreamResponse.Content.Headers.ContentType?.ToString() ?? "application/vnd.apple.mpegurl");
		}

		await StreamingProxy.StreamBodyAsync(upstreamResponse, Response, cancellationToken).ConfigureAwait(false);
		return new EmptyResult();
	}

	[HttpGet("proxy/movie/{serviceType}/{serverId}/{tmdbId:int}")]
	public async Task<IActionResult> ProxyMovieResource(
		[FromRoute] string serviceType,
		[FromRoute] string serverId,
		[FromRoute] int tmdbId,
		[FromQuery] string? token,
		[FromQuery] string? p,
		CancellationToken cancellationToken)
	{
		if (tmdbId <= 0)
		{
			return BadRequest("Invalid TMDB id.");
		}
		if (!ServiceScope.TryCreate(serviceType, serverId, out var scope) || scope is null)
		{
			return BadRequest("Invalid service scope.");
		}

		token = string.IsNullOrWhiteSpace(token)
			? Request.Headers["X-Tindarr-Playback-Token"].FirstOrDefault()
			: token;
		if (string.IsNullOrWhiteSpace(token) || !playbackTokenService.TryValidateMovieToken(token, scope, tmdbId, DateTimeOffset.UtcNow))
		{
			return Unauthorized();
		}

		if (string.IsNullOrWhiteSpace(p))
		{
			return BadRequest("Missing resource pointer.");
		}

		string relativePathAndQuery;
		try
		{
			relativePathAndQuery = Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes(p));
		}
		catch
		{
			return BadRequest("Invalid resource pointer.");
		}

		if (string.IsNullOrWhiteSpace(relativePathAndQuery) || !relativePathAndQuery.StartsWith("/", StringComparison.Ordinal))
		{
			return BadRequest("Invalid resource pointer.");
		}

		var upstreamOrigin = await ResolveUpstreamOriginAsync(scope, cancellationToken).ConfigureAwait(false);
		if (upstreamOrigin is null)
		{
			return BadRequest("Upstream server is not configured.");
		}

		// Prevent open proxying by ensuring we're only ever talking to the configured upstream origin.
		var upstreamUri = new Uri(upstreamOrigin, relativePathAndQuery);

		var headers = await BuildUpstreamAuthHeadersAsync(scope, cancellationToken).ConfigureAwait(false);
		if (headers is null)
		{
			return BadRequest("Upstream auth is not configured.");
		}

		var client = httpClientFactory.CreateClient();
		using var upstreamRequest = new HttpRequestMessage(HttpMethod.Get, upstreamUri);
		StreamingProxy.CopyAllowedRequestHeaders(Request, upstreamRequest);
		// If upstream returns a gzip'd playlist, rewriting will break. Always request identity.
		upstreamRequest.Headers.AcceptEncoding.Clear();
		foreach (var header in headers)
		{
			upstreamRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
		}

		using var upstreamResponse = await client
			.SendAsync(upstreamRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
			.ConfigureAwait(false);

		if (upstreamResponse.IsSuccessStatusCode && IsHlsPlaylistResponse(upstreamResponse))
		{
			var playlistText = await upstreamResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
			var rewritten = _manifestRewriter.Rewrite(playlistText, upstreamUri, uri => BuildGatewayProxyUrl(scope, tmdbId, token!, uri));
			Response.StatusCode = (int)upstreamResponse.StatusCode;
			StreamingProxy.CopyAllowedResponseHeaders(upstreamResponse, Response);
			Response.Headers.Remove(HeaderNames.ContentLength);
			return Content(rewritten, upstreamResponse.Content.Headers.ContentType?.ToString() ?? "application/vnd.apple.mpegurl");
		}

		await StreamingProxy.StreamBodyAsync(upstreamResponse, Response, cancellationToken).ConfigureAwait(false);
		return new EmptyResult();
	}

	private static bool IsHlsPlaylistResponse(HttpResponseMessage response)
	{
		var ct = response.Content.Headers.ContentType?.MediaType;
		if (string.IsNullOrWhiteSpace(ct))
		{
			return false;
		}

		return ct.Contains("mpegurl", StringComparison.OrdinalIgnoreCase)
			|| ct.Contains("vnd.apple.mpegurl", StringComparison.OrdinalIgnoreCase)
			|| ct.Contains("application/x-mpegURL", StringComparison.OrdinalIgnoreCase);
	}

	private string BuildGatewayProxyUrl(ServiceScope scope, int tmdbId, string token, Uri upstreamResource)
	{
		var scrubbed = ScrubQuerySecrets(upstreamResource);
		var relative = scrubbed.PathAndQuery;
		var p = Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(relative));
		return $"/api/v1/playback/proxy/movie/{Uri.EscapeDataString(scope.ServiceType.ToString().ToLowerInvariant())}/{Uri.EscapeDataString(scope.ServerId)}/{tmdbId}?token={Uri.EscapeDataString(token)}&p={Uri.EscapeDataString(p)}";
	}

	private static Uri ScrubQuerySecrets(Uri upstream)
	{
		if (!upstream.IsAbsoluteUri)
		{
			return upstream;
		}

		var query = upstream.Query;
		if (string.IsNullOrWhiteSpace(query) || query == "?")
		{
			return upstream;
		}

		// Very small scrub list to prevent leaking provider credentials if an upstream ever embeds them.
		var toRemove = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			"X-Plex-Token",
			"X-Emby-Token",
			"api_key",
			"apikey",
			"access_token",
			"token"
		};

		var parsed = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(query);
		var kept = new List<KeyValuePair<string, string?>>(parsed.Count);
		foreach (var kvp in parsed)
		{
			if (toRemove.Contains(kvp.Key))
			{
				continue;
			}
			foreach (var v in kvp.Value)
			{
				kept.Add(new KeyValuePair<string, string?>(kvp.Key, v));
			}
		}

		var rebuilt = Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString(upstream.GetLeftPart(UriPartial.Path), kept);
		return new Uri(rebuilt, UriKind.Absolute);
	}

	private async Task<Uri?> ResolveUpstreamOriginAsync(ServiceScope scope, CancellationToken cancellationToken)
	{
		var settings = await settingsRepo.GetAsync(scope, cancellationToken).ConfigureAwait(false);
		if (settings is null)
		{
			return null;
		}

		string? baseUrl = scope.ServiceType switch
		{
			ServiceType.Plex => settings.PlexServerUri,
			ServiceType.Jellyfin => settings.JellyfinBaseUrl,
			ServiceType.Emby => settings.EmbyBaseUrl,
			_ => null
		};

		if (string.IsNullOrWhiteSpace(baseUrl) || !Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var baseUri))
		{
			return null;
		}

		// Origin = scheme + host + port; never allow arbitrary upstream hosts.
		var builder = new UriBuilder(baseUri)
		{
			Path = string.Empty,
			Query = string.Empty,
			Fragment = string.Empty
		};
		return builder.Uri;
	}

	private async Task<IReadOnlyDictionary<string, string>?> BuildUpstreamAuthHeadersAsync(ServiceScope scope, CancellationToken cancellationToken)
	{
		var settings = await settingsRepo.GetAsync(scope, cancellationToken).ConfigureAwait(false);
		if (settings is null)
		{
			return null;
		}

		switch (scope.ServiceType)
		{
			case ServiceType.Plex:
			{
				var account = await settingsRepo
					.GetAsync(new ServiceScope(ServiceType.Plex, PlexConstants.AccountServerId), cancellationToken)
					.ConfigureAwait(false);

				var accessToken = !string.IsNullOrWhiteSpace(settings.PlexServerAccessToken)
					? settings.PlexServerAccessToken!
					: account?.PlexAuthToken;
				if (string.IsNullOrWhiteSpace(accessToken))
				{
					return null;
				}

				var clientIdentifier = settings.PlexClientIdentifier ?? account?.PlexClientIdentifier;
				if (string.IsNullOrWhiteSpace(clientIdentifier))
				{
					return null;
				}

				return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
				{
					["X-Plex-Token"] = accessToken,
					["X-Plex-Client-Identifier"] = clientIdentifier.Trim(),
					["X-Plex-Product"] = _plexOptions.Product,
					["X-Plex-Platform"] = "Chromecast",
					["X-Plex-Platform-Version"] = "1",
					["X-Plex-Device"] = "Chromecast",
					["X-Plex-Model"] = "Chromecast",
					["X-Plex-Version"] = _plexOptions.Version,
				};
			}

			case ServiceType.Jellyfin:
				return string.IsNullOrWhiteSpace(settings.JellyfinApiKey)
					? null
					: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
					{
						["X-Emby-Token"] = settings.JellyfinApiKey!
					};

			case ServiceType.Emby:
				return string.IsNullOrWhiteSpace(settings.EmbyApiKey)
					? null
					: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
					{
						["X-Emby-Token"] = settings.EmbyApiKey!
					};

			default:
				return null;
		}
	}
}
