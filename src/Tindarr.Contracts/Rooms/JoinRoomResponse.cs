namespace Tindarr.Contracts.Rooms;

public sealed record JoinRoomResponse(
	string RoomId,
	string OwnerUserId,
	string ServiceType,
	string ServerId,
	IReadOnlyList<RoomMemberDto> Members);
