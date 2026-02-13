using Tindarr.Application.Interfaces.Interactions;
using Tindarr.Domain.Common;
using Tindarr.Domain.Interactions;

namespace Tindarr.Infrastructure.Interactions;

/// <summary>
/// Routes interactions to either the main EF Core store (tindarr.db) or an in-memory store.
///
/// This is used to keep "solo media server swiping" (Plex/Jellyfin/Emby) ephemeral and
/// prevent it from polluting the main database.
/// </summary>
public sealed class RoutingInteractionStore(
	EfCoreInteractionStore db,
	InMemoryInteractionStore memory) : IInteractionStore
{
	private static bool UseMemory(ServiceScope scope)
	{
		return scope.ServiceType is ServiceType.Plex or ServiceType.Jellyfin or ServiceType.Emby;
	}

	private IInteractionStore Resolve(ServiceScope scope)
	{
		return UseMemory(scope) ? memory : db;
	}

	public Task AddAsync(Interaction interaction, CancellationToken cancellationToken)
	{
		return Resolve(interaction.Scope).AddAsync(interaction, cancellationToken);
	}

	public Task<Interaction?> UndoLastAsync(string userId, ServiceScope scope, CancellationToken cancellationToken)
	{
		return Resolve(scope).UndoLastAsync(userId, scope, cancellationToken);
	}

	public Task<IReadOnlyCollection<int>> GetInteractedTmdbIdsAsync(string userId, ServiceScope scope, CancellationToken cancellationToken)
	{
		return Resolve(scope).GetInteractedTmdbIdsAsync(userId, scope, cancellationToken);
	}

	public Task<IReadOnlyList<Interaction>> ListAsync(
		string userId,
		ServiceScope scope,
		InteractionAction? action,
		int? tmdbId,
		int limit,
		CancellationToken cancellationToken)
	{
		return Resolve(scope).ListAsync(userId, scope, action, tmdbId, limit, cancellationToken);
	}

	public Task<IReadOnlyList<Interaction>> ListForScopeAsync(
		ServiceScope scope,
		int? tmdbId,
		int limit,
		CancellationToken cancellationToken)
	{
		return Resolve(scope).ListForScopeAsync(scope, tmdbId, limit, cancellationToken);
	}

	public Task<IReadOnlyList<Interaction>> SearchAsync(
		string? userId,
		ServiceScope? scope,
		InteractionAction? action,
		int? tmdbId,
		int limit,
		CancellationToken cancellationToken)
	{
		// Admin/search queries without an explicit scope should remain DB-backed.
		if (scope is null)
		{
			return db.SearchAsync(userId, scope, action, tmdbId, limit, cancellationToken);
		}

		return Resolve(scope).SearchAsync(userId, scope, action, tmdbId, limit, cancellationToken);
	}
}
