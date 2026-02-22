namespace Tindarr.Contracts.Rooms;

public sealed record CreateRoomRequest(
	string ServiceType,
	string ServerId,
	/// <summary>Optional custom room name (URL slug). If provided, becomes the room ID; otherwise a GUID is used.</summary>
	string? RoomName = null);
