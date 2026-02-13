using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tindarr.Application.Abstractions.Caching;
using Tindarr.Application.Abstractions.Integrations;
using Tindarr.Application.Abstractions.Persistence;
using Tindarr.Application.Interfaces.Preferences;
using Tindarr.Application.Options;
using Tindarr.Contracts.Tmdb;
using Tindarr.Infrastructure.Integrations.Tmdb.Http;

namespace Tindarr.Api.Services;

public sealed class TmdbBuildJob(
	IServiceScopeFactory scopeFactory,
	ILogger<TmdbBuildJob> logger) : ITmdbBuildJob
{
	private readonly object _gate = new();
	private CancellationTokenSource? _cts;
	private Task? _running;
	private Status _status = Status.Idle;

	public TmdbBuildStatusDto GetStatus()
	{
		lock (_gate)
		{
			return _status.ToDto();
		}
	}

	public bool TryStart(StartTmdbBuildRequest request)
	{
		lock (_gate)
		{
			if (_running is { IsCompleted: false })
			{
				return false;
			}

			_cts?.Dispose();
			_cts = new CancellationTokenSource();
			_status = Status.Running(rateLimitOverride: request.RateLimitOverride);
			_running = Task.Run(() => RunAsync(request, _cts.Token));
			return true;
		}
	}

	public bool TryCancel(string reason)
	{
		lock (_gate)
		{
			if (_cts is null || _running is null || _running.IsCompleted)
			{
				return false;
			}

			_status = _status with { LastMessage = string.IsNullOrWhiteSpace(reason) ? "Cancel requested" : reason };
			_cts.Cancel();
			return true;
		}
	}

	private void Update(Func<Status, Status> mutate)
	{
		lock (_gate)
		{
			_status = mutate(_status);
		}
	}

	private async Task RunAsync(StartTmdbBuildRequest request, CancellationToken stoppingToken)
	{
		try
		{
			using var scope = scopeFactory.CreateScope();
			var tmdb = scope.ServiceProvider.GetRequiredService<IOptions<TmdbOptions>>().Value;
			if (!tmdb.HasCredentials)
			{
				Update(s => s.Fail("TMDB is not configured (missing credentials)."));
				return;
			}

			var usersBatchSize = Math.Clamp(request.UsersBatchSize, 1, 200);
			var discoverLimit = Math.Clamp(request.DiscoverLimitPerUser, 1, 1000);

			var userRepo = scope.ServiceProvider.GetRequiredService<IUserRepository>();
			var prefsService = scope.ServiceProvider.GetRequiredService<IUserPreferencesService>();
			var tmdbClient = scope.ServiceProvider.GetRequiredService<ITmdbClient>();
			var metadataStore = scope.ServiceProvider.GetRequiredService<ITmdbMetadataStore>();
			var imageCache = scope.ServiceProvider.GetRequiredService<ITmdbImageCache>();

			var allUsers = await userRepo.ListAsync(0, 10_000, stoppingToken).ConfigureAwait(false);
			Update(s => s with { UsersTotal = allUsers.Count, LastMessage = "Starting" });

			var settings = await metadataStore.GetSettingsAsync(stoppingToken).ConfigureAwait(false);
			var shouldPrefetchImages = request.PrefetchImages
				&& settings.PosterMode == TmdbPosterMode.LocalProxy
				&& settings.ImageCacheMaxMb > 0;
			var maxImageBytes = (long)Math.Max(0, settings.ImageCacheMaxMb) * 1024L * 1024L;

			var bypass = request.RateLimitOverride;
			var previousBypass = TmdbRateLimitingHandler.BypassRateLimit.Value;
			TmdbRateLimitingHandler.BypassRateLimit.Value = bypass;
			try
			{
				var processed = 0;
				foreach (var batch in allUsers.Chunk(usersBatchSize))
				{
					foreach (var user in batch)
					{
						stoppingToken.ThrowIfCancellationRequested();
						Update(s => s with { CurrentUserId = user.Id, LastMessage = "Discovering" });

						var prefs = await prefsService.GetOrDefaultAsync(user.Id, stoppingToken).ConfigureAwait(false);
						var discovered = await tmdbClient.DiscoverMoviesAsync(prefs, page: 1, limit: discoverLimit, stoppingToken).ConfigureAwait(false);
						await metadataStore.AddToUserPoolAsync(user.Id, discovered, stoppingToken).ConfigureAwait(false);

						Update(s => s with { MoviesDiscovered = s.MoviesDiscovered + discovered.Count });

						if (shouldPrefetchImages)
						{
							Update(s => s with { LastMessage = "Prefetching images" });
							foreach (var m in discovered.Take(12))
							{
								stoppingToken.ThrowIfCancellationRequested();
								var fetchedAny = false;
								if (!string.IsNullOrWhiteSpace(m.PosterPath))
								{
									var r = await imageCache.GetOrFetchAsync(tmdb.PosterSize, m.PosterPath!, stoppingToken).ConfigureAwait(false);
									fetchedAny |= r is not null;
								}
								if (!string.IsNullOrWhiteSpace(m.BackdropPath))
								{
									var r = await imageCache.GetOrFetchAsync(tmdb.BackdropSize, m.BackdropPath!, stoppingToken).ConfigureAwait(false);
									fetchedAny |= r is not null;
								}
								if (fetchedAny)
								{
									Update(s => s with { ImagesFetched = s.ImagesFetched + 1 });
								}
							}

							await imageCache.PruneAsync(maxImageBytes, stoppingToken).ConfigureAwait(false);
						}

						processed++;
						Update(s => s with { UsersProcessed = processed, LastMessage = "User complete" });
					}
				}
			}
			finally
			{
				TmdbRateLimitingHandler.BypassRateLimit.Value = previousBypass;
			}

			// Backfill a bit of details at the end (genres/etc).
			Update(s => s with { LastMessage = "Backfilling details" });
			var ids = await metadataStore.ListMoviesNeedingDetailsAsync(50, stoppingToken).ConfigureAwait(false);
			foreach (var id in ids)
			{
				stoppingToken.ThrowIfCancellationRequested();
				var details = await tmdbClient.GetMovieDetailsAsync(id, stoppingToken).ConfigureAwait(false);
				if (details is null)
				{
					continue;
				}
				await metadataStore.UpdateMovieDetailsAsync(details, stoppingToken).ConfigureAwait(false);
				Update(s => s with { DetailsFetched = s.DetailsFetched + 1 });
			}

			Update(s => s.Complete("Done"));
		}
		catch (OperationCanceledException)
		{
			Update(s => s.Cancel("Canceled"));
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "TMDB build job failed");
			Update(s => s.Fail(ex.Message));
		}
	}

	private sealed record Status(
		string State,
		DateTimeOffset? StartedAtUtc,
		DateTimeOffset? FinishedAtUtc,
		bool RateLimitOverride,
		string? CurrentUserId,
		int UsersProcessed,
		int UsersTotal,
		int MoviesDiscovered,
		int DetailsFetched,
		int ImagesFetched,
		string? LastMessage,
		string? LastError)
	{
		public static Status Idle => new(
			State: "idle",
			StartedAtUtc: null,
			FinishedAtUtc: null,
			RateLimitOverride: false,
			CurrentUserId: null,
			UsersProcessed: 0,
			UsersTotal: 0,
			MoviesDiscovered: 0,
			DetailsFetched: 0,
			ImagesFetched: 0,
			LastMessage: null,
			LastError: null);

		public static Status Running(bool rateLimitOverride) => Idle with
		{
			State = "running",
			StartedAtUtc = DateTimeOffset.UtcNow,
			RateLimitOverride = rateLimitOverride,
			LastMessage = "Starting"
		};

		public Status Complete(string msg) => this with
		{
			State = "completed",
			FinishedAtUtc = DateTimeOffset.UtcNow,
			LastMessage = msg
		};

		public Status Cancel(string msg) => this with
		{
			State = "canceled",
			FinishedAtUtc = DateTimeOffset.UtcNow,
			LastMessage = msg
		};

		public Status Fail(string err) => this with
		{
			State = "failed",
			FinishedAtUtc = DateTimeOffset.UtcNow,
			LastError = err,
			LastMessage = "Failed"
		};

		public TmdbBuildStatusDto ToDto() => new(
			State,
			StartedAtUtc,
			FinishedAtUtc,
			RateLimitOverride,
			CurrentUserId,
			UsersProcessed,
			UsersTotal,
			MoviesDiscovered,
			DetailsFetched,
			ImagesFetched,
			LastMessage,
			LastError);
	}
}
