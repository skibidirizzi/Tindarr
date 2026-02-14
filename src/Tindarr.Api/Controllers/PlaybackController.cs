using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using Tindarr.Application.Abstractions.Security;
using Tindarr.Application.Interfaces.Playback;
using Tindarr.Domain.Common;

namespace Tindarr.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/v1/playback")]
public sealed class PlaybackController(
	IPlaybackTokenService playbackTokenService,
	IEnumerable<IPlaybackProvider> providers,
	IHttpClientFactory httpClientFactory,
	ILogger<PlaybackController> logger) : ControllerBase
{
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
		CopyRequestHeaderIfPresent(HeaderNames.Range);
		CopyRequestHeaderIfPresent(HeaderNames.IfRange);
		CopyRequestHeaderIfPresent(HeaderNames.IfModifiedSince);
		CopyRequestHeaderIfPresent(HeaderNames.IfNoneMatch);
		CopyRequestHeaderIfPresent(HeaderNames.Accept);

		foreach (var header in upstream.Headers)
		{
			upstreamRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
		}

		using HttpResponseMessage upstreamResponse = await client
			.SendAsync(upstreamRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
			.ConfigureAwait(false);

		logger.LogDebug("Upstream playback response. StatusCode={StatusCode} ContentType={ContentType}", (int)upstreamResponse.StatusCode, upstreamResponse.Content.Headers.ContentType?.ToString());

		Response.StatusCode = (int)upstreamResponse.StatusCode;
		CopyResponseHeaderIfPresent(HeaderNames.ContentType);
		CopyResponseHeaderIfPresent(HeaderNames.ContentLength);
		CopyResponseHeaderIfPresent(HeaderNames.AcceptRanges);
		CopyResponseHeaderIfPresent(HeaderNames.ContentRange);
		CopyResponseHeaderIfPresent(HeaderNames.ETag);
		CopyResponseHeaderIfPresent(HeaderNames.LastModified);

		// Some servers set Content-Disposition; ok for casting.
		CopyResponseHeaderIfPresent(HeaderNames.ContentDisposition);

		// Stream body
		await upstreamResponse.Content.CopyToAsync(Response.Body, cancellationToken).ConfigureAwait(false);
		return new EmptyResult();

		void CopyRequestHeaderIfPresent(string headerName)
		{
			if (Request.Headers.TryGetValue(headerName, out var values))
			{
				upstreamRequest.Headers.TryAddWithoutValidation(headerName, (IEnumerable<string>)values);
			}
		}

		void CopyResponseHeaderIfPresent(string headerName)
		{
			if (upstreamResponse.Headers.TryGetValues(headerName, out var values)
				|| upstreamResponse.Content.Headers.TryGetValues(headerName, out values))
			{
				Response.Headers[headerName] = values.ToArray();
			}
		}
	}
}
