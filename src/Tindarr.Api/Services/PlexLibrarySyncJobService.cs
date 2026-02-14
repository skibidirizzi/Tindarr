using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Tindarr.Application.Interfaces.Integrations;
using Tindarr.Domain.Common;

namespace Tindarr.Api.Services;

public enum PlexLibrarySyncJobState
{
	Idle = 0,
	Running = 1,
	Completed = 2,
	Failed = 3
}

public sealed record PlexLibrarySyncJobStatus(
	string ServiceType,
	string ServerId,
	PlexLibrarySyncJobState State,
	int TotalSections,
	int ProcessedSections,
	int TotalItems,
	int ProcessedItems,
	int TmdbIdsFound,
	DateTimeOffset? StartedAtUtc,
	DateTimeOffset? FinishedAtUtc,
	string? Message,
	DateTimeOffset UpdatedAtUtc);

public interface IPlexLibrarySyncJobService
{
	PlexLibrarySyncJobStatus GetStatus(ServiceScope scope);
	Task<PlexLibrarySyncJobStatus> StartAsync(ServiceScope scope, CancellationToken cancellationToken);
}

public sealed class PlexLibrarySyncJobService(IServiceScopeFactory scopeFactory) : IPlexLibrarySyncJobService
{
	private sealed class JobEntry
	{
		public readonly SemaphoreSlim Gate = new(1, 1);
		public PlexLibrarySyncJobStatus Status = new(
			ServiceType: "plex",
			ServerId: "default",
			State: PlexLibrarySyncJobState.Idle,
			TotalSections: 0,
			ProcessedSections: 0,
			TotalItems: 0,
			ProcessedItems: 0,
			TmdbIdsFound: 0,
			StartedAtUtc: null,
			FinishedAtUtc: null,
			Message: null,
			UpdatedAtUtc: DateTimeOffset.UtcNow);
	}

	private readonly ConcurrentDictionary<string, JobEntry> _jobs = new(StringComparer.OrdinalIgnoreCase);

	private static string Key(ServiceScope scope) => $"{scope.ServiceType}:{scope.ServerId}";

	public PlexLibrarySyncJobStatus GetStatus(ServiceScope scope)
	{
		var entry = _jobs.GetOrAdd(Key(scope), _ => new JobEntry());
		return entry.Status;
	}

	public async Task<PlexLibrarySyncJobStatus> StartAsync(ServiceScope scope, CancellationToken cancellationToken)
	{
		var entry = _jobs.GetOrAdd(Key(scope), _ => new JobEntry());
		await entry.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			if (entry.Status.State == PlexLibrarySyncJobState.Running)
			{
				return entry.Status;
			}

			var startedAt = DateTimeOffset.UtcNow;
			entry.Status = entry.Status with
			{
				ServiceType = scope.ServiceType.ToString().ToLowerInvariant(),
				ServerId = scope.ServerId,
				State = PlexLibrarySyncJobState.Running,
				TotalSections = 0,
				ProcessedSections = 0,
				TotalItems = 0,
				ProcessedItems = 0,
				TmdbIdsFound = 0,
				StartedAtUtc = startedAt,
				FinishedAtUtc = null,
				Message = null,
				UpdatedAtUtc = startedAt
			};

			_ = Task.Run(async () =>
			{
				var progress = new Progress<PlexLibrarySyncProgress>(p =>
				{
					entry.Status = entry.Status with
					{
						TotalSections = p.TotalSections,
						ProcessedSections = p.ProcessedSections,
						TotalItems = p.TotalItems,
						ProcessedItems = p.ProcessedItems,
						TmdbIdsFound = p.TmdbIdsFound,
						Message = string.IsNullOrWhiteSpace(p.CurrentSectionTitle) ? "Syncing…" : $"Syncing: {p.CurrentSectionTitle}",
						UpdatedAtUtc = DateTimeOffset.UtcNow
					};
				});

				try
				{
					using var serviceScope = scopeFactory.CreateScope();
					var plexService = serviceScope.ServiceProvider.GetRequiredService<IPlexService>();
					await plexService.SyncLibraryWithProgressAsync(scope, progress, CancellationToken.None).ConfigureAwait(false);
					var finishedAt = DateTimeOffset.UtcNow;
					entry.Status = entry.Status with
					{
						State = PlexLibrarySyncJobState.Completed,
						FinishedAtUtc = finishedAt,
						Message = "Sync complete.",
						UpdatedAtUtc = finishedAt
					};
				}
				catch (Exception ex)
				{
					var finishedAt = DateTimeOffset.UtcNow;
					entry.Status = entry.Status with
					{
						State = PlexLibrarySyncJobState.Failed,
						FinishedAtUtc = finishedAt,
						Message = ex.Message,
						UpdatedAtUtc = finishedAt
					};
				}
			});

			return entry.Status;
		}
		finally
		{
			entry.Gate.Release();
		}
	}
}
