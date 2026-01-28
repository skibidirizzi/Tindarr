using Tindarr.Domain.Common;

namespace Tindarr.Application.Abstractions.Persistence;

public interface IServiceSettingsRepository
{
	Task<ServiceSettingsRecord?> GetAsync(ServiceScope scope, CancellationToken cancellationToken);

	Task<IReadOnlyList<ServiceSettingsRecord>> ListAsync(ServiceType serviceType, CancellationToken cancellationToken);

	Task UpsertAsync(ServiceScope scope, ServiceSettingsUpsert upsert, CancellationToken cancellationToken);
}

public sealed record ServiceSettingsRecord(
	ServiceType ServiceType,
	string ServerId,
	string RadarrBaseUrl,
	string RadarrApiKey,
	int? RadarrQualityProfileId,
	string? RadarrRootFolderPath,
	string? RadarrTagLabel,
	int? RadarrTagId,
	bool RadarrAutoAddEnabled,
	long? RadarrLastAutoAddAcceptedId,
	DateTimeOffset? RadarrLastLibrarySyncUtc,
	DateTimeOffset UpdatedAtUtc);

public sealed record ServiceSettingsUpsert(
	string RadarrBaseUrl,
	string RadarrApiKey,
	int? RadarrQualityProfileId,
	string? RadarrRootFolderPath,
	string? RadarrTagLabel,
	int? RadarrTagId,
	bool RadarrAutoAddEnabled,
	long? RadarrLastAutoAddAcceptedId,
	DateTimeOffset? RadarrLastLibrarySyncUtc);
