using Tindarr.Domain.Common;

namespace Tindarr.Domain.Rooms;

public sealed record RoomState(
	string RoomId,
	string OwnerUserId,
	ServiceScope Scope,
	bool IsClosed,
	DateTimeOffset CreatedAtUtc,
	DateTimeOffset LastActivityAtUtc,
	IReadOnlyList<RoomMember> Members);
