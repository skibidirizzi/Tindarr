using Tindarr.Domain.Interactions;

namespace Tindarr.Application.Interfaces.Rooms;

public interface IRoomInteractionStore
{
	Task AddAsync(string roomId, Interaction interaction, CancellationToken cancellationToken);
	Task<IReadOnlyList<Interaction>> ListAsync(string roomId, int limit, CancellationToken cancellationToken);
	Task CleanupExpiredAsync(CancellationToken cancellationToken);
}
