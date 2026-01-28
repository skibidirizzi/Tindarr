using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tindarr.Application.Abstractions.Integrations;
using Tindarr.Application.Abstractions.Persistence;
using Tindarr.Application.Interfaces.Integrations;
using Tindarr.Application.Options;
using Tindarr.Domain.Common;

namespace Tindarr.Workers.Jobs;

/// <summary>
/// Periodically syncs Plex library cache and TMDB enrichment.
/// </summary>
public sealed class PlexLibrarySyncWorker(
	IServiceScopeFactory scopeFactory,
	IOptions<PlexOptions> options,
	ILogger<PlexLibrarySyncWorker> logger) : PeriodicBackgroundService(logger)
{
	private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
	private readonly PlexOptions _options = options.Value;

	protected override TimeSpan Interval => TimeSpan.FromMinutes(Math.Clamp(_options.LibrarySyncMinutes, 1, 1440));

	protected override TimeSpan InitialDelay => TimeSpan.FromSeconds(15);

	protected override async Task ExecuteOnceAsync(CancellationToken stoppingToken)
	{
		using var scope = _scopeFactory.CreateScope();
		var settingsRepo = scope.ServiceProvider.GetRequiredService<IServiceSettingsRepository>();
		var plexService = scope.ServiceProvider.GetRequiredService<IPlexService>();

		var settings = await settingsRepo.ListAsync(ServiceType.Plex, stoppingToken);
		if (settings.Count == 0)
		{
			Logger.LogDebug("plex library sync tick (no settings)");
			return;
		}

		foreach (var entry in settings)
		{
			if (string.Equals(entry.ServerId, PlexConstants.AccountServerId, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			var serviceScope = new ServiceScope(entry.ServiceType, entry.ServerId);
			await plexService.EnsureLibrarySyncAsync(serviceScope, stoppingToken);
		}
	}
}
