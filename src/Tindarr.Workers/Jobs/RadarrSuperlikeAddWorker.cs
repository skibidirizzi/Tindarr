using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Tindarr.Application.Abstractions.Persistence;
using Tindarr.Application.Interfaces.Integrations;
using Tindarr.Domain.Common;

namespace Tindarr.Workers.Jobs;

public sealed class RadarrSuperlikeAddWorker(
	IServiceScopeFactory scopeFactory,
	ILogger<RadarrSuperlikeAddWorker> logger) : PeriodicBackgroundService(logger)
{
	protected override TimeSpan Interval => TimeSpan.FromSeconds(1);

	protected override async Task ExecuteOnceAsync(CancellationToken stoppingToken)
	{
		using var diScope = scopeFactory.CreateScope();
		var pendingAdds = diScope.ServiceProvider.GetRequiredService<IRadarrPendingAddRepository>();
		var radarr = diScope.ServiceProvider.GetRequiredService<IRadarrService>();

		var now = DateTimeOffset.UtcNow;
		var due = await pendingAdds.ListDueAsync(now, limit: 50, stoppingToken);
		if (due.Count == 0)
		{
			return;
		}

		Logger.LogInformation("radarr superlike add: processing {Count} due items", due.Count);

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
					Logger.LogInformation(
						"radarr superlike add succeeded for {ServerId} tmdb:{TmdbId} (added={Added} exists={Exists})",
						item.ServerId,
						item.TmdbId,
						result.Added,
						result.AlreadyExists);
					await pendingAdds.MarkProcessedAsync(item.Id, DateTimeOffset.UtcNow, stoppingToken);
					continue;
				}

				Logger.LogWarning(
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
	}
}
