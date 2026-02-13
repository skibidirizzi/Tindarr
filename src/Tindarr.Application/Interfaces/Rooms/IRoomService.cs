using Tindarr.Domain.Common;
using Tindarr.Domain.Interactions;
using Tindarr.Domain.Rooms;

namespace Tindarr.Application.Interfaces.Rooms;

public interface IRoomService
{
	Task<RoomState> CreateAsync(string ownerUserId, ServiceScope scope, CancellationToken cancellationToken);
	Task<RoomState> JoinAsync(string roomId, string userId, CancellationToken cancellationToken);
	Task<RoomState> CloseAsync(string roomId, string ownerUserId, CancellationToken cancellationToken);
	Task<RoomState?> GetAsync(string roomId, CancellationToken cancellationToken);
	Task<IReadOnlyList<SwipeCard>> GetSwipeDeckAsync(string roomId, string userId, int limit, CancellationToken cancellationToken);
	Task<Interaction> AddInteractionAsync(string roomId, string userId, int tmdbId, InteractionAction action, CancellationToken cancellationToken);
	Task<IReadOnlyList<int>> ListMatchesAsync(string roomId, CancellationToken cancellationToken);
}
