using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tindarr.Application.Abstractions.Persistence;
using Tindarr.Application.Interfaces.Integrations;
using Tindarr.Domain.Common;

namespace Tindarr.Workers.Jobs;

public sealed class RadarrSuperlikeAddWorker(
	IServiceScopeFactory scopeFactory,
	ILogger<RadarrSuperlikeAddWorker> logger) : BackgroundService
{
	// Cross-process notification (API -> Workers) isn't wired up today, so we still have to
	// wake up periodically. Keep the idle tick modest to reduce churn while staying responsive.
	private static readonly TimeSpan MaxIdleDelay = TimeSpan.FromSeconds(10);
	private static readonly TimeSpan ErrorBackoffDelay = TimeSpan.FromSeconds(5);

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		while (!stoppingToken.IsCancellationRequested)
		{
			try
			{
				var processedAny = await ExecuteOnceAsync(stoppingToken).ConfigureAwait(false);
				if (processedAny)
				{
					// Immediately loop to pick up any additional due items.
					continue;
				}

				var delay = await ComputeNextDelayAsync(stoppingToken).ConfigureAwait(false);
				if (delay > TimeSpan.Zero)
				{
					await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
				}
			}
			catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
			{
				return;
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "Worker iteration failed for {WorkerType}", GetType().Name);
				await Task.Delay(ErrorBackoffDelay, stoppingToken).ConfigureAwait(false);
			}
		}
	}

	private async Task<TimeSpan> ComputeNextDelayAsync(CancellationToken stoppingToken)
	{
		using var diScope = scopeFactory.CreateScope();
		var pendingAdds = diScope.ServiceProvider.GetRequiredService<IRadarrPendingAddRepository>();

		var nextReadyAt = await pendingAdds.GetNextReadyAtUtcAsync(stoppingToken).ConfigureAwait(false);
		if (nextReadyAt is null)
		{
			return MaxIdleDelay;
		}

		var now = DateTimeOffset.UtcNow;
		var delay = nextReadyAt.Value - now;
		if (delay <= TimeSpan.Zero)
		{
			return TimeSpan.Zero;
		}

		return delay > MaxIdleDelay ? MaxIdleDelay : delay;
	}

	private async Task<bool> ExecuteOnceAsync(CancellationToken stoppingToken)
	{
		using var diScope = scopeFactory.CreateScope();
		var pendingAdds = diScope.ServiceProvider.GetRequiredService<IRadarrPendingAddRepository>();
		var radarr = diScope.ServiceProvider.GetRequiredService<IRadarrService>();

		var now = DateTimeOffset.UtcNow;
		var due = await pendingAdds.ListDueAsync(now, limit: 50, stoppingToken);
		if (due.Count == 0)
		{
			return false;
		}

		logger.LogInformation("radarr superlike add: processing {Count} due items", due.Count);

		foreach (var item in due)
		{
			stoppingToken.ThrowIfCancellationRequested();

			if (item.ServiceType != ServiceType.Radarr)
			{
				await pendingAdds.RescheduleAsync(
					item.Id,
					nextReadyAtUtc: now.AddHours(1),
					lastError: $"Unexpected service type: {item.ServiceType}",
					stoppingToken);
				continue;
			}

			var scope = new ServiceScope(item.ServiceType, item.ServerId);

			try
			{
				var result = await radarr.AddMovieAsync(scope, item.TmdbId, stoppingToken);
				if (result.Added || result.AlreadyExists)
				{
					logger.LogInformation(
						"radarr superlike add succeeded for {ServerId} tmdb:{TmdbId} (added={Added} exists={Exists})",
						item.ServerId,
						item.TmdbId,
						result.Added,
						result.AlreadyExists);
					await pendingAdds.MarkProcessedAsync(item.Id, DateTimeOffset.UtcNow, stoppingToken);
					continue;
				}

				logger.LogWarning(
					"radarr superlike add failed for {ServerId} tmdb:{TmdbId}: {Message}",
					item.ServerId,
					item.TmdbId,
					result.Message ?? "(no message)");

				await pendingAdds.RescheduleAsync(
					item.Id,
					nextReadyAtUtc: DateTimeOffset.UtcNow.AddMinutes(5),
					lastError: result.Message ?? "Radarr add failed.",
					stoppingToken);
			}
			catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				logger.LogWarning(ex, "Radarr superlike add failed for {ServerId} tmdb:{TmdbId}", item.ServerId, item.TmdbId);
				await pendingAdds.RescheduleAsync(
					item.Id,
					nextReadyAtUtc: DateTimeOffset.UtcNow.AddMinutes(5),
					lastError: ex.Message,
					stoppingToken);
			}
		}

		return true;
	}
}
