using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Tindarr.Application.Abstractions.Persistence;
using Tindarr.Domain.Common;
using Tindarr.Infrastructure.PlexCache.Entities;

namespace Tindarr.Infrastructure.PlexCache.Repositories;

public sealed class PlexLibraryCacheRepository(PlexCacheDbContext db) : IPlexLibraryCacheRepository
{
	public async Task<IReadOnlyCollection<int>> GetTmdbIdsAsync(ServiceScope scope, CancellationToken cancellationToken)
	{
		if (scope.ServiceType != ServiceType.Plex)
		{
			return Array.Empty<int>();
		}

		var ids = await db.LibraryItems
			.AsNoTracking()
			.Where(x => x.ServerId == scope.ServerId)
			.Select(x => x.TmdbId)
			.Distinct()
			.ToListAsync(cancellationToken);

		return ids;
	}

	public async Task ReplaceTmdbIdsAsync(ServiceScope scope, IReadOnlyCollection<int> tmdbIds, DateTimeOffset syncedAtUtc, CancellationToken cancellationToken)
	{
		if (scope.ServiceType != ServiceType.Plex)
		{
			return;
		}

		await db.LibraryItems
			.Where(x => x.ServerId == scope.ServerId)
			.ExecuteDeleteAsync(cancellationToken);

		await AddTmdbIdsAsync(scope, tmdbIds, syncedAtUtc, cancellationToken);
	}

	public async Task AddTmdbIdsAsync(ServiceScope scope, IReadOnlyCollection<int> tmdbIds, DateTimeOffset syncedAtUtc, CancellationToken cancellationToken)
	{
		if (scope.ServiceType != ServiceType.Plex)
		{
			return;
		}

		if (tmdbIds.Count == 0)
		{
			return;
		}

		var rows = tmdbIds
			.Where(x => x > 0)
			.Distinct()
			.Select(id => new PlexLibraryCacheItemEntity
			{
				ServerId = scope.ServerId,
				TmdbId = id,
				UpdatedAtUtc = syncedAtUtc
			})
			.ToList();

		if (rows.Count == 0)
		{
			return;
		}

		db.LibraryItems.AddRange(rows);

		try
		{
			await db.SaveChangesAsync(cancellationToken);
		}
		catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
		{
			// Ignore duplicates (unique constraint).
		}
	}

	public async Task RemoveTmdbIdsAsync(ServiceScope scope, IReadOnlyCollection<int> tmdbIds, CancellationToken cancellationToken)
	{
		if (scope.ServiceType != ServiceType.Plex)
		{
			return;
		}

		if (tmdbIds.Count == 0)
		{
			return;
		}

		var set = tmdbIds.Where(x => x > 0).ToHashSet();
		if (set.Count == 0)
		{
			return;
		}

		await db.LibraryItems
			.Where(x => x.ServerId == scope.ServerId && set.Contains(x.TmdbId))
			.ExecuteDeleteAsync(cancellationToken);
	}

	private static bool IsUniqueConstraintViolation(DbUpdateException exception)
	{
		return exception.InnerException is SqliteException sqliteException
			&& (sqliteException.SqliteExtendedErrorCode == 1555
				|| sqliteException.SqliteExtendedErrorCode == 2067);
	}
}
