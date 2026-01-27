using Microsoft.Extensions.Logging;

namespace Tindarr.Workers.Jobs;

/// <summary>
/// Submits accepted movies to Radarr and tracks request outcomes.
/// Core-only: no request queue/status persistence yet.
/// </summary>
public sealed class RadarrRequestWorker(ILogger<RadarrRequestWorker> logger) : PeriodicBackgroundService(logger)
{
	protected override TimeSpan Interval => TimeSpan.FromMinutes(1);

	protected override Task ExecuteOnceAsync(CancellationToken stoppingToken)
	{
		Logger.LogDebug("Radarr request worker tick (core-only stub)");
		return Task.CompletedTask;
	}
}

