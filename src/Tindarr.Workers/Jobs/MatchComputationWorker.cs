using Microsoft.Extensions.Logging;

namespace Tindarr.Workers.Jobs;

/// <summary>
/// Processes swipe events and computes match state; emits match-found events.
/// Core-only: no persistence/matching engine integration yet.
/// </summary>
public sealed class MatchComputationWorker(ILogger<MatchComputationWorker> logger) : PeriodicBackgroundService(logger)
{
	protected override TimeSpan Interval => TimeSpan.FromSeconds(2);

	protected override Task ExecuteOnceAsync(CancellationToken stoppingToken)
	{
		Logger.LogDebug("Match computation worker tick (core-only stub)");
		return Task.CompletedTask;
	}
}

