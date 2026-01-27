using Microsoft.Extensions.Logging;

namespace Tindarr.Workers.Jobs;

/// <summary>
/// Syncs media server state (Plex/Jellyfin/Emby) and caches library/health.
/// Core-only: no provider integrations invoked yet.
/// </summary>
public sealed class MediaServerSyncWorker(ILogger<MediaServerSyncWorker> logger) : PeriodicBackgroundService(logger)
{
	protected override TimeSpan Interval => TimeSpan.FromMinutes(10);

	protected override Task ExecuteOnceAsync(CancellationToken stoppingToken)
	{
		Logger.LogDebug("Media server sync worker tick (core-only stub)");
		return Task.CompletedTask;
	}
}

