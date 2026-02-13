using Tindarr.Domain.Rooms;

namespace Tindarr.Application.Interfaces.Rooms;

public interface IRoomStore
{
	Task CreateAsync(RoomState state, CancellationToken cancellationToken);
	Task<RoomState?> GetAsync(string roomId, CancellationToken cancellationToken);
	Task UpdateAsync(RoomState state, CancellationToken cancellationToken);
	Task CleanupExpiredAsync(CancellationToken cancellationToken);
}
