using Microsoft.EntityFrameworkCore;
using Tindarr.Application.Abstractions.Persistence;
using Tindarr.Domain.Common;
using Tindarr.Infrastructure.Persistence.Entities;

namespace Tindarr.Infrastructure.Persistence.Repositories;

public sealed class RadarrPendingAddRepository(TindarrDbContext db) : IRadarrPendingAddRepository
{
	public async Task<bool> TryEnqueueAsync(
		ServiceScope scope,
		string userId,
		int tmdbId,
		DateTimeOffset readyAtUtc,
		CancellationToken cancellationToken)
	{
		var existing = await db.RadarrPendingAdds
			.SingleOrDefaultAsync(x =>
				x.ServiceType == scope.ServiceType
				&& x.ServerId == scope.ServerId
				&& x.UserId == userId
				&& x.TmdbId == tmdbId
				&& x.CanceledAtUtc == null,
				cancellationToken);

		if (existing is not null)
		{
			if (existing.ProcessedAtUtc is null)
			{
				// Keep the earliest ready time; don't extend a user's cooldown.
				if (readyAtUtc < existing.ReadyAtUtc)
				{
					existing.ReadyAtUtc = readyAtUtc;
					await db.SaveChangesAsync(cancellationToken);
				}
			}
			return false;
		}

		var now = DateTimeOffset.UtcNow;
		db.RadarrPendingAdds.Add(new RadarrPendingAddEntity
		{
			ServiceType = scope.ServiceType,
			ServerId = scope.ServerId,
			UserId = userId,
			TmdbId = tmdbId,
			ReadyAtUtc = readyAtUtc,
			CreatedAtUtc = now,
			AttemptCount = 0
		});

		await db.SaveChangesAsync(cancellationToken);
		return true;
	}

	public async Task<bool> TryCancelAsync(ServiceScope scope, string userId, int tmdbId, CancellationToken cancellationToken)
	{
		var entity = await db.RadarrPendingAdds
			.SingleOrDefaultAsync(x =>
				x.ServiceType == scope.ServiceType
				&& x.ServerId == scope.ServerId
				&& x.UserId == userId
				&& x.TmdbId == tmdbId
				&& x.CanceledAtUtc == null
				&& x.ProcessedAtUtc == null,
				cancellationToken);

		if (entity is null)
		{
			return false;
		}

		entity.CanceledAtUtc = DateTimeOffset.UtcNow;
		await db.SaveChangesAsync(cancellationToken);
		return true;
	}

	public async Task<IReadOnlyList<RadarrPendingAddRecord>> ListDueAsync(DateTimeOffset nowUtc, int limit, CancellationToken cancellationToken)
	{
		limit = Math.Clamp(limit, 1, 500);

		// SQLite provider has limited DateTimeOffset translation support in LINQ.
		// Filter and sort in-memory to avoid runtime translation exceptions.
		var rows = await db.RadarrPendingAdds
			.AsNoTracking()
			.Where(x => x.CanceledAtUtc == null && x.ProcessedAtUtc == null)
			.ToListAsync(cancellationToken);

		rows = rows
			.Where(x => x.ReadyAtUtc <= nowUtc)
			.OrderBy(x => x.ReadyAtUtc)
			.ThenBy(x => x.Id)
			.Take(limit)
			.ToList();

		return rows
			.Select(x => new RadarrPendingAddRecord(
				x.Id,
				x.ServiceType,
				x.ServerId,
				x.UserId,
				x.TmdbId,
				x.ReadyAtUtc,
				x.AttemptCount,
				x.LastError))
			.ToList();
	}

	public async Task MarkProcessedAsync(long id, DateTimeOffset processedAtUtc, CancellationToken cancellationToken)
	{
		var entity = await db.RadarrPendingAdds.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
		if (entity is null)
		{
			return;
		}

		entity.ProcessedAtUtc = processedAtUtc;
		entity.LastError = null;
		await db.SaveChangesAsync(cancellationToken);
	}

	public async Task RescheduleAsync(long id, DateTimeOffset nextReadyAtUtc, string? lastError, CancellationToken cancellationToken)
	{
		var entity = await db.RadarrPendingAdds.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
		if (entity is null)
		{
			return;
		}

		entity.AttemptCount += 1;
		entity.ReadyAtUtc = nextReadyAtUtc;
		entity.LastError = lastError;
		await db.SaveChangesAsync(cancellationToken);
	}
}
