namespace Tindarr.Contracts.Rooms;

public sealed record RoomStateResponse(
	string RoomId,
	string OwnerUserId,
	string ServiceType,
	string ServerId,
	bool IsClosed,
	string CreatedAtUtc,
	string LastActivityAtUtc,
	IReadOnlyList<RoomMemberDto> Members);
