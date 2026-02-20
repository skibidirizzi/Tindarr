using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tindarr.Application.Abstractions.Ops;
using Tindarr.Application.Interfaces.Rooms;

namespace Tindarr.Api.Hosting;

/// <summary>
/// Periodically cleans expired room state and room interaction data.
/// Satisfies INV-0012: room cleanup MUST be automated (worker or hosted service).
/// Uses effective cleanup options (DB overrides + config) so admin UI changes apply.
/// </summary>
public sealed class RoomCleanupHostedService(
	IRoomStore roomStore,
	IRoomInteractionStore roomInteractionStore,
	IEffectiveAdvancedSettings effectiveSettings,
	ILogger<RoomCleanupHostedService> logger) : BackgroundService
{
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		while (!stoppingToken.IsCancellationRequested)
		{
			var opts = effectiveSettings.GetCleanupOptions();
			if (opts.Enabled)
			{
				try
				{
					await roomStore.CleanupExpiredAsync(stoppingToken).ConfigureAwait(false);
					await roomInteractionStore.CleanupExpiredAsync(stoppingToken).ConfigureAwait(false);
					logger.LogDebug("Room cleanup cycle completed");
				}
				catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
				{
					return;
				}
				catch (Exception ex)
				{
					logger.LogError(ex, "Room cleanup cycle failed");
				}
			}
			else
			{
				logger.LogDebug("Room cleanup hosted service is disabled");
			}

			await Task.Delay(opts.Interval, stoppingToken).ConfigureAwait(false);
		}
	}
}
