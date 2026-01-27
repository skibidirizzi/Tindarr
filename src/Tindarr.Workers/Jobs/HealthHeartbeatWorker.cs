using Microsoft.Extensions.Logging;

namespace Tindarr.Workers.Jobs;

/// <summary>
/// Performs periodic health checks against external dependencies and publishes snapshots.
/// Core-only: no dependency checks or publishing implementation yet.
/// </summary>
public sealed class HealthHeartbeatWorker(ILogger<HealthHeartbeatWorker> logger) : PeriodicBackgroundService(logger)
{
	protected override TimeSpan Interval => TimeSpan.FromSeconds(30);

	protected override Task ExecuteOnceAsync(CancellationToken stoppingToken)
	{
		Logger.LogDebug("Health heartbeat worker tick (core-only stub)");
		return Task.CompletedTask;
	}
}

