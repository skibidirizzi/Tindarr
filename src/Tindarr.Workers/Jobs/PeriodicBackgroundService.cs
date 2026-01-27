using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Tindarr.Workers.Jobs;

/// <summary>
/// Minimal, core-only periodic worker base class.
/// Intended to be extended with real domain/application logic later.
/// </summary>
public abstract class PeriodicBackgroundService(ILogger logger) : BackgroundService
{
	protected ILogger Logger { get; } = logger;

	protected abstract TimeSpan Interval { get; }

	protected virtual TimeSpan InitialDelay => TimeSpan.Zero;

	protected abstract Task ExecuteOnceAsync(CancellationToken stoppingToken);

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		if (InitialDelay > TimeSpan.Zero)
		{
			await Task.Delay(InitialDelay, stoppingToken);
		}

		using var timer = new PeriodicTimer(Interval);

		while (await timer.WaitForNextTickAsync(stoppingToken))
		{
			try
			{
				await ExecuteOnceAsync(stoppingToken);
			}
			catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
			{
				return;
			}
			catch (Exception ex)
			{
				Logger.LogError(ex, "Worker iteration failed for {WorkerType}", GetType().Name);
			}
		}
	}
}

