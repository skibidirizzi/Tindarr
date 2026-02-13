using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tindarr.Application.Abstractions.Persistence;
using Tindarr.Application.Options;

namespace Tindarr.Workers.Jobs;

/// <summary>
/// Expires inactive rooms and temporary guest users; purges stale caches.
/// Runs as part of the Workers host.
/// </summary>
public sealed class CleanupWorker(
	IServiceScopeFactory scopeFactory,
	IOptions<CleanupOptions> options,
	ILogger<CleanupWorker> logger) : PeriodicBackgroundService(logger)
{
	private readonly CleanupOptions cleanup = options.Value;

	protected override TimeSpan Interval => cleanup.Interval;

	protected override async Task ExecuteOnceAsync(CancellationToken stoppingToken)
	{
		if (!cleanup.Enabled)
		{
			Logger.LogDebug("Cleanup worker is disabled");
			return;
		}

		using var scope = scopeFactory.CreateScope();
		var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();

		if (cleanup.PurgeGuestUsers)
		{
			var cutoff = DateTimeOffset.UtcNow - cleanup.GuestUserMaxAge;
			var deleted = await users.PurgeGuestUsersAsync(cutoff, stoppingToken).ConfigureAwait(false);
			if (deleted > 0)
			{
				Logger.LogInformation("Purged {Deleted} guest users created before {CutoffUtc}", deleted, cutoff);
			}
			else
			{
				Logger.LogDebug("No guest users to purge");
			}
		}
	}
}

