using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Tindarr.Application.Abstractions.Caching;
using Tindarr.Application.Abstractions.Integrations;
using Tindarr.Application.Abstractions.Ops;
using Tindarr.Application.Abstractions.Persistence;
using Tindarr.Application.Interfaces.Ops;
using Tindarr.Application.Abstractions.Security;
using Tindarr.Application.Options;
using Tindarr.Contracts.Auth;
using Tindarr.Contracts.Setup;
using Tindarr.Contracts.Tmdb;
using Tindarr.Domain.Common;
using Tindarr.Api.Services;
using Tindarr.Infrastructure.Integrations.Tmdb.Http;

namespace Tindarr.Api.Controllers;

[ApiController]
[Route("api/v1/setup")]
public sealed class SetupController(
	IUserRepository users,
	IPasswordHasher passwordHasher,
	ITokenService tokenService,
	Microsoft.Extensions.Options.IOptions<RegistrationOptions> registrationOptions,
	IEffectiveRegistrationOptions effectiveRegistration,
	IServiceSettingsRepository serviceSettings,
	IPlexLibrarySyncJobService plexLibrarySyncJob,
	IServiceScopeFactory scopeFactory,
	ITmdbBuildJob tmdbBuildJob,
	ITmdbDiscoverPrewarmRunner prewarmRunner,
	Tindarr.Application.Abstractions.Ops.IPopulateProgressReport populateProgressReport,
	ILanAddressResolver lanAddressResolver,
	IWanAddressResolver wanAddressResolver,
	IHttpClientFactory httpClientFactory) : ControllerBase
{
	/// <summary>
	/// Returns whether initial setup has been completed (at least one user exists).
	/// Allowed without authentication so the UI can redirect to the setup wizard on first run.
	/// </summary>
	[HttpGet("status")]
	[AllowAnonymous]
	public async Task<ActionResult<SetupStatusResponse>> GetStatus(CancellationToken cancellationToken)
	{
		var list = await users.ListAsync(0, 1, cancellationToken);
		return Ok(new SetupStatusResponse(list.Count > 0));
	}

	/// <summary>
	/// Creates the initial admin user and returns an auth response. Only allowed when no users exist.
	/// </summary>
	[HttpPost("admin")]
	[AllowAnonymous]
	public async Task<ActionResult<AuthResponse>> CreateInitialAdmin(
		[FromBody] SetupAdminRequest request,
		CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(request?.Password))
		{
			return BadRequest("Password is required.");
		}

		var list = await users.ListAsync(0, 1, cancellationToken);
		if (list.Count > 0)
		{
			return BadRequest("Setup already completed. Use login to sign in.");
		}

		const string adminId = "admin";
		const string displayName = "Admin";
		var now = DateTimeOffset.UtcNow;

		await users.CreateAsync(new CreateUserRecord(adminId, displayName, now), cancellationToken);

		var hashed = passwordHasher.Hash(request.Password.Trim(), registrationOptions.Value.PasswordHashIterations);
		await users.SetPasswordAsync(adminId, hashed.Hash, hashed.Salt, hashed.Iterations, cancellationToken);

		var roles = new List<string> { effectiveRegistration.DefaultRole, "Admin" };
		await users.SetRolesAsync(adminId, roles, cancellationToken);

		var token = tokenService.IssueAccessToken(adminId, roles);
		return Ok(new AuthResponse(
			token.AccessToken,
			token.ExpiresAtUtc,
			adminId,
			displayName,
			roles));
	}

	/// <summary>
	/// Returns suggested LAN/WAN base URL values (port from request; LAN from machine NICs, WAN from config or external service).
	/// Admin only; used during guided setup.
	/// </summary>
	[HttpGet("suggested-urls")]
	[Authorize(Policy = Tindarr.Api.Auth.Policies.AdminOnly)]
	public async Task<ActionResult<SuggestedUrlsResponse>> GetSuggestedUrls(
		[FromServices] Microsoft.Extensions.Options.IOptions<BaseUrlOptions> baseUrlOptions,
		CancellationToken cancellationToken)
	{
		var connection = HttpContext.Connection;
		int? port = null;
		if (connection.LocalPort > 0)
		{
			port = connection.LocalPort;
		}

		// Try to get port from Host header (e.g. when behind reverse proxy).
		var hostHeader = HttpContext.Request.Headers.Host.ToString();
		if (!string.IsNullOrWhiteSpace(hostHeader) && hostHeader.Contains(':', StringComparison.Ordinal))
		{
			var lastColon = hostHeader.LastIndexOf(':');
			if (int.TryParse(hostHeader.AsSpan(lastColon + 1), out var p) && p > 0 && p <= 65535)
			{
				port = p;
			}
		}

		// Prefer machine LAN IP from NICs so we suggest a reachable address even when the request came from localhost.
		string? suggestedLan = null;
		var lanIp = lanAddressResolver.GetLanIPv4();
		if (lanIp is not null && port is not null)
		{
			var host = lanIp.ToString();
			if (lanIp.IsIPv4MappedToIPv6)
			{
				host = lanIp.MapToIPv4().ToString();
			}
			suggestedLan = $"{host}:{port}";
		}
		else if (connection.LocalIpAddress is { } localIp && port is not null)
		{
			var host = localIp.ToString();
			if (localIp.IsIPv4MappedToIPv6)
			{
				host = localIp.MapToIPv4().ToString();
			}
			suggestedLan = $"{host}:{port}";
		}

		// WAN: from config first (unless config is loopback or private IP), else best-effort public IP via external service.
		string? suggestedWan = null;
		var wan = baseUrlOptions.Value.Wan;
		if (!string.IsNullOrWhiteSpace(wan) && Uri.TryCreate(wan, UriKind.Absolute, out var wanUri) && !string.IsNullOrWhiteSpace(wanUri.Host))
		{
			// Do not suggest localhost, loopback, or private (RFC 1918) IPs for WAN; fall through to public IP detection.
			if (System.Net.IPAddress.TryParse(wanUri.Host, out var wanHostIp))
			{
				var ip = wanHostIp.IsIPv4MappedToIPv6 ? wanHostIp.MapToIPv4() : wanHostIp;
				if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !System.Net.IPAddress.IsLoopback(ip) && !IsPrivateIPv4(ip))
				{
					suggestedWan = wanUri.Host + (wanUri.Port is > 0 and var wp ? $":{wp}" : "");
				}
			}
			else
			{
				// Host is a hostname (e.g. example.com), not an IP; allow it as WAN.
				var hostLower = wanUri.Host.Trim().ToLowerInvariant();
				if (hostLower != "localhost")
				{
					suggestedWan = wanUri.Host + (wanUri.Port is > 0 and var wp ? $":{wp}" : "");
				}
			}
		}
		if (suggestedWan is null && port is not null)
		{
			using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			cts.CancelAfter(TimeSpan.FromSeconds(2));
			var publicIp = await wanAddressResolver.GetPublicIPv4Async(cts.Token).ConfigureAwait(false);
			if (!string.IsNullOrWhiteSpace(publicIp))
			{
				suggestedWan = $"{publicIp}:{port}";
			}
		}

		return Ok(new SuggestedUrlsResponse(port, suggestedLan, suggestedWan));
	}

	/// <summary>
	/// Checks whether a base URL is reachable (HTTP GET). Admin only; used by setup wizard "Try default".
	/// </summary>
	[HttpPost("check-reachability")]
	[Authorize(Policy = Tindarr.Api.Auth.Policies.AdminOnly)]
	public async Task<ActionResult<CheckReachabilityResponse>> CheckReachability(
		[FromBody] CheckReachabilityRequest request,
		CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(request?.BaseUrl))
		{
			return BadRequest("BaseUrl is required.");
		}

		var baseUrl = EnsureHttpOrHttps(request.BaseUrl.Trim());
		if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
		{
			return Ok(new CheckReachabilityResponse(false, "BaseUrl must use http:// or https://."));
		}

		try
		{
			using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			cts.CancelAfter(TimeSpan.FromSeconds(3));
			var client = httpClientFactory.CreateClient();
			client.Timeout = TimeSpan.FromSeconds(3);
			using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
			// Any HTTP response (including 401/403) means the server is reachable
			return Ok(new CheckReachabilityResponse(true, null));
		}
		catch (OperationCanceledException)
		{
			// Covers TaskCanceledException (timeout) as well
			return Ok(new CheckReachabilityResponse(false, "Connection timed out."));
		}
		catch (HttpRequestException ex)
		{
			return Ok(new CheckReachabilityResponse(false, ex.Message ?? "Connection failed."));
		}
	}

	private static string EnsureHttpOrHttps(string baseUrl)
	{
		var t = (baseUrl ?? string.Empty).Trim();
		if (t.Length >= 8 && (t.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || t.StartsWith("http://", StringComparison.OrdinalIgnoreCase)))
			return t;
		return "http://" + t;
	}

	/// <summary>
	/// Optionally run library sync for all media servers and start TMDB build with rate-limit bypass.
	/// Admin only. Excluded from API rate limiting so setup can trigger population in one request.
	/// </summary>
	[HttpPost("complete")]
	[Authorize(Policy = Tindarr.Api.Auth.Policies.AdminOnly)]
	public async Task<ActionResult<SetupCompleteResponse>> Complete(
		[FromBody] SetupCompleteRequest request,
		CancellationToken cancellationToken)
	{
		var message = new List<string>();
		var syncTasks = new List<Task>();

		// Prewarm first: same process as TMDB admin tab. Run until 200 movies in DB so user sees a populated swipe deck on first run.
		const int initialPrewarmTarget = 500;
		const int maxPrewarmPasses = 200;
		var offset = 0;
		_ = await prewarmRunner.RunOnceAsync(0, cancellationToken).ConfigureAwait(false);
		var passes = 1;
		while (passes < maxPrewarmPasses)
		{
			using (var scope = scopeFactory.CreateScope())
			{
				var metadataStore = scope.ServiceProvider.GetRequiredService<ITmdbMetadataStore>();
				var stats = await metadataStore.GetStatsAsync(cancellationToken).ConfigureAwait(false);
				if (stats.MovieCount >= initialPrewarmTarget)
				{
					break;
				}
			}
			offset = await prewarmRunner.RunOnceAsync(offset, cancellationToken).ConfigureAwait(false);
			passes++;
		}
		message.Add(passes == 1 ? "Prewarm (first pass) completed." : $"Prewarm completed ({passes} passes, target {initialPrewarmTarget} movies).");

		if (request.RunLibrarySync)
		{
			var plexScopes = await serviceSettings.ListAsync(ServiceType.Plex, cancellationToken);
			foreach (var row in plexScopes)
			{
				if (string.IsNullOrWhiteSpace(row.ServerId) || string.Equals(row.ServerId, PlexConstants.AccountServerId, StringComparison.OrdinalIgnoreCase))
					continue;
				if (string.IsNullOrWhiteSpace(row.PlexServerUri))
					continue;
				var scope = new ServiceScope(ServiceType.Plex, row.ServerId);
				await plexLibrarySyncJob.StartAsync(scope, cancellationToken).ConfigureAwait(false);
				syncTasks.Add(plexLibrarySyncJob.WhenFinishedAsync(scope, cancellationToken));
				message.Add($"Plex {row.ServerId} sync started.");
			}

			var jellyfinScopes = await serviceSettings.ListAsync(ServiceType.Jellyfin, cancellationToken);
			foreach (var row in jellyfinScopes)
			{
				if (string.IsNullOrWhiteSpace(row.ServerId) || string.IsNullOrWhiteSpace(row.JellyfinBaseUrl))
					continue;
				var scope = new ServiceScope(ServiceType.Jellyfin, row.ServerId);
				var scopeCopy = scope;
				var jfTask = Task.Run(async () =>
				{
					using var sp = scopeFactory.CreateScope();
					var svc = sp.ServiceProvider.GetRequiredService<Tindarr.Application.Interfaces.Integrations.IJellyfinService>();
					try
					{
						await svc.SyncLibraryAsync(scopeCopy, CancellationToken.None);
					}
					catch
					{
						// best-effort; sync can be retried from Admin
					}
				});
				syncTasks.Add(jfTask);
				message.Add($"Jellyfin {row.ServerId} sync started.");
			}

			var embyScopes = await serviceSettings.ListAsync(ServiceType.Emby, cancellationToken);
			foreach (var row in embyScopes)
			{
				if (string.IsNullOrWhiteSpace(row.ServerId) || string.IsNullOrWhiteSpace(row.EmbyBaseUrl))
					continue;
				var scope = new ServiceScope(ServiceType.Emby, row.ServerId);
				var scopeCopy = scope;
				var embyTask = Task.Run(async () =>
				{
					using var sp = scopeFactory.CreateScope();
					var svc = sp.ServiceProvider.GetRequiredService<Tindarr.Application.Interfaces.Integrations.IEmbyService>();
					try
					{
						await svc.SyncLibraryAsync(scopeCopy, CancellationToken.None);
					}
					catch
					{
						// best-effort
					}
				});
				syncTasks.Add(embyTask);
				message.Add($"Emby {row.ServerId} sync started.");
			}
		}

		if (request.RunTmdbBuild)
		{
			// Same defaults as TMDB admin tab: rate limit bypass, 50 discover per user, prefetch images.
			var started = tmdbBuildJob.TryStart(new StartTmdbBuildRequest(
				RateLimitOverride: true,
				UsersBatchSize: 10,
				DiscoverLimitPerUser: 50,
				PrefetchImages: true));
			message.Add(started ? "TMDB build started (rate limit bypassed)." : "TMDB build already running or not started.");
		}

		// Run fetch-all-details and fetch-all-images only after library sync has completed,
		// so the TMDB metadata store is seeded with library movies and batch jobs can fill them.
		if (request.RunFetchAllDetails || request.RunFetchAllImages)
		{
			if (syncTasks.Count > 0)
			{
				await Task.WhenAll(syncTasks).ConfigureAwait(false);
				message.Add("Library sync completed; starting fetch-all-details and fetch-all-images.");
			}

			int detailsTotal = 0;
			int imagesTotal = 0;
			using (var scope = scopeFactory.CreateScope())
			{
				var metadataStore = scope.ServiceProvider.GetRequiredService<ITmdbMetadataStore>();
				if (request.RunFetchAllDetails)
					detailsTotal = await metadataStore.CountMoviesNeedingDetailsAsync(cancellationToken).ConfigureAwait(false);
				if (request.RunFetchAllImages)
				{
					var stats = await metadataStore.GetStatsAsync(cancellationToken).ConfigureAwait(false);
					imagesTotal = Math.Max(0, stats.MovieCount);
				}
			}

			populateProgressReport.SetRunning(detailsTotal, imagesTotal);

			var factory = scopeFactory;
			var progressReport = populateProgressReport;
			var detailsTask = request.RunFetchAllDetails
				? Task.Run(() => RunFetchAllDetailsBatchAsync(factory))
				: Task.CompletedTask;
			var imagesTask = request.RunFetchAllImages
				? Task.Run(() => RunFetchAllImagesBatchAsync(factory))
				: Task.CompletedTask;
			_ = Task.WhenAll(detailsTask, imagesTask).ContinueWith(_ => progressReport.SetIdle(), TaskContinuationOptions.None);
			if (request.RunFetchAllDetails)
				message.Add("Fetch all details started (rate limit bypassed).");
			if (request.RunFetchAllImages)
				message.Add("Fetch all images started.");
		}

		return Accepted(new SetupCompleteResponse(message.Count > 0 ? string.Join(" ", message) : "Nothing to run."));
	}

	private static async Task RunFetchAllDetailsBatchAsync(IServiceScopeFactory scopeFactory)
	{
		using var scope = scopeFactory.CreateScope();
		var sp = scope.ServiceProvider;
		var metadataStore = sp.GetRequiredService<ITmdbMetadataStore>();
		var tmdbClient = sp.GetRequiredService<ITmdbClient>();
		var effectiveSettings = sp.GetRequiredService<IEffectiveAdvancedSettings>();
		var progressReport = sp.GetRequiredService<Tindarr.Application.Abstractions.Ops.IPopulateProgressReport>();
		if (!effectiveSettings.HasEffectiveTmdbCredentials())
			return;
		TmdbRateLimitingHandler.BypassRateLimit.Value = true;
		try
		{
			var detailsDone = 0;
			const int chunkSize = 1000;
			while (true)
			{
				var ids = await metadataStore.ListMoviesNeedingDetailsAsync(chunkSize, CancellationToken.None).ConfigureAwait(false);
				if (ids.Count == 0)
					break;
				foreach (var tmdbId in ids)
				{
					try
					{
						var details = await tmdbClient.GetMovieDetailsAsync(tmdbId, CancellationToken.None).ConfigureAwait(false);
						if (details is not null)
						{
							await metadataStore.UpdateMovieDetailsAsync(details, CancellationToken.None).ConfigureAwait(false);
							detailsDone++;
							progressReport.SetDetailsDone(detailsDone);
						}
					}
					catch
					{
						// best-effort per movie
					}
				}
			}
		}
		finally
		{
			TmdbRateLimitingHandler.BypassRateLimit.Value = false;
		}
	}

	private static async Task RunFetchAllImagesBatchAsync(IServiceScopeFactory scopeFactory)
	{
		using var scope = scopeFactory.CreateScope();
		var sp = scope.ServiceProvider;
		var metadataStore = sp.GetRequiredService<ITmdbMetadataStore>();
		var imageCache = sp.GetRequiredService<ITmdbImageCache>();
		var tmdbOptions = sp.GetRequiredService<IOptions<TmdbOptions>>();
		var effectiveSettings = sp.GetRequiredService<IEffectiveAdvancedSettings>();
		var progressReport = sp.GetRequiredService<Tindarr.Application.Abstractions.Ops.IPopulateProgressReport>();
		if (!effectiveSettings.HasEffectiveTmdbCredentials())
			return;
		var settings = await metadataStore.GetSettingsAsync(CancellationToken.None).ConfigureAwait(false);
		if (settings.PosterMode != TmdbPosterMode.LocalProxy || settings.ImageCacheMaxMb <= 0)
			return;
		var take = 500;
		var skip = 0;
		var imagesDone = 0;
		while (true)
		{
			var chunk = await metadataStore.ListMoviesAsync(skip, take, missingDetailsOnly: false, titleQuery: null, CancellationToken.None).ConfigureAwait(false);
			if (chunk.Count == 0)
				break;
			foreach (var movie in chunk)
			{
				try
				{
					if (!string.IsNullOrWhiteSpace(movie.PosterPath))
						await imageCache.GetOrFetchAsync(tmdbOptions.Value.PosterSize, movie.PosterPath, CancellationToken.None).ConfigureAwait(false);
					if (!string.IsNullOrWhiteSpace(movie.BackdropPath))
						await imageCache.GetOrFetchAsync(tmdbOptions.Value.BackdropSize, movie.BackdropPath, CancellationToken.None).ConfigureAwait(false);
					imagesDone++;
					progressReport.SetImagesDone(imagesDone);
				}
				catch
				{
					// best-effort per movie
				}
			}
			var maxBytes = (long)Math.Max(0, settings.ImageCacheMaxMb) * 1024L * 1024L;
			await imageCache.PruneAsync(maxBytes, CancellationToken.None).ConfigureAwait(false);
			skip += chunk.Count;
			if (chunk.Count < take)
				break;
		}
	}

	/// <summary>RFC 1918 private IPv4 (10/8, 172.16/12, 192.168/16).</summary>
	private static bool IsPrivateIPv4(System.Net.IPAddress ip)
	{
		var bytes = (ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip).GetAddressBytes();
		if (bytes.Length != 4) return false;
		// 10.0.0.0/8
		if (bytes[0] == 10) return true;
		// 172.16.0.0/12
		if (bytes[0] == 172 && (bytes[1] & 0xF0) == 0x10) return true;
		// 192.168.0.0/16
		if (bytes[0] == 192 && bytes[1] == 168) return true;
		return false;
	}
}
