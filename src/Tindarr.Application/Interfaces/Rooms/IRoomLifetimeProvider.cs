namespace Tindarr.Application.Interfaces.Rooms;

public interface IRoomLifetimeProvider
{
	Task<TimeSpan> GetRoomTtlAsync(CancellationToken cancellationToken);
	Task<TimeSpan> GetGuestSessionTtlAsync(CancellationToken cancellationToken);
}
