using Microsoft.Extensions.Logging;

namespace Tindarr.Workers.Jobs;

/// <summary>
/// Builds curated/filtered candidate pools and precomputes room recommendations.
/// Core-only: no recommendation engine implementation yet.
/// </summary>
public sealed class RecommendationWorker(ILogger<RecommendationWorker> logger) : PeriodicBackgroundService(logger)
{
	protected override TimeSpan Interval => TimeSpan.FromMinutes(15);

	protected override Task ExecuteOnceAsync(CancellationToken stoppingToken)
	{
		Logger.LogDebug("Recommendation worker tick (core-only stub)");
		return Task.CompletedTask;
	}
}

