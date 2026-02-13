using Tindarr.Application.Abstractions.Persistence;
using Tindarr.Domain.Common;

namespace Tindarr.Application.Interfaces.Integrations;

public interface IJellyfinService
{
	Task<IReadOnlyList<JellyfinServerRecord>> ListServersAsync(CancellationToken cancellationToken);

	Task<ServiceSettingsRecord?> GetSettingsAsync(ServiceScope scope, CancellationToken cancellationToken);

	Task<ServiceSettingsRecord> UpsertSettingsAsync(JellyfinSettingsUpsert upsert, bool confirmNewInstance, CancellationToken cancellationToken);

	Task<JellyfinConnectionTestResult> TestConnectionAsync(ServiceScope scope, CancellationToken cancellationToken);

	Task<JellyfinLibrarySyncResult> SyncLibraryAsync(ServiceScope scope, CancellationToken cancellationToken);

	Task<JellyfinLibrarySyncResult?> EnsureLibrarySyncAsync(ServiceScope scope, CancellationToken cancellationToken);
}

public sealed record JellyfinSettingsUpsert(string BaseUrl, string ApiKey);

public sealed record JellyfinServerRecord(
	string ServerId,
	string Name,
	string? BaseUrl,
	string? Version,
	DateTimeOffset? LastLibrarySyncUtc,
	DateTimeOffset UpdatedAtUtc);

public sealed record JellyfinLibrarySyncResult(int Count, DateTimeOffset SyncedAtUtc);

public sealed record JellyfinConnectionTestResult(bool Ok, string? Message);
