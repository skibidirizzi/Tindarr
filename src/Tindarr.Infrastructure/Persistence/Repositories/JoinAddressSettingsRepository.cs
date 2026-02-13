using Microsoft.EntityFrameworkCore;
using Tindarr.Application.Abstractions.Persistence;
using Tindarr.Infrastructure.Persistence.Entities;

namespace Tindarr.Infrastructure.Persistence.Repositories;

public sealed class JoinAddressSettingsRepository(TindarrDbContext db) : IJoinAddressSettingsRepository
{
	public async Task<JoinAddressSettingsRecord?> GetAsync(CancellationToken cancellationToken)
	{
		var entity = await db.JoinAddressSettings
			.AsNoTracking()
			.SingleOrDefaultAsync(cancellationToken);

		return entity is null ? null : Map(entity);
	}

	public async Task<JoinAddressSettingsRecord> UpsertAsync(JoinAddressSettingsUpsert upsert, CancellationToken cancellationToken)
	{
		var entity = await db.JoinAddressSettings
			.SingleOrDefaultAsync(cancellationToken);

		var now = DateTimeOffset.UtcNow;
		if (entity is null)
		{
			entity = new JoinAddressSettingsEntity
			{
				LanHostPort = upsert.LanHostPort,
				WanHostPort = upsert.WanHostPort,
				UpdatedAtUtc = now
			};
			db.JoinAddressSettings.Add(entity);
		}
		else
		{
			entity.LanHostPort = upsert.LanHostPort;
			entity.WanHostPort = upsert.WanHostPort;
			entity.UpdatedAtUtc = now;
		}

		await db.SaveChangesAsync(cancellationToken);
		return new JoinAddressSettingsRecord(entity.LanHostPort, entity.WanHostPort, entity.UpdatedAtUtc);
	}

	private static JoinAddressSettingsRecord Map(JoinAddressSettingsEntity entity)
	{
		return new JoinAddressSettingsRecord(entity.LanHostPort, entity.WanHostPort, entity.UpdatedAtUtc);
	}
}
