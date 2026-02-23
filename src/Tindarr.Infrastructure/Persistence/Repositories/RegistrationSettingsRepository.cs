using Microsoft.EntityFrameworkCore;
using Tindarr.Application.Abstractions.Persistence;
using Tindarr.Infrastructure.Persistence.Entities;

namespace Tindarr.Infrastructure.Persistence.Repositories;

public sealed class RegistrationSettingsRepository(TindarrDbContext db) : IRegistrationSettingsRepository
{
	public async Task<RegistrationSettingsRecord?> GetAsync(CancellationToken cancellationToken)
	{
		var entity = await db.RegistrationSettings
			.AsNoTracking()
			.FirstOrDefaultAsync(cancellationToken);

		return entity is null ? null : Map(entity);
	}

	public async Task<RegistrationSettingsRecord> UpsertAsync(RegistrationSettingsUpsert upsert, CancellationToken cancellationToken)
	{
		var entity = await db.RegistrationSettings
			.FirstOrDefaultAsync(cancellationToken);

		var now = DateTimeOffset.UtcNow;
		if (entity is null)
		{
			entity = new RegistrationSettingsEntity
			{
				AllowOpenRegistration = upsert.AllowOpenRegistration,
				RequireAdminApprovalForNewUsers = upsert.RequireAdminApprovalForNewUsers,
				DefaultRole = upsert.DefaultRole,
				UpdatedAtUtc = now
			};
			db.RegistrationSettings.Add(entity);
		}
		else
		{
			entity.AllowOpenRegistration = upsert.AllowOpenRegistration;
			entity.RequireAdminApprovalForNewUsers = upsert.RequireAdminApprovalForNewUsers;
			entity.DefaultRole = upsert.DefaultRole;
			entity.UpdatedAtUtc = now;
		}

		await db.SaveChangesAsync(cancellationToken);
		return new RegistrationSettingsRecord(
			entity.AllowOpenRegistration,
			entity.RequireAdminApprovalForNewUsers,
			entity.DefaultRole,
			entity.UpdatedAtUtc);
	}

	private static RegistrationSettingsRecord Map(RegistrationSettingsEntity entity)
	{
		return new RegistrationSettingsRecord(
			entity.AllowOpenRegistration,
			entity.RequireAdminApprovalForNewUsers,
			entity.DefaultRole,
			entity.UpdatedAtUtc);
	}
}
