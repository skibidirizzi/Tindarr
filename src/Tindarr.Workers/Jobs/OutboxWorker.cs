using Microsoft.Extensions.Logging;

namespace Tindarr.Workers.Jobs;

/// <summary>
/// Delivers queued domain events (swipes, room updates, matches).
/// Core-only: no persistence/outbox implementation yet.
/// </summary>
public sealed class OutboxWorker(ILogger<OutboxWorker> logger) : PeriodicBackgroundService(logger)
{
	protected override TimeSpan Interval => TimeSpan.FromSeconds(2);

	protected override Task ExecuteOnceAsync(CancellationToken stoppingToken)
	{
		Logger.LogDebug("Outbox worker tick (core-only stub)");
		return Task.CompletedTask;
	}
}

