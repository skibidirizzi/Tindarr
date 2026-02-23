using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tindarr.Application.Abstractions.Ops;

namespace Tindarr.Workers.Jobs;

/// <summary>
/// Periodically fills the TMDB metadata and image cache in the background until the configured
/// "max movies" or "max image cache size" is reached (whichever is checked first). Runs every 45s,
/// offset from details backfill so prewarm and details run async and details never catches up.
/// </summary>
public sealed class TmdbDiscoverPrewarmWorker(
	IServiceScopeFactory scopeFactory,
	ILogger<TmdbDiscoverPrewarmWorker> logger) : PeriodicBackgroundService(logger)
{
	private const int IntervalSeconds = 45;
	private int _skip;

	protected override TimeSpan Interval => TimeSpan.FromSeconds(IntervalSeconds);

	protected override TimeSpan InitialDelay => TimeSpan.FromSeconds(5);

	protected override async Task ExecuteOnceAsync(CancellationToken stoppingToken)
	{
		Logger.LogInformation("TmdbDiscoverPrewarmWorker tick (every {Interval}s until max movies or max image cache).", IntervalSeconds);
		using var scope = scopeFactory.CreateScope();
		var runner = scope.ServiceProvider.GetRequiredService<ITmdbDiscoverPrewarmRunner>();
		_skip = await runner.RunOnceAsync(_skip, stoppingToken).ConfigureAwait(false);
	}
}
