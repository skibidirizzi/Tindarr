namespace Tindarr.Contracts.Rooms;

public sealed record CreateRoomRequest(
	string ServiceType,
	string ServerId);
