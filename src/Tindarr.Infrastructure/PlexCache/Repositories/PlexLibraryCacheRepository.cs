using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Tindarr.Application.Abstractions.Integrations;
using Tindarr.Application.Abstractions.Persistence;
using Tindarr.Domain.Common;
using Tindarr.Infrastructure.PlexCache.Entities;

namespace Tindarr.Infrastructure.PlexCache.Repositories;

public sealed class PlexLibraryCacheRepository(PlexCacheDbContext db) : IPlexLibraryCacheRepository
{
	private const int SqliteParameterChunkSize = 400;

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

	public async Task<int> CountTmdbIdsAsync(ServiceScope scope, CancellationToken cancellationToken)
	{
		if (scope.ServiceType != ServiceType.Plex)
		{
			return 0;
		}

		return await db.LibraryItems
			.AsNoTracking()
			.Where(x => x.ServerId == scope.ServerId && x.TmdbId > 0)
			.Select(x => x.TmdbId)
			.Distinct()
			.CountAsync(cancellationToken);
	}

	public async Task<IReadOnlyList<PlexLibraryItem>> ListItemsAsync(ServiceScope scope, int skip, int take, CancellationToken cancellationToken)
	{
		if (scope.ServiceType != ServiceType.Plex)
		{
			return Array.Empty<PlexLibraryItem>();
		}

		skip = Math.Max(0, skip);
		take = Math.Clamp(take, 1, 500);

		var rows = await db.LibraryItems
			.AsNoTracking()
			.Where(x => x.ServerId == scope.ServerId && x.TmdbId > 0)
			.OrderByDescending(x => x.UpdatedAtUtc)
			.ThenBy(x => x.TmdbId)
			.Skip(skip)
			.Take(take)
			.Select(x => new { x.TmdbId, x.Title, x.RatingKey })
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);

		return rows
			.Select(x => new PlexLibraryItem(
				TmdbId: x.TmdbId,
				Title: string.IsNullOrWhiteSpace(x.Title) ? $"TMDB:{x.TmdbId}" : x.Title!,
				RatingKey: string.IsNullOrWhiteSpace(x.RatingKey) ? null : x.RatingKey,
				Guid: null))
			.ToList();
	}

	public async Task<string?> TryGetRatingKeyAsync(ServiceScope scope, int tmdbId, CancellationToken cancellationToken)
	{
		if (scope.ServiceType != ServiceType.Plex)
		{
			return null;
		}
		if (tmdbId <= 0)
		{
			return null;
		}

		// Prefer a non-null rating key if present.
		return await db.LibraryItems
			.AsNoTracking()
			.Where(x => x.ServerId == scope.ServerId && x.TmdbId == tmdbId)
			.OrderByDescending(x => x.RatingKey != null)
			.Select(x => x.RatingKey)
			.FirstOrDefaultAsync(cancellationToken)
			.ConfigureAwait(false);
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

	public async Task ReplaceItemsAsync(ServiceScope scope, IReadOnlyCollection<PlexLibraryItem> items, DateTimeOffset syncedAtUtc, CancellationToken cancellationToken)
	{
		if (scope.ServiceType != ServiceType.Plex)
		{
			return;
		}

		await db.LibraryItems
			.Where(x => x.ServerId == scope.ServerId)
			.ExecuteDeleteAsync(cancellationToken)
			.ConfigureAwait(false);

		if (items.Count == 0)
		{
			return;
		}

		var rows = items
			.Where(x => x.TmdbId > 0)
			.GroupBy(x => x.TmdbId)
			.Select(g =>
			{
				var best = g.FirstOrDefault(i => !string.IsNullOrWhiteSpace(i.RatingKey)) ?? g.First();
				return new PlexLibraryCacheItemEntity
				{
					ServerId = scope.ServerId,
					TmdbId = g.Key,
					RatingKey = string.IsNullOrWhiteSpace(best.RatingKey) ? null : best.RatingKey!.Trim(),
					Title = string.IsNullOrWhiteSpace(best.Title) ? null : best.Title.Trim(),
					UpdatedAtUtc = syncedAtUtc
				};
			})
			.ToList();

		if (rows.Count == 0)
		{
			return;
		}

		db.LibraryItems.AddRange(rows);
		try
		{
			await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
		}
		catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
		{
			// Ignore duplicates (unique constraint).
		}
	}

	public async Task SyncItemsAsync(ServiceScope scope, IReadOnlyCollection<PlexLibraryItem> items, DateTimeOffset syncedAtUtc, CancellationToken cancellationToken)
	{
		if (scope.ServiceType != ServiceType.Plex)
		{
			return;
		}

		var serverId = scope.ServerId;
		var normalized = items
			.Where(x => x.TmdbId > 0)
			.GroupBy(x => x.TmdbId)
			.Select(g =>
			{
				var best = g.FirstOrDefault(i => !string.IsNullOrWhiteSpace(i.RatingKey)) ?? g.First();
				return new
				{
					TmdbId = g.Key,
					RatingKey = Normalize(best.RatingKey),
					Title = Normalize(best.Title)
				};
			})
			.ToDictionary(x => x.TmdbId, x => (x.RatingKey, x.Title));

		await using var tx = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			if (normalized.Count == 0)
			{
				await db.LibraryItems
					.Where(x => x.ServerId == serverId)
					.ExecuteDeleteAsync(cancellationToken)
					.ConfigureAwait(false);
				await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
				return;
			}

			var existing = await db.LibraryItems
				.AsNoTracking()
				.Where(x => x.ServerId == serverId)
				.Select(x => new { x.TmdbId, x.RatingKey, x.Title })
				.ToListAsync(cancellationToken)
				.ConfigureAwait(false);

			var existingById = existing.ToDictionary(
				x => x.TmdbId,
				x => (RatingKey: Normalize(x.RatingKey), Title: Normalize(x.Title)));

			var incomingIds = normalized.Keys.ToHashSet();
			var existingIds = existingById.Keys.ToHashSet();

			var toDelete = existingIds.Except(incomingIds).ToList();
			for (var i = 0; i < toDelete.Count; i += SqliteParameterChunkSize)
			{
				var chunk = toDelete.Skip(i).Take(SqliteParameterChunkSize).ToList();
				await db.LibraryItems
					.Where(x => x.ServerId == serverId && chunk.Contains(x.TmdbId))
					.ExecuteDeleteAsync(cancellationToken)
					.ConfigureAwait(false);
			}

			var toAddIds = incomingIds.Except(existingIds).ToList();
			if (toAddIds.Count > 0)
			{
				var addRows = new List<PlexLibraryCacheItemEntity>(toAddIds.Count);
				foreach (var tmdbId in toAddIds)
				{
					var v = normalized[tmdbId];
					addRows.Add(new PlexLibraryCacheItemEntity
					{
						ServerId = serverId,
						TmdbId = tmdbId,
						RatingKey = v.RatingKey,
						Title = v.Title,
						UpdatedAtUtc = syncedAtUtc
					});
				}

				db.LibraryItems.AddRange(addRows);
				try
				{
					await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
				}
				catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
				{
					// Ignore duplicates (unique constraint).
				}
			}

			var toUpdate = incomingIds
				.Intersect(existingIds)
				.Where(id =>
				{
					var inc = normalized[id];
					var cur = existingById[id];
					return !string.Equals(inc.RatingKey, cur.RatingKey, StringComparison.Ordinal)
						|| !string.Equals(inc.Title, cur.Title, StringComparison.Ordinal);
				})
				.ToList();

			foreach (var tmdbId in toUpdate)
			{
				var inc = normalized[tmdbId];
				await db.LibraryItems
					.Where(x => x.ServerId == serverId && x.TmdbId == tmdbId)
					.ExecuteUpdateAsync(setters => setters
						.SetProperty(x => x.RatingKey, inc.RatingKey)
						.SetProperty(x => x.Title, inc.Title)
						.SetProperty(x => x.UpdatedAtUtc, syncedAtUtc),
						cancellationToken)
					.ConfigureAwait(false);
			}

			// Refresh UpdatedAtUtc for all remaining rows in one statement.
			await db.LibraryItems
				.Where(x => x.ServerId == serverId)
				.ExecuteUpdateAsync(setters => setters.SetProperty(x => x.UpdatedAtUtc, syncedAtUtc), cancellationToken)
				.ConfigureAwait(false);

			await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
		}
		catch
		{
			await tx.RollbackAsync(cancellationToken).ConfigureAwait(false);
			throw;
		}
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

	private static string? Normalize(string? value)
	{
		return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
	}
}
