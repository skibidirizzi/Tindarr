using Microsoft.Extensions.Logging;

namespace Tindarr.Workers.Jobs;

/// <summary>
/// Expires inactive rooms and temporary guest users; purges stale caches.
/// Core-only: no persistence cleanup implementation yet.
/// </summary>
public sealed class CleanupWorker(ILogger<CleanupWorker> logger) : PeriodicBackgroundService(logger)
{
	protected override TimeSpan Interval => TimeSpan.FromMinutes(5);

	protected override Task ExecuteOnceAsync(CancellationToken stoppingToken)
	{
		Logger.LogDebug("Cleanup worker tick (core-only stub)");
		return Task.CompletedTask;
	}
}

