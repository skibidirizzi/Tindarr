using Tindarr.Domain.Rooms;

namespace Tindarr.Application.Interfaces.Rooms;

public interface IRoomStore
{
	Task CreateAsync(RoomState state, CancellationToken cancellationToken);
	Task<RoomState?> GetAsync(string roomId, CancellationToken cancellationToken);
	/// <summary>Lists non-expired rooms; when openOnly is true, returns only rooms that are not closed.</summary>
	Task<IReadOnlyList<RoomState>> ListAliveAsync(bool openOnly, CancellationToken cancellationToken);
	Task UpdateAsync(RoomState state, CancellationToken cancellationToken);
	Task CleanupExpiredAsync(CancellationToken cancellationToken);
}
