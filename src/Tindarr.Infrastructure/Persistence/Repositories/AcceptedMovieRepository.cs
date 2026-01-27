using Microsoft.EntityFrameworkCore;
using Tindarr.Application.Abstractions.Persistence;
using Tindarr.Domain.AcceptedMovies;
using Tindarr.Domain.Common;
using Tindarr.Infrastructure.Persistence.Entities;

namespace Tindarr.Infrastructure.Persistence.Repositories;

public sealed class AcceptedMovieRepository(TindarrDbContext db) : IAcceptedMovieRepository
{
	public async Task<IReadOnlyList<AcceptedMovie>> ListAsync(ServiceScope scope, int limit, CancellationToken cancellationToken)
	{
		var rows = await db.AcceptedMovies
			.AsNoTracking()
			.Where(x => x.ServiceType == scope.ServiceType && x.ServerId == scope.ServerId)
			.OrderByDescending(x => x.AcceptedAtUtc)
			.ThenByDescending(x => x.Id)
			.Take(Math.Clamp(limit, 1, 500))
			.ToListAsync(cancellationToken);

		return rows
			.Select(x => new AcceptedMovie(
				new ServiceScope(x.ServiceType, x.ServerId),
				x.TmdbId,
				x.AcceptedByUserId,
				x.AcceptedAtUtc))
			.ToList();
	}

	public async Task<bool> TryAddAsync(ServiceScope scope, int tmdbId, string? acceptedByUserId, CancellationToken cancellationToken)
	{
		var entity = new AcceptedMovieEntity
		{
			ServiceType = scope.ServiceType,
			ServerId = scope.ServerId,
			TmdbId = tmdbId,
			AcceptedByUserId = string.IsNullOrWhiteSpace(acceptedByUserId) ? null : acceptedByUserId.Trim(),
			AcceptedAtUtc = DateTimeOffset.UtcNow
		};

		db.AcceptedMovies.Add(entity);

		try
		{
			await db.SaveChangesAsync(cancellationToken);
			return true;
		}
		catch (DbUpdateException)
		{
			// Unique constraint: (ServiceType, ServerId, TmdbId). Treat duplicates as no-op.
			return false;
		}
	}
}

