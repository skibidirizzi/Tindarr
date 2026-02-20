using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using QRCoder;
using System.Net;
using System.Net.NetworkInformation;
using Tindarr.Api.Auth;
using Tindarr.Application.Abstractions.Persistence;
using Tindarr.Application.Abstractions.Security;
using Tindarr.Application.Interfaces.Playback;
using Tindarr.Application.Interfaces.Casting;
using Tindarr.Application.Interfaces.Ops;
using Tindarr.Application.Interfaces.Rooms;
using Tindarr.Application.Options;
using Tindarr.Contracts.Casting;
using Tindarr.Domain.Common;

namespace Tindarr.Api.Controllers;

[ApiController]
[Route("api/v1/casting")]
public sealed class CastingController(
	ICastClient castClient,
	ICastUrlTokenService castUrlTokenService,
	IPlaybackTokenService playbackTokenService,
	IEnumerable<IPlaybackProvider> playbackProviders,
	IRoomService roomService,
	IBaseUrlResolver baseUrlResolver,
	IJoinAddressSettingsRepository joinAddressSettings,
	IOptions<BaseUrlOptions> baseUrlOptions,
	ILogger<CastingController> logger,
	Tindarr.Infrastructure.Casting.CastingSessionStore castingSessionStore) : ControllerBase
{
	[HttpPost("sessions/{sessionId}/end")]
	[Authorize]
	public IActionResult EndCastingSession([FromRoute] string sessionId)
	{
		if (string.IsNullOrWhiteSpace(sessionId))
		{
			return BadRequest("sessionId is required.");
		}

		castingSessionStore.EndSession(sessionId.Trim());
		return Ok();
	}

	[HttpGet("devices")]
	[Authorize]
	public async Task<ActionResult<IReadOnlyList<CastDeviceDto>>> ListDevices(CancellationToken cancellationToken)
	{
		var devices = await castClient.DiscoverAsync(cancellationToken).ConfigureAwait(false);
		var dtos = devices
			.Select(d => new CastDeviceDto(d.Id, d.Name, d.Address, d.Port))
			.ToList();
		return Ok(dtos);
	}

	[HttpGet("rooms/{roomId}/qr.png")]
	[AllowAnonymous]
	public async Task<IActionResult> GetRoomQrPng(
		[FromRoute] string roomId,
		[FromQuery] string? token,
		[FromQuery] string? variant,
		CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(roomId))
		{
			return BadRequest("Room id is required.");
		}

		if (string.IsNullOrWhiteSpace(token) || !castUrlTokenService.TryValidateRoomQrToken(token, roomId, DateTimeOffset.UtcNow))
		{
			return Unauthorized();
		}

		var room = await roomService.GetAsync(roomId, cancellationToken).ConfigureAwait(false);
		if (room is null)
		{
			return NotFound("Room not found.");
		}

		var joinUrl = await BuildJoinUrlAsync(room.RoomId, variant, cancellationToken).ConfigureAwait(false);
		if (joinUrl is null)
		{
			return BadRequest("Join URL base is not configured. Ask an admin to set LAN/WAN join address in Admin Console.");
		}

		var bytes = BuildQrPng(joinUrl);
		return File(bytes, "image/png");
	}

	[HttpPost("rooms/{roomId}/qr")]
	[Authorize]
	public async Task<IActionResult> CastRoomQr(
		[FromRoute] string roomId,
		[FromBody] CastQrRequest request,
		CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(request.DeviceId))
		{
			return BadRequest("DeviceId is required.");
		}
		if (string.IsNullOrWhiteSpace(roomId))
		{
			return BadRequest("Room id is required.");
		}

		var room = await roomService.GetAsync(roomId, cancellationToken).ConfigureAwait(false);
		if (room is null)
		{
			return NotFound("Room not found.");
		}

		var baseUri = await ResolveCastFetchBaseUriAsync(cancellationToken).ConfigureAwait(false);
		if (baseUri is null)
		{
			return BadRequest("LAN cast base URL is not configured. Ask an admin to set LAN join address in Admin Console or configure 'BaseUrl:Lan'.");
		}

		var token = castUrlTokenService.IssueRoomQrToken(roomId, DateTimeOffset.UtcNow);
		var qrPath = $"api/v1/casting/rooms/{Uri.EscapeDataString(roomId)}/qr.png?token={Uri.EscapeDataString(token)}";
		if (!string.IsNullOrWhiteSpace(request.Variant) && (request.Variant.Equals("lan", StringComparison.OrdinalIgnoreCase) || request.Variant.Equals("wan", StringComparison.OrdinalIgnoreCase)))
		{
			qrPath += "&variant=" + Uri.EscapeDataString(request.Variant.Trim().ToLowerInvariant());
		}
		var qrUrl = new Uri(baseUri, qrPath);
		logger.LogInformation("Casting room QR to device {DeviceId}: {Url}", request.DeviceId, qrUrl);
		Response.Headers["X-Tindarr-Cast-Url"] = qrUrl.ToString();
		var serverUrls = GetServerUrlsHeaderValue();
		if (!string.IsNullOrWhiteSpace(serverUrls))
		{
			Response.Headers["X-Tindarr-Server-Urls"] = serverUrls;
		}
		if (!IsServerListeningOnNonLoopback())
		{
			return BadRequest("API is only listening on localhost. Chromecast cannot reach it. Start the API with '--urls http://0.0.0.0:<port>' (or bind to your LAN IP) and allow the port through Windows Firewall.");
		}

		await castClient.CastAsync(request.DeviceId, new CastMedia(
			ContentUrl: qrUrl.ToString(),
			ContentType: "image/png",
			Title: "Join room",
			SubTitle: roomId), cancellationToken).ConfigureAwait(false);

		return Ok();
	}

	[HttpGet("rooms/{roomId}/qr/cast-url")]
	[Authorize]
	public async Task<ActionResult<CastMediaUrlDto>> GetRoomQrCastUrl(
		[FromRoute] string roomId,
		[FromQuery] string? variant,
		CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(roomId))
		{
			return BadRequest("Room id is required.");
		}

		var room = await roomService.GetAsync(roomId, cancellationToken).ConfigureAwait(false);
		if (room is null)
		{
			return NotFound("Room not found.");
		}

		var baseUri = await ResolveCastFetchBaseUriAsync(cancellationToken).ConfigureAwait(false);
		if (baseUri is null)
		{
			return BadRequest("LAN cast base URL is not configured. Ask an admin to set LAN join address in Admin Console or configure 'BaseUrl:Lan'.");
		}

		if (!IsServerListeningOnNonLoopback())
		{
			return BadRequest("API is only listening on localhost. Chromecast cannot reach it. Start the API with '--urls http://0.0.0.0:<port>' (or bind to your LAN IP) and allow the port through Windows Firewall.");
		}

		var token = castUrlTokenService.IssueRoomQrToken(roomId, DateTimeOffset.UtcNow);
		var qrPath = $"api/v1/casting/rooms/{Uri.EscapeDataString(roomId)}/qr.png?token={Uri.EscapeDataString(token)}";
		if (!string.IsNullOrWhiteSpace(variant) && (variant.Equals("lan", StringComparison.OrdinalIgnoreCase) || variant.Equals("wan", StringComparison.OrdinalIgnoreCase)))
		{
			qrPath += "&variant=" + Uri.EscapeDataString(variant.Trim().ToLowerInvariant());
		}
		var qrUrl = new Uri(baseUri, qrPath);
		Response.Headers["X-Tindarr-Cast-Url"] = qrUrl.ToString();

		var sessionId = Guid.NewGuid().ToString();
		var contentRuntimeSeconds = 3600; // QR is “static”; treat as 1h unless ended explicitly.
		castingSessionStore.RegisterSession(
			sessionId: sessionId,
			deviceId: "sdk",
			contentTitle: "Join room",
			contentSubtitle: roomId,
			contentType: "image/png",
			contentRuntimeSeconds: contentRuntimeSeconds);

		return Ok(new CastMediaUrlDto(
			Url: qrUrl.ToString(),
			ContentType: "image/png",
			Title: "Join room",
			SubTitle: roomId,
			SessionId: sessionId));
	}

	[HttpPost("movie")]
	[Authorize]
	public async Task<IActionResult> CastMovie([FromBody] CastMovieRequest request, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(request.DeviceId))
			return BadRequest("DeviceId is required.");
		if (request.TmdbId <= 0)
			return BadRequest("TmdbId must be positive.");

		if (!ServiceScope.TryCreate(request.ServiceType, request.ServerId, out var scope) || scope is null)
			return BadRequest("ServiceType and ServerId are required.");

		var title = string.IsNullOrWhiteSpace(request.Title) ? $"TMDB:{request.TmdbId}" : request.Title.Trim();
		var contentRuntimeSeconds = 7200; // TODO: Lookup actual runtime if available

		var directUrl = await TryBuildDirectMovieCastUrlAsync(scope, request.TmdbId, cancellationToken).ConfigureAwait(false);
		if (directUrl is not null)
		{
			logger.LogInformation("Casting movie to device {DeviceId} via direct {ServiceType} URL.", request.DeviceId, scope.ServiceType);
			var sessionId = Guid.NewGuid().ToString();
			try
			{
				await castClient.CastAsync(request.DeviceId, new CastMedia(
					ContentUrl: directUrl.ToString(),
					ContentType: "video/mp4",
					Title: title,
					SubTitle: scope.ServiceType.ToString()), cancellationToken).ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				castingSessionStore.LogError(sessionId, request.DeviceId, "CastAsync failed; session was not started.", ex);
				throw;
			}
			castingSessionStore.RegisterSession(
				sessionId: sessionId,
				deviceId: request.DeviceId,
				contentTitle: title,
				contentSubtitle: scope.ServiceType.ToString(),
				contentType: "video/mp4",
				contentRuntimeSeconds: contentRuntimeSeconds);
			return Ok();
		}

		var baseUri = await ResolveCastFetchBaseUriAsync(cancellationToken).ConfigureAwait(false);
		if (baseUri is null)
			return BadRequest("LAN cast base URL is not configured. Ask an admin to set LAN join address in Admin Console or configure 'BaseUrl:Lan'.");

		var token = playbackTokenService.IssueMovieToken(scope, request.TmdbId, DateTimeOffset.UtcNow);
		var gatewaySessionId = Guid.NewGuid().ToString();
		var playbackUrl = new Uri(baseUri, $"api/v1/playback/movie/{Uri.EscapeDataString(scope.ServiceType.ToString().ToLowerInvariant())}/{Uri.EscapeDataString(scope.ServerId)}/{request.TmdbId}?token={Uri.EscapeDataString(token)}&castSessionId={Uri.EscapeDataString(gatewaySessionId)}");
		logger.LogInformation("Casting movie to device {DeviceId}: {Url}", request.DeviceId, playbackUrl);
		Response.Headers["X-Tindarr-Cast-Url"] = playbackUrl.ToString();
		var serverUrls = GetServerUrlsHeaderValue();
		if (!string.IsNullOrWhiteSpace(serverUrls))
			Response.Headers["X-Tindarr-Server-Urls"] = serverUrls;
		if (!IsServerListeningOnNonLoopback())
			return BadRequest("API is only listening on localhost. Chromecast cannot reach it. Start the API with '--urls http://0.0.0.0:<port>' (or bind to your LAN IP) and allow the port through Windows Firewall.");
		try
		{
			await castClient.CastAsync(request.DeviceId, new CastMedia(
				ContentUrl: playbackUrl.ToString(),
				ContentType: "video/mp4",
				Title: title,
				SubTitle: scope.ServiceType.ToString()), cancellationToken).ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			castingSessionStore.LogError(gatewaySessionId, request.DeviceId, "CastAsync failed; session was not started.", ex);
			throw;
		}

		castingSessionStore.RegisterSession(
			sessionId: gatewaySessionId,
			deviceId: request.DeviceId,
			contentTitle: title,
			contentSubtitle: scope.ServiceType.ToString(),
			contentType: "video/mp4",
			contentRuntimeSeconds: contentRuntimeSeconds);

		return Ok();
	}

	[HttpPost("movie/cast-url")]
	[Authorize]
	public async Task<ActionResult<CastMediaUrlDto>> GetMovieCastUrl([FromBody] GetMovieCastUrlRequest request, CancellationToken cancellationToken)
	{
		if (request.TmdbId <= 0)
		{
			return BadRequest("TmdbId must be positive.");
		}

		if (!ServiceScope.TryCreate(request.ServiceType, request.ServerId, out var scope) || scope is null)
		{
			return BadRequest("ServiceType and ServerId are required.");
		}

		var title = string.IsNullOrWhiteSpace(request.Title) ? $"TMDB:{request.TmdbId}" : request.Title.Trim();
		var deviceId = string.IsNullOrWhiteSpace(request.DeviceId) ? "sdk" : request.DeviceId.Trim();
		var sessionId = Guid.NewGuid().ToString();
		var contentRuntimeSeconds = 7200; // TODO: Lookup actual runtime if available

		var directUrl = await TryBuildDirectMovieCastUrlAsync(scope, request.TmdbId, cancellationToken).ConfigureAwait(false);
		if (directUrl is not null)
		{
			castingSessionStore.RegisterSession(
				sessionId: sessionId,
				deviceId: deviceId,
				contentTitle: title,
				contentSubtitle: scope.ServiceType.ToString(),
				contentType: "video/mp4",
				contentRuntimeSeconds: contentRuntimeSeconds);

			return Ok(new CastMediaUrlDto(
				Url: directUrl.ToString(),
				ContentType: "video/mp4",
				Title: title,
				SubTitle: scope.ServiceType.ToString(),
				SessionId: sessionId));
		}

		var baseUri = await ResolveCastFetchBaseUriAsync(cancellationToken).ConfigureAwait(false);
		if (baseUri is null)
		{
			return BadRequest("LAN cast base URL is not configured. Ask an admin to set LAN join address in Admin Console or configure 'BaseUrl:Lan'.");
		}

		if (!IsServerListeningOnNonLoopback())
		{
			return BadRequest("API is only listening on localhost. Chromecast cannot reach it. Start the API with '--urls http://0.0.0.0:<port>' (or bind to your LAN IP) and allow the port through Windows Firewall.");
		}

		var token = playbackTokenService.IssueMovieToken(scope, request.TmdbId, DateTimeOffset.UtcNow);
		var playbackUrl = new Uri(baseUri, $"api/v1/playback/movie/{Uri.EscapeDataString(scope.ServiceType.ToString().ToLowerInvariant())}/{Uri.EscapeDataString(scope.ServerId)}/{request.TmdbId}?token={Uri.EscapeDataString(token)}&castSessionId={Uri.EscapeDataString(sessionId)}");
		Response.Headers["X-Tindarr-Cast-Url"] = playbackUrl.ToString();

		castingSessionStore.RegisterSession(
			sessionId: sessionId,
			deviceId: deviceId,
			contentTitle: title,
			contentSubtitle: scope.ServiceType.ToString(),
			contentType: "video/mp4",
			contentRuntimeSeconds: contentRuntimeSeconds);

		return Ok(new CastMediaUrlDto(
			Url: playbackUrl.ToString(),
			ContentType: "video/mp4",
			Title: title,
			SubTitle: scope.ServiceType.ToString(),
			SessionId: sessionId));
	}

	private async Task<Uri?> TryBuildDirectMovieCastUrlAsync(ServiceScope scope, int tmdbId, CancellationToken cancellationToken)
	{
		var provider = playbackProviders.FirstOrDefault(p => p.ServiceType == scope.ServiceType);
		if (provider is not IDirectPlaybackProvider direct)
		{
			return null;
		}

		try
		{
			var uri = await direct.TryBuildDirectMovieStreamUrlAsync(scope, tmdbId, cancellationToken).ConfigureAwait(false);
			if (uri is null)
			{
				return null;
			}

			// If upstream is configured as localhost/loopback, Chromecast won't be able to reach it.
			// Best-effort: rewrite loopback to a LAN IP so we can still return a direct URL.
			if (IsLoopbackHost(uri.Host))
			{
				// Prefer the configured Rooms LAN host (host part only) since it's explicitly
				// the address other devices on the LAN should use to reach this machine.
				var joinSettings = await joinAddressSettings.GetAsync(cancellationToken).ConfigureAwait(false);
				var lanHostPort = joinSettings?.LanHostPort?.Trim();
				var lanHost = string.IsNullOrWhiteSpace(lanHostPort) ? null : lanHostPort.Split(':', 2, StringSplitOptions.TrimEntries)[0];
				if (!string.IsNullOrWhiteSpace(lanHost) && !IsLoopbackHost(lanHost))
				{
					uri = new UriBuilder(uri)
					{
						Host = lanHost
					}.Uri;
				}
				else
				{
					// Next best: the host the caller used to reach Tindarr (often already a LAN IP).
					var requestHost = Request.Host.Host;
					if (!string.IsNullOrWhiteSpace(requestHost) && !IsLoopbackHost(requestHost))
					{
						uri = new UriBuilder(uri)
						{
							Host = requestHost
						}.Uri;
					}
					else
					{
						var rewritten = RewriteLoopbackToLanIp(uri, uri.Port);
						if (rewritten is null)
						{
							return null;
						}

						uri = rewritten;
					}
				}
			}

			return uri;
		}
		catch (Exception ex)
		{
			logger.LogDebug(ex, "Failed to build direct movie stream URL; falling back to playback gateway. ServiceType={ServiceType} ServerId={ServerId} TmdbId={TmdbId}", scope.ServiceType, scope.ServerId, tmdbId);
			return null;
		}
	}

	private async Task<string?> BuildJoinUrlAsync(string roomId, string? variant, CancellationToken cancellationToken)
	{
		var settings = await joinAddressSettings.GetAsync(cancellationToken).ConfigureAwait(false);
		var lanHostPort = settings?.LanHostPort;
		var wanHostPort = settings?.WanHostPort;

		// When variant is specified (for cast QR hotswap), use that host:port.
		if (!string.IsNullOrWhiteSpace(variant))
		{
			var useLan = variant.Trim().Equals("lan", StringComparison.OrdinalIgnoreCase);
			var hostPort = useLan ? lanHostPort : wanHostPort;
			if (string.IsNullOrWhiteSpace(hostPort))
			{
				hostPort = useLan ? wanHostPort : lanHostPort;
			}
			if (!string.IsNullOrWhiteSpace(hostPort))
			{
				hostPort = hostPort.Trim().TrimEnd('/');
				var scheme = ResolveJoinScheme(hostPort, lanHostPort, wanHostPort, baseUrlOptions.Value, Request.Scheme);
				return $"{scheme}://{hostPort}/rooms/{Uri.EscapeDataString(roomId)}";
			}
		}

		var baseUri = await ResolveJoinBaseUriAsync(cancellationToken).ConfigureAwait(false);
		if (baseUri is null)
		{
			// If join base isn't configured, fall back to LAN-only base so the QR image can still render.
			baseUri = await ResolveCastFetchBaseUriAsync(cancellationToken).ConfigureAwait(false);
		}
		if (baseUri is null)
		{
			return null;
		}

		return new Uri(baseUri, $"rooms/{Uri.EscapeDataString(roomId)}").ToString();
	}

	private async Task<Uri?> ResolveCastFetchBaseUriAsync(CancellationToken cancellationToken)
	{
		// Chromecast fetches media from the server: always prefer LAN.
		var settings = await joinAddressSettings.GetAsync(cancellationToken).ConfigureAwait(false);
		var lanHostPort = settings?.LanHostPort;
		var wanHostPort = settings?.WanHostPort;

		var requestPort = Request.Host.Port;
		if (!string.IsNullOrWhiteSpace(lanHostPort))
		{
			var hostPort = lanHostPort.Trim().TrimEnd('/');
			var scheme = ResolveJoinScheme(hostPort, lanHostPort, wanHostPort, baseUrlOptions.Value, Request.Scheme);
			var uri = new Uri($"{scheme}://{hostPort}/", UriKind.Absolute);
			return RewriteLoopbackToLanIp(uri, requestPort);
		}

		if (!string.IsNullOrWhiteSpace(baseUrlOptions.Value.Lan)
			&& Uri.TryCreate(baseUrlOptions.Value.Lan.Trim(), UriKind.Absolute, out var lanUri)
			&& lanUri.Scheme is "http" or "https")
		{
			return RewriteLoopbackToLanIp(lanUri, requestPort);
		}

		// Last-resort fallback: use current request host if it isn't loopback.
		if (Request.Host.HasValue
			&& (Request.Scheme is "http" or "https")
			&& !string.Equals(Request.Host.Host, "localhost", StringComparison.OrdinalIgnoreCase)
			&& !string.Equals(Request.Host.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase)
			&& !string.Equals(Request.Host.Host, "::1", StringComparison.OrdinalIgnoreCase))
		{
			return new Uri($"{Request.Scheme}://{Request.Host.Value}/", UriKind.Absolute);
		}

		// If we're bound to localhost (common in dev), attempt to substitute a LAN IP.
		if (Request.Host.HasValue && (Request.Scheme is "http" or "https"))
		{
			var candidate = new Uri($"{Request.Scheme}://{Request.Host.Value}/", UriKind.Absolute);
			var rewritten = RewriteLoopbackToLanIp(candidate, requestPort);
			if (rewritten is not null)
			{
				return rewritten;
			}
		}

		return null;
	}

	private static Uri? RewriteLoopbackToLanIp(Uri uri, int? preferredPort)
	{
		if (!IsLoopbackHost(uri.Host))
		{
			return uri;
		}

		var lanIp = TryGetPrivateIPv4();
		if (lanIp is null)
		{
			return null;
		}

		var builder = new UriBuilder(uri)
		{
			Host = lanIp.ToString()
		};
		if (preferredPort is > 0)
		{
			builder.Port = preferredPort.Value;
		}
		return builder.Uri;
	}

	private static IPAddress? TryGetPrivateIPv4()
	{
		try
		{
			foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
			{
				if (nic.OperationalStatus != OperationalStatus.Up)
				{
					continue;
				}

				var props = nic.GetIPProperties();
				foreach (var uni in props.UnicastAddresses)
				{
					var ip = uni.Address;
					if (ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
					{
						continue;
					}
					if (IPAddress.IsLoopback(ip))
					{
						continue;
					}
					if (!IsPrivateIPv4(ip))
					{
						continue;
					}

					return ip;
				}
			}
		}
		catch
		{
			// ignore
		}

		return null;
	}

	private static bool IsPrivateIPv4(IPAddress ip)
	{
		var b = ip.GetAddressBytes();
		// 10.0.0.0/8
		if (b[0] == 10) return true;
		// 172.16.0.0/12
		if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return true;
		// 192.168.0.0/16
		if (b[0] == 192 && b[1] == 168) return true;
		return false;
	}

	private bool IsServerListeningOnNonLoopback()
	{
		var server = HttpContext.RequestServices.GetService<IServer>();
		var addresses = server?.Features.Get<IServerAddressesFeature>()?.Addresses;
		if (addresses is null || addresses.Count == 0)
		{
			// Unknown: don't block casting.
			return true;
		}

		foreach (var address in addresses)
		{
			if (!Uri.TryCreate(address, UriKind.Absolute, out var uri))
			{
				continue;
			}

			// Kestrel uses these to mean "any".
			if (string.Equals(uri.Host, "0.0.0.0", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(uri.Host, "[::]", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(uri.Host, "::", StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}

			if (!IsLoopbackHost(uri.Host))
			{
				return true;
			}
		}

		return false;
	}

	private string? GetServerUrlsHeaderValue()
	{
		var server = HttpContext.RequestServices.GetService<IServer>();
		var addresses = server?.Features.Get<IServerAddressesFeature>()?.Addresses;
		if (addresses is null || addresses.Count == 0)
		{
			return null;
		}

		// Avoid giant headers.
		return string.Join(";", addresses.Take(10));
	}

	private static bool IsLoopbackHost(string host)
	{
		if (string.IsNullOrWhiteSpace(host))
		{
			return true;
		}

		if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		return System.Net.IPAddress.TryParse(host, out var ip) && System.Net.IPAddress.IsLoopback(ip);
	}

	private async Task<Uri?> ResolveJoinBaseUriAsync(CancellationToken cancellationToken)
	{
		var settings = await joinAddressSettings.GetAsync(cancellationToken).ConfigureAwait(false);
		var lanHostPort = settings?.LanHostPort;
		var wanHostPort = settings?.WanHostPort;

		// Prefer explicit join-address settings (admin-configured) since Chromecast must reach the server.
		if (!string.IsNullOrWhiteSpace(lanHostPort) || !string.IsNullOrWhiteSpace(wanHostPort))
		{
			var clientIp = HttpContext.Connection.RemoteIpAddress;
			var mode = baseUrlOptions.Value.Mode;
			var useLan = mode switch
			{
				BaseUrlMode.ForceLan => true,
				BaseUrlMode.ForceWan => false,
				BaseUrlMode.Auto => clientIp is not null && baseUrlResolver.IsLanClient(clientIp),
				_ => false
			};

			var hostPort = useLan ? lanHostPort : wanHostPort;
			if (string.IsNullOrWhiteSpace(hostPort))
			{
				hostPort = useLan ? wanHostPort : lanHostPort;
			}

			hostPort = (hostPort ?? string.Empty).Trim().TrimEnd('/');
			if (string.IsNullOrWhiteSpace(hostPort))
			{
				return null;
			}

			var scheme = ResolveJoinScheme(hostPort, lanHostPort, wanHostPort, baseUrlOptions.Value, Request.Scheme);
			return new Uri($"{scheme}://{hostPort}/", UriKind.Absolute);
		}

		// Fallback: use configured BaseUrl (appsettings/env). Helpful in dev.
		try
		{
			var clientIp = HttpContext.Connection.RemoteIpAddress;
			return baseUrlResolver.GetBaseUri(clientIp);
		}
		catch
		{
			// ignore; fall through
		}

		// Final fallback: use the current request host.
		if (Request.Host.HasValue && (Request.Scheme is "http" or "https"))
		{
			return new Uri($"{Request.Scheme}://{Request.Host.Value}/", UriKind.Absolute);
		}

		return null;
	}

	private static string ResolveJoinScheme(
		string hostPort,
		string? lanHostPort,
		string? wanHostPort,
		BaseUrlOptions options,
		string? requestScheme)
	{
		var normalizedHostPort = (hostPort ?? string.Empty).Trim();
		var normalizedLan = (lanHostPort ?? string.Empty).Trim();
		var normalizedWan = (wanHostPort ?? string.Empty).Trim();

		string? preferredBase = null;
		if (!string.IsNullOrWhiteSpace(normalizedLan)
			&& string.Equals(normalizedHostPort, normalizedLan, StringComparison.OrdinalIgnoreCase))
		{
			preferredBase = options.Lan;
		}
		else if (!string.IsNullOrWhiteSpace(normalizedWan)
			&& string.Equals(normalizedHostPort, normalizedWan, StringComparison.OrdinalIgnoreCase))
		{
			preferredBase = options.Wan;
		}

		if (!string.IsNullOrWhiteSpace(preferredBase)
			&& Uri.TryCreate(preferredBase.Trim(), UriKind.Absolute, out var baseUri)
			&& baseUri.Scheme is "http" or "https")
		{
			return baseUri.Scheme;
		}

		if (Uri.TryCreate("http://" + normalizedHostPort, UriKind.Absolute, out var parsed))
		{
			return parsed.Port == 443 ? "https" : "http";
		}

		if (string.Equals(requestScheme, "https", StringComparison.OrdinalIgnoreCase))
		{
			return "https";
		}

		return "http";
	}

	private static byte[] BuildQrPng(string text)
	{
		using var generator = new QRCodeGenerator();
		using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.Q);
		var qr = new PngByteQRCode(data);
		return qr.GetGraphic(pixelsPerModule: 10);
	}
}
