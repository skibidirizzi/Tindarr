namespace Tindarr.Domain.Rooms;

public sealed record RoomMember(
	string UserId,
	DateTimeOffset JoinedAtUtc);
