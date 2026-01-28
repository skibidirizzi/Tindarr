using Microsoft.EntityFrameworkCore;
using Tindarr.Application.Abstractions.Persistence;
using Tindarr.Domain.Common;
using Tindarr.Infrastructure.Persistence.Entities;

namespace Tindarr.Infrastructure.Persistence.Repositories;

public sealed class LibraryCacheRepository(TindarrDbContext db) : ILibraryCacheRepository
{
	public async Task<IReadOnlyCollection<int>> GetTmdbIdsAsync(ServiceScope scope, CancellationToken cancellationToken)
	{
		var ids = await db.LibraryCache
			.AsNoTracking()
			.Where(x => x.ServiceType == scope.ServiceType && x.ServerId == scope.ServerId)
			.Select(x => x.TmdbId)
			.Distinct()
			.ToListAsync(cancellationToken);

		return ids;
	}

	public async Task ReplaceTmdbIdsAsync(ServiceScope scope, IReadOnlyCollection<int> tmdbIds, DateTimeOffset syncedAtUtc, CancellationToken cancellationToken)
	{
		await db.LibraryCache
			.Where(x => x.ServiceType == scope.ServiceType && x.ServerId == scope.ServerId)
			.ExecuteDeleteAsync(cancellationToken);

		if (tmdbIds.Count == 0)
		{
			return;
		}

		var rows = tmdbIds
			.Where(x => x > 0)
			.Distinct()
			.Select(id => new LibraryCacheEntity
			{
				ServiceType = scope.ServiceType,
				ServerId = scope.ServerId,
				TmdbId = id,
				SyncedAtUtc = syncedAtUtc
			})
			.ToList();

		if (rows.Count == 0)
		{
			return;
		}

		db.LibraryCache.AddRange(rows);
		await db.SaveChangesAsync(cancellationToken);
	}

	public async Task AddTmdbIdsAsync(ServiceScope scope, IReadOnlyCollection<int> tmdbIds, DateTimeOffset syncedAtUtc, CancellationToken cancellationToken)
	{
		if (tmdbIds.Count == 0)
		{
			return;
		}

		var rows = tmdbIds
			.Where(x => x > 0)
			.Distinct()
			.Select(id => new LibraryCacheEntity
			{
				ServiceType = scope.ServiceType,
				ServerId = scope.ServerId,
				TmdbId = id,
				SyncedAtUtc = syncedAtUtc
			})
			.ToList();

		db.LibraryCache.AddRange(rows);

		try
		{
			await db.SaveChangesAsync(cancellationToken);
		}
		catch (DbUpdateException)
		{
			// Ignore duplicates (unique constraint).
		}
	}
}
