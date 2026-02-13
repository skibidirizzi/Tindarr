using Tindarr.Application.Abstractions.Persistence;
using Tindarr.Domain.Common;

namespace Tindarr.Application.Interfaces.Integrations;

public interface IEmbyService
{
	Task<IReadOnlyList<EmbyServerRecord>> ListServersAsync(CancellationToken cancellationToken);

	Task<ServiceSettingsRecord?> GetSettingsAsync(ServiceScope scope, CancellationToken cancellationToken);

	Task<ServiceSettingsRecord> UpsertSettingsAsync(EmbySettingsUpsert upsert, bool confirmNewInstance, CancellationToken cancellationToken);

	Task<EmbyConnectionTestResult> TestConnectionAsync(ServiceScope scope, CancellationToken cancellationToken);

	Task<EmbyLibrarySyncResult> SyncLibraryAsync(ServiceScope scope, CancellationToken cancellationToken);

	Task<EmbyLibrarySyncResult?> EnsureLibrarySyncAsync(ServiceScope scope, CancellationToken cancellationToken);
}

public sealed record EmbySettingsUpsert(string BaseUrl, string ApiKey);

public sealed record EmbyServerRecord(
	string ServerId,
	string Name,
	string? BaseUrl,
	string? Version,
	DateTimeOffset? LastLibrarySyncUtc,
	DateTimeOffset UpdatedAtUtc);

public sealed record EmbyLibrarySyncResult(int Count, DateTimeOffset SyncedAtUtc);

public sealed record EmbyConnectionTestResult(bool Ok, string? Message);
