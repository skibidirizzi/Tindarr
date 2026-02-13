namespace Tindarr.Contracts.Rooms;

public sealed record CreateRoomResponse(
	string RoomId,
	string OwnerUserId,
	string ServiceType,
	string ServerId,
	IReadOnlyList<RoomMemberDto> Members);
