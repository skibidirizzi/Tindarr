using Tindarr.Application.Abstractions.Persistence;
using Tindarr.Contracts.Movies;
using Tindarr.Domain.Common;

namespace Tindarr.Application.Interfaces.Integrations;

public interface IPlexService
{
	Task<PlexPinCreateResult> CreatePinAsync(CancellationToken cancellationToken);

	Task<PlexPinStatusResult> VerifyPinAsync(long pinId, CancellationToken cancellationToken);

	Task<PlexAuthStatusResult> GetAuthStatusAsync(CancellationToken cancellationToken);

	Task<IReadOnlyList<PlexServerRecord>> RefreshServersAsync(CancellationToken cancellationToken);

	Task<IReadOnlyList<PlexServerRecord>> ListServersAsync(CancellationToken cancellationToken);

	Task<PlexLibrarySyncResult> SyncLibraryAsync(ServiceScope scope, CancellationToken cancellationToken);

	Task<PlexLibrarySyncResult> SyncLibraryWithProgressAsync(
		ServiceScope scope,
		IProgress<PlexLibrarySyncProgress> progress,
		CancellationToken cancellationToken);

	Task<PlexLibrarySyncResult?> EnsureLibrarySyncAsync(ServiceScope scope, CancellationToken cancellationToken);

	Task<IReadOnlyList<MovieDetailsDto>> GetLibraryAsync(
		ServiceScope scope,
		UserPreferencesRecord preferences,
		int limit,
		CancellationToken cancellationToken);

	Task<IReadOnlyList<MovieDetailsDto>> GetCachedLibraryAsync(
		ServiceScope scope,
		UserPreferencesRecord preferences,
		int limit,
		CancellationToken cancellationToken);
}

public sealed record PlexLibrarySyncProgress(
	int TotalSections,
	int ProcessedSections,
	int TotalItems,
	int ProcessedItems,
	int TmdbIdsFound,
	string? CurrentSectionTitle);

public sealed record PlexPinCreateResult(
	long PinId,
	string Code,
	DateTimeOffset? ExpiresAtUtc,
	string AuthUrl);

public sealed record PlexPinStatusResult(
	long PinId,
	string Code,
	DateTimeOffset? ExpiresAtUtc,
	bool Authorized);

public sealed record PlexAuthStatusResult(bool HasClientIdentifier, bool HasAuthToken);

public sealed record PlexServerRecord(
	string ServerId,
	string Name,
	string? BaseUrl,
	string? Version,
	string? Platform,
	bool? Owned,
	bool? Online,
	DateTimeOffset? LastLibrarySyncUtc,
	DateTimeOffset UpdatedAtUtc);

public sealed record PlexLibrarySyncResult(int Count, DateTimeOffset SyncedAtUtc);
