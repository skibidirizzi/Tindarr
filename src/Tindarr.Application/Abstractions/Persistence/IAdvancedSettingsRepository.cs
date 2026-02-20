namespace Tindarr.Application.Abstractions.Persistence;

public interface IAdvancedSettingsRepository
{
	Task<AdvancedSettingsRecord?> GetAsync(CancellationToken cancellationToken);

	Task<AdvancedSettingsRecord> UpsertAsync(AdvancedSettingsUpsert upsert, CancellationToken cancellationToken);
}

public sealed record AdvancedSettingsRecord(
	bool? ApiRateLimitEnabled,
	int? ApiRateLimitPermitLimit,
	int? ApiRateLimitWindowMinutes,
	bool? CleanupEnabled,
	int? CleanupIntervalMinutes,
	bool? CleanupPurgeGuestUsers,
	int? CleanupGuestUserMaxAgeHours,
	string? TmdbApiKey,
	string? DateTimeDisplayMode,
	string? TimeZoneId,
	string? DateOrder,
	DateTimeOffset UpdatedAtUtc);

public sealed record AdvancedSettingsUpsert(
	bool? ApiRateLimitEnabled,
	int? ApiRateLimitPermitLimit,
	int? ApiRateLimitWindowMinutes,
	bool? CleanupEnabled,
	int? CleanupIntervalMinutes,
	bool? CleanupPurgeGuestUsers,
	int? CleanupGuestUserMaxAgeHours,
	string? TmdbApiKey,
	string? DateTimeDisplayMode,
	string? TimeZoneId,
	string? DateOrder);
