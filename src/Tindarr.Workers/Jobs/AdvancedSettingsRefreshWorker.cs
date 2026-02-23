using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tindarr.Application.Abstractions.Ops;
using Tindarr.Application.Abstractions.Persistence;

namespace Tindarr.Workers.Jobs;

/// <summary>
/// Polls advanced settings UpdatedAtUtc and signals the cache to reload ~1 second after a put
/// so Workers (separate process from API) pick up new TMDB credentials without restart.
/// </summary>
public sealed class AdvancedSettingsRefreshWorker(
	IServiceScopeFactory scopeFactory,
	IEffectiveAdvancedSettings effectiveAdvancedSettings,
	ILogger<AdvancedSettingsRefreshWorker> logger) : PeriodicBackgroundService(logger)
{
	protected override TimeSpan Interval => TimeSpan.FromSeconds(2);

	protected override async Task ExecuteOnceAsync(CancellationToken stoppingToken)
	{
		try
		{
			using var scope = scopeFactory.CreateScope();
			var repo = scope.ServiceProvider.GetRequiredService<IAdvancedSettingsRepository>();
			var record = await repo.GetAsync(stoppingToken).ConfigureAwait(false);
			var dbUpdatedAt = record?.UpdatedAtUtc ?? DateTimeOffset.MinValue;
			effectiveAdvancedSettings.SignalSettingsUpdated(dbUpdatedAt);
		}
		catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception ex)
		{
			Logger.LogDebug(ex, "Advanced settings refresh check failed");
		}
	}
}
