using Tindarr.Application.Abstractions.Integrations;
using Tindarr.Application.Abstractions.Persistence;
using Tindarr.Domain.Common;

namespace Tindarr.Application.Interfaces.Integrations;

public interface IRadarrService
{
	Task<ServiceSettingsRecord?> GetSettingsAsync(ServiceScope scope, CancellationToken cancellationToken);

	Task<ServiceSettingsRecord> UpsertSettingsAsync(ServiceScope scope, RadarrSettingsUpsert upsert, CancellationToken cancellationToken);

	Task<RadarrConnectionTestResult> TestConnectionAsync(ServiceScope scope, CancellationToken cancellationToken);

	Task<IReadOnlyList<RadarrQualityProfile>> GetQualityProfilesAsync(ServiceScope scope, CancellationToken cancellationToken);

	Task<IReadOnlyList<RadarrRootFolder>> GetRootFoldersAsync(ServiceScope scope, CancellationToken cancellationToken);

	Task<RadarrLibrarySyncResult> SyncLibraryAsync(ServiceScope scope, CancellationToken cancellationToken);

	Task<RadarrLibrarySyncResult?> EnsureLibrarySyncAsync(ServiceScope scope, CancellationToken cancellationToken);

	Task<RadarrAutoAddResult> AutoAddAcceptedMoviesAsync(ServiceScope scope, CancellationToken cancellationToken);

	Task<RadarrAddMovieResult> AddMovieAsync(ServiceScope scope, int tmdbId, CancellationToken cancellationToken);
}

public sealed record RadarrSettingsUpsert(
	string BaseUrl,
	string? ApiKey,
	int? QualityProfileId,
	string? RootFolderPath,
	string? TagLabel,
	bool AutoAddEnabled);

public sealed record RadarrLibrarySyncResult(int Count, DateTimeOffset SyncedAtUtc);

public sealed record RadarrAutoAddResult(
	int Attempted,
	int Added,
	int SkippedExisting,
	int Failed,
	long? LastAcceptedId,
	string? Message);
