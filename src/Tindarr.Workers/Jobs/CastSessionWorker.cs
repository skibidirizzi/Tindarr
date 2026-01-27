using Microsoft.Extensions.Logging;

namespace Tindarr.Workers.Jobs;

/// <summary>
/// Monitors active cast sessions; cleans up orphaned sessions and emits disconnect events.
/// Core-only: no cast session store integration yet.
/// </summary>
public sealed class CastSessionWorker(ILogger<CastSessionWorker> logger) : PeriodicBackgroundService(logger)
{
	protected override TimeSpan Interval => TimeSpan.FromSeconds(10);

	protected override Task ExecuteOnceAsync(CancellationToken stoppingToken)
	{
		Logger.LogDebug("Cast session worker tick (core-only stub)");
		return Task.CompletedTask;
	}
}

