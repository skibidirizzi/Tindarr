namespace Tindarr.Contracts.Rooms;

public sealed record RoomMemberDto(
	string UserId,
	string JoinedAtUtc);
