using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tindarr.Application.Abstractions.Domain;
using Tindarr.Application.Abstractions.Persistence;
using Tindarr.Application.Interfaces.Interactions;
using Tindarr.Domain.Common;

namespace Tindarr.Workers.Jobs;

/// <summary>
/// Processes swipe events and computes match state; emits match-found events.
/// Computes TMDB-scope matches and queues Radarr adds.
/// </summary>
public sealed class MatchComputationWorker(
	IServiceScopeFactory scopeFactory,
	ILogger<MatchComputationWorker> logger) : PeriodicBackgroundService(logger)
{
	private const string MatchSystemUserId = "match";

	protected override TimeSpan Interval => TimeSpan.FromSeconds(30);

	protected override TimeSpan InitialDelay => TimeSpan.FromSeconds(10);

	protected override async Task ExecuteOnceAsync(CancellationToken stoppingToken)
	{
		using var diScope = scopeFactory.CreateScope();
		var interactionStore = diScope.ServiceProvider.GetRequiredService<IInteractionStore>();
		var matchingEngine = diScope.ServiceProvider.GetRequiredService<IMatchingEngine>();
		var settingsRepo = diScope.ServiceProvider.GetRequiredService<IServiceSettingsRepository>();
		var pendingAdds = diScope.ServiceProvider.GetRequiredService<IRadarrPendingAddRepository>();

		var radarrScope = await TryResolveDefaultRadarrScopeAsync(settingsRepo, stoppingToken).ConfigureAwait(false);
		if (radarrScope is null)
		{
			return;
		}

		// Use configured TMDB scopes if present; otherwise default to tmdb/tmdb.
		var tmdbSettings = await settingsRepo.ListAsync(ServiceType.Tmdb, stoppingToken).ConfigureAwait(false);
		var tmdbScopes = tmdbSettings.Count > 0
			? tmdbSettings.Select(x => new ServiceScope(ServiceType.Tmdb, x.ServerId)).ToList()
			: [new ServiceScope(ServiceType.Tmdb, "tmdb")];

		var now = DateTimeOffset.UtcNow;
		foreach (var tmdbScope in tmdbScopes)
		{
			stoppingToken.ThrowIfCancellationRequested();

			var effectiveMinUsers = 2;
			int? effectiveMinUserPercent = null;

			var settings = await settingsRepo.GetAsync(tmdbScope, stoppingToken).ConfigureAwait(false);
			if (settings is not null)
			{
				if (settings.MatchMinUsers is not null)
				{
					effectiveMinUsers = Math.Clamp(settings.MatchMinUsers.Value, 0, 50);
				}
				else if (settings.MatchMinUserPercent is not null)
				{
					// If only percent is configured, disable count threshold.
					effectiveMinUsers = 0;
				}

				effectiveMinUserPercent = settings.MatchMinUserPercent;
			}

			var interactions = await interactionStore.ListForScopeAsync(tmdbScope, tmdbId: null, limit: 50_000, stoppingToken).ConfigureAwait(false);
			if (interactions.Count == 0)
			{
				continue;
			}

			var matches = matchingEngine.ComputeLikedByAllMatches(tmdbScope, interactions, effectiveMinUsers, effectiveMinUserPercent);
			if (matches.Count == 0)
			{
				continue;
			}

			var enqueued = 0;
			foreach (var tmdbId in matches)
			{
				stoppingToken.ThrowIfCancellationRequested();
				var created = await pendingAdds.TryEnqueueAsync(radarrScope, MatchSystemUserId, tmdbId, readyAtUtc: now.AddSeconds(15), stoppingToken).ConfigureAwait(false);
				if (created)
				{
					enqueued++;
				}
			}

			Logger.LogInformation(
				"match computation: scope {ServiceType}/{ServerId} matches={Matches} enqueued={Enqueued} (radarr={RadarrServerId})",
				tmdbScope.ServiceType,
				tmdbScope.ServerId,
				matches.Count,
				enqueued,
				radarrScope.ServerId);
		}
	}

	private static async Task<ServiceScope?> TryResolveDefaultRadarrScopeAsync(
		IServiceSettingsRepository settingsRepo,
		CancellationToken cancellationToken)
	{
		var rows = await settingsRepo.ListAsync(ServiceType.Radarr, cancellationToken).ConfigureAwait(false);
		var configured = rows
			.Where(x => !string.IsNullOrWhiteSpace(x.ServerId))
			.Where(x => !string.IsNullOrWhiteSpace(x.RadarrBaseUrl))
			.Where(x => !string.IsNullOrWhiteSpace(x.RadarrApiKey))
			.ToList();

		if (configured.Count == 0)
		{
			return null;
		}

		var preferred = configured.FirstOrDefault(x => string.Equals(x.ServerId, "default", StringComparison.OrdinalIgnoreCase))
			?? configured.First();

		return new ServiceScope(ServiceType.Radarr, preferred.ServerId);
	}
}

