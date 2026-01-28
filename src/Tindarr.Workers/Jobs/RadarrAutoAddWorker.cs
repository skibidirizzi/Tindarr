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

	protected override TimeSpan Interval => TimeSpan.FromMinutes(Math.Clamp(_options.AutoAddMinutes, 1, 1440));

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

		foreach (var entry in settings)
		{
			var serviceScope = new ServiceScope(entry.ServiceType, entry.ServerId);
			await radarrService.EnsureLibrarySyncAsync(serviceScope, stoppingToken);
			var result = await radarrService.AutoAddAcceptedMoviesAsync(serviceScope, stoppingToken);

			Logger.LogInformation(
				"radarr auto-add processed scope {ServerId}: attempted={Attempted} added={Added} skipped={Skipped} failed={Failed}",
				entry.ServerId,
				result.Attempted,
				result.Added,
				result.SkippedExisting,
				result.Failed);
		}
	}
}
