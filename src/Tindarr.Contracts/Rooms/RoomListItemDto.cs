namespace Tindarr.Contracts.Rooms;

public sealed record RoomListItemDto(
	string RoomId,
	string OwnerUserId,
	string ServiceType,
	string ServerId,
	bool IsClosed,
	string CreatedAtUtc,
	string LastActivityAtUtc,
	int MemberCount);
