using Microsoft.EntityFrameworkCore;
using Tindarr.Application.Abstractions.Persistence;
using Tindarr.Infrastructure.Persistence.Entities;

namespace Tindarr.Infrastructure.Persistence.Repositories;

public sealed class AdvancedSettingsRepository(TindarrDbContext db) : IAdvancedSettingsRepository
{
	public async Task<AdvancedSettingsRecord?> GetAsync(CancellationToken cancellationToken)
	{
		var entity = await db.AdvancedSettings
			.AsNoTracking()
			.FirstOrDefaultAsync(cancellationToken);

		return entity is null ? null : Map(entity);
	}

	public async Task<AdvancedSettingsRecord> UpsertAsync(AdvancedSettingsUpsert upsert, CancellationToken cancellationToken)
	{
		var entity = await db.AdvancedSettings
			.FirstOrDefaultAsync(cancellationToken);

		var now = DateTimeOffset.UtcNow;
		if (entity is null)
		{
			entity = new AdvancedSettingsEntity
			{
				ApiRateLimitEnabled = upsert.ApiRateLimitEnabled,
				ApiRateLimitPermitLimit = upsert.ApiRateLimitPermitLimit,
				ApiRateLimitWindowMinutes = upsert.ApiRateLimitWindowMinutes,
				CleanupEnabled = upsert.CleanupEnabled,
				CleanupIntervalMinutes = upsert.CleanupIntervalMinutes,
				CleanupPurgeGuestUsers = upsert.CleanupPurgeGuestUsers,
				CleanupGuestUserMaxAgeHours = upsert.CleanupGuestUserMaxAgeHours,
				TmdbApiKey = upsert.TmdbApiKey,
				TmdbReadAccessToken = upsert.TmdbReadAccessToken,
				DateTimeDisplayMode = upsert.DateTimeDisplayMode,
				TimeZoneId = upsert.TimeZoneId,
				DateOrder = upsert.DateOrder,
				UpdatedAtUtc = now
			};
			db.AdvancedSettings.Add(entity);
		}
		else
		{
			entity.ApiRateLimitEnabled = upsert.ApiRateLimitEnabled;
			entity.ApiRateLimitPermitLimit = upsert.ApiRateLimitPermitLimit;
			entity.ApiRateLimitWindowMinutes = upsert.ApiRateLimitWindowMinutes;
			entity.CleanupEnabled = upsert.CleanupEnabled;
			entity.CleanupIntervalMinutes = upsert.CleanupIntervalMinutes;
			entity.CleanupPurgeGuestUsers = upsert.CleanupPurgeGuestUsers;
			entity.CleanupGuestUserMaxAgeHours = upsert.CleanupGuestUserMaxAgeHours;
			entity.TmdbApiKey = upsert.TmdbApiKey;
			entity.TmdbReadAccessToken = upsert.TmdbReadAccessToken;
			entity.DateTimeDisplayMode = upsert.DateTimeDisplayMode;
			entity.TimeZoneId = upsert.TimeZoneId;
			entity.DateOrder = upsert.DateOrder;
			entity.UpdatedAtUtc = now;
		}

		await db.SaveChangesAsync(cancellationToken);
		return new AdvancedSettingsRecord(
			entity.ApiRateLimitEnabled,
			entity.ApiRateLimitPermitLimit,
			entity.ApiRateLimitWindowMinutes,
			entity.CleanupEnabled,
			entity.CleanupIntervalMinutes,
			entity.CleanupPurgeGuestUsers,
			entity.CleanupGuestUserMaxAgeHours,
			entity.TmdbApiKey,
			entity.TmdbReadAccessToken,
			entity.DateTimeDisplayMode,
			entity.TimeZoneId,
			entity.DateOrder,
			entity.UpdatedAtUtc);
	}

	private static AdvancedSettingsRecord Map(AdvancedSettingsEntity entity)
	{
		return new AdvancedSettingsRecord(
			entity.ApiRateLimitEnabled,
			entity.ApiRateLimitPermitLimit,
			entity.ApiRateLimitWindowMinutes,
			entity.CleanupEnabled,
			entity.CleanupIntervalMinutes,
			entity.CleanupPurgeGuestUsers,
			entity.CleanupGuestUserMaxAgeHours,
			entity.TmdbApiKey,
			entity.TmdbReadAccessToken,
			entity.DateTimeDisplayMode,
			entity.TimeZoneId,
			entity.DateOrder,
			entity.UpdatedAtUtc);
	}
}
