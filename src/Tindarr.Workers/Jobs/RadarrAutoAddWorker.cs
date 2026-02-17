using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tindarr.Application.Abstractions.Persistence;
using Tindarr.Application.Interfaces.Integrations;
using Tindarr.Application.Options;
using Tindarr.Domain.Common;

namespace Tindarr.Workers.Jobs;

/// <summary>
/// Periodically syncs radarr library cache and auto-adds accepted movies.
/// </summary>
public sealed class RadarrAutoAddWorker(
	IServiceScopeFactory scopeFactory,
	IOptions<RadarrOptions> options,
	ILogger<RadarrAutoAddWorker> logger) : PeriodicBackgroundService(logger)
{
	private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
	private readonly RadarrOptions _options = options.Value;

	// Interval is enforced per-scope using settings; keep a small tick so changes apply quickly.
	protected override TimeSpan Interval => TimeSpan.FromSeconds(30);

	protected override TimeSpan InitialDelay => TimeSpan.FromSeconds(10);

	protected override async Task ExecuteOnceAsync(CancellationToken stoppingToken)
	{
		using var scope = _scopeFactory.CreateScope();
		var settingsRepo = scope.ServiceProvider.GetRequiredService<IServiceSettingsRepository>();
		var radarrService = scope.ServiceProvider.GetRequiredService<IRadarrService>();

		var settings = await settingsRepo.ListAsync(ServiceType.Radarr, stoppingToken);
		if (settings.Count == 0)
		{
			Logger.LogDebug("radarr auto-add worker tick (no settings)");
			return;
		}

		var now = DateTimeOffset.UtcNow;
		foreach (var entry in settings)
		{
			var serviceScope = new ServiceScope(entry.ServiceType, entry.ServerId);

			var intervalMinutes = Math.Clamp(entry.RadarrAutoAddIntervalMinutes ?? _options.AutoAddMinutes, 1, 1440);
			var interval = TimeSpan.FromMinutes(intervalMinutes);
			if (entry.RadarrLastAutoAddRunUtc is not null && now - entry.RadarrLastAutoAddRunUtc.Value < interval)
			{
				continue;
			}

			await radarrService.EnsureLibrarySyncAsync(serviceScope, stoppingToken);
			var result = await radarrService.AutoAddAcceptedMoviesAsync(serviceScope, stoppingToken);
			await TryMarkLastRunAsync(settingsRepo, serviceScope, now, stoppingToken);

			Logger.LogInformation(
				"radarr auto-add processed scope {ServerId}: attempted={Attempted} added={Added} skipped={Skipped} failed={Failed}",
				entry.ServerId,
				result.Attempted,
				result.Added,
				result.SkippedExisting,
				result.Failed);
		}
	}

	private static async Task TryMarkLastRunAsync(
		IServiceSettingsRepository settingsRepo,
		ServiceScope scope,
		DateTimeOffset lastRunUtc,
		CancellationToken cancellationToken)
	{
		var settings = await settingsRepo.GetAsync(scope, cancellationToken).ConfigureAwait(false);
		if (settings is null)
		{
			return;
		}

		var upsert = new ServiceSettingsUpsert(
			settings.RadarrBaseUrl,
			settings.RadarrApiKey,
			settings.RadarrQualityProfileId,
			settings.RadarrRootFolderPath,
			settings.RadarrTagLabel,
			settings.RadarrTagId,
			settings.RadarrAutoAddEnabled,
			settings.RadarrAutoAddIntervalMinutes,
			lastRunUtc,
			settings.RadarrLastAutoAddAcceptedId,
			settings.RadarrLastLibrarySyncUtc,
			settings.MatchMinUsers,
			settings.MatchMinUserPercent,
			settings.JellyfinBaseUrl,
			settings.JellyfinApiKey,
			settings.JellyfinServerName,
			settings.JellyfinServerVersion,
			settings.JellyfinLastLibrarySyncUtc,
			settings.EmbyBaseUrl,
			settings.EmbyApiKey,
			settings.EmbyServerName,
			settings.EmbyServerVersion,
			settings.EmbyLastLibrarySyncUtc,
			settings.PlexClientIdentifier,
			settings.PlexAuthToken,
			settings.PlexServerName,
			settings.PlexServerUri,
			settings.PlexServerVersion,
			settings.PlexServerPlatform,
			settings.PlexServerOwned,
			settings.PlexServerOnline,
			settings.PlexServerAccessToken,
			settings.PlexLastLibrarySyncUtc);

		await settingsRepo.UpsertAsync(scope, upsert, cancellationToken).ConfigureAwait(false);
	}
}
