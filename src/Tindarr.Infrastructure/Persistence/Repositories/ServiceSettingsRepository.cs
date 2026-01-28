using Microsoft.EntityFrameworkCore;
using Tindarr.Application.Abstractions.Persistence;
using Tindarr.Domain.Common;
using Tindarr.Infrastructure.Persistence.Entities;

namespace Tindarr.Infrastructure.Persistence.Repositories;

public sealed class ServiceSettingsRepository(TindarrDbContext db) : IServiceSettingsRepository
{
	public async Task<ServiceSettingsRecord?> GetAsync(ServiceScope scope, CancellationToken cancellationToken)
	{
		var entity = await db.ServiceSettings
			.AsNoTracking()
			.SingleOrDefaultAsync(x => x.ServiceType == scope.ServiceType && x.ServerId == scope.ServerId, cancellationToken);

		return entity is null ? null : Map(entity);
	}

	public async Task<IReadOnlyList<ServiceSettingsRecord>> ListAsync(ServiceType serviceType, CancellationToken cancellationToken)
	{
		var rows = await db.ServiceSettings
			.AsNoTracking()
			.Where(x => x.ServiceType == serviceType)
			.OrderBy(x => x.ServerId)
			.ToListAsync(cancellationToken);

		return rows.Select(Map).ToList();
	}

	public async Task UpsertAsync(ServiceScope scope, ServiceSettingsUpsert upsert, CancellationToken cancellationToken)
	{
		var entity = await db.ServiceSettings
			.SingleOrDefaultAsync(x => x.ServiceType == scope.ServiceType && x.ServerId == scope.ServerId, cancellationToken);

		var now = DateTimeOffset.UtcNow;

		if (entity is null)
		{
			entity = new ServiceSettingsEntity
			{
				ServiceType = scope.ServiceType,
				ServerId = scope.ServerId,
				RadarrBaseUrl = upsert.RadarrBaseUrl,
				RadarrApiKey = upsert.RadarrApiKey,
				RadarrQualityProfileId = upsert.RadarrQualityProfileId,
				RadarrRootFolderPath = upsert.RadarrRootFolderPath,
				RadarrTagLabel = upsert.RadarrTagLabel,
				RadarrTagId = upsert.RadarrTagId,
				RadarrAutoAddEnabled = upsert.RadarrAutoAddEnabled,
				RadarrLastAutoAddAcceptedId = upsert.RadarrLastAutoAddAcceptedId,
				RadarrLastLibrarySyncUtc = upsert.RadarrLastLibrarySyncUtc,
				UpdatedAtUtc = now
			};

			db.ServiceSettings.Add(entity);
		}
		else
		{
			entity.RadarrBaseUrl = upsert.RadarrBaseUrl;
			entity.RadarrApiKey = upsert.RadarrApiKey;
			entity.RadarrQualityProfileId = upsert.RadarrQualityProfileId;
			entity.RadarrRootFolderPath = upsert.RadarrRootFolderPath;
			entity.RadarrTagLabel = upsert.RadarrTagLabel;
			entity.RadarrTagId = upsert.RadarrTagId;
			entity.RadarrAutoAddEnabled = upsert.RadarrAutoAddEnabled;
			entity.RadarrLastAutoAddAcceptedId = upsert.RadarrLastAutoAddAcceptedId;
			entity.RadarrLastLibrarySyncUtc = upsert.RadarrLastLibrarySyncUtc;
			entity.UpdatedAtUtc = now;
		}

		await db.SaveChangesAsync(cancellationToken);
	}

	private static ServiceSettingsRecord Map(ServiceSettingsEntity entity)
	{
		return new ServiceSettingsRecord(
			entity.ServiceType,
			entity.ServerId,
			entity.RadarrBaseUrl,
			entity.RadarrApiKey,
			entity.RadarrQualityProfileId,
			entity.RadarrRootFolderPath,
			entity.RadarrTagLabel,
			entity.RadarrTagId,
			entity.RadarrAutoAddEnabled,
			entity.RadarrLastAutoAddAcceptedId,
			entity.RadarrLastLibrarySyncUtc,
			entity.UpdatedAtUtc);
	}
}
