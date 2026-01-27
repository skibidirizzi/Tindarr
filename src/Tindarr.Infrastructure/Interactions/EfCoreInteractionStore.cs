using Microsoft.EntityFrameworkCore;
using Tindarr.Application.Interfaces.Interactions;
using Tindarr.Domain.Common;
using Tindarr.Domain.Interactions;
using Tindarr.Infrastructure.Persistence;
using Tindarr.Infrastructure.Persistence.Entities;

namespace Tindarr.Infrastructure.Interactions;

public sealed class EfCoreInteractionStore(TindarrDbContext db) : IInteractionStore
{
	public async Task AddAsync(Interaction interaction, CancellationToken cancellationToken)
	{
		var entity = new InteractionEntity
		{
			UserId = interaction.UserId,
			ServiceType = interaction.Scope.ServiceType,
			ServerId = interaction.Scope.ServerId,
			TmdbId = interaction.TmdbId,
			Action = interaction.Action,
			CreatedAtUtc = interaction.CreatedAtUtc
		};

		db.Interactions.Add(entity);
		await db.SaveChangesAsync(cancellationToken);
	}

	public async Task<Interaction?> UndoLastAsync(string userId, ServiceScope scope, CancellationToken cancellationToken)
	{
		var last = await db.Interactions
			.Where(x => x.UserId == userId && x.ServiceType == scope.ServiceType && x.ServerId == scope.ServerId)
			// SQLite (EF Core) cannot translate DateTimeOffset ordering reliably; Id is monotonically increasing.
			.OrderByDescending(x => x.Id)
			.FirstOrDefaultAsync(cancellationToken);

		if (last is null)
		{
			return null;
		}

		db.Interactions.Remove(last);
		await db.SaveChangesAsync(cancellationToken);

		return new Interaction(
			last.UserId,
			new ServiceScope(last.ServiceType, last.ServerId),
			last.TmdbId,
			last.Action,
			last.CreatedAtUtc);
	}

	public async Task<IReadOnlyCollection<int>> GetInteractedTmdbIdsAsync(string userId, ServiceScope scope, CancellationToken cancellationToken)
	{
		// We keep this as a distinct list to avoid pulling unnecessary rows into memory.
		var ids = await db.Interactions
			.Where(x => x.UserId == userId && x.ServiceType == scope.ServiceType && x.ServerId == scope.ServerId)
			.Select(x => x.TmdbId)
			.Distinct()
			.ToListAsync(cancellationToken);

		return ids;
	}

	public async Task<IReadOnlyList<Interaction>> ListAsync(
		string userId,
		ServiceScope scope,
		InteractionAction? action,
		int? tmdbId,
		int limit,
		CancellationToken cancellationToken)
	{
		var query = db.Interactions
			.AsNoTracking()
			.Where(x => x.UserId == userId && x.ServiceType == scope.ServiceType && x.ServerId == scope.ServerId);

		if (action is not null)
		{
			query = query.Where(x => x.Action == action.Value);
		}

		if (tmdbId is not null)
		{
			query = query.Where(x => x.TmdbId == tmdbId.Value);
		}

		// Newest first. Use Id ordering for SQLite compatibility (CreatedAtUtc is DateTimeOffset).
		var rows = await query
			.OrderByDescending(x => x.Id)
			.Take(Math.Max(1, limit))
			.ToListAsync(cancellationToken);

		return rows
			.Select(x => new Interaction(
				x.UserId,
				new ServiceScope(x.ServiceType, x.ServerId),
				x.TmdbId,
				x.Action,
				x.CreatedAtUtc))
			.ToList();
	}

	public async Task<IReadOnlyList<Interaction>> ListForScopeAsync(
		ServiceScope scope,
		int? tmdbId,
		int limit,
		CancellationToken cancellationToken)
	{
		var query = db.Interactions
			.AsNoTracking()
			.Where(x => x.ServiceType == scope.ServiceType && x.ServerId == scope.ServerId);

		if (tmdbId is not null)
		{
			query = query.Where(x => x.TmdbId == tmdbId.Value);
		}

		// Newest first. Use Id ordering for SQLite compatibility (CreatedAtUtc is DateTimeOffset).
		var rows = await query
			.OrderByDescending(x => x.Id)
			.Take(Math.Max(1, limit))
			.ToListAsync(cancellationToken);

		return rows
			.Select(x => new Interaction(
				x.UserId,
				new ServiceScope(x.ServiceType, x.ServerId),
				x.TmdbId,
				x.Action,
				x.CreatedAtUtc))
			.ToList();
	}
}

