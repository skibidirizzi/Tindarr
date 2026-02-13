namespace Tindarr.Contracts.Rooms;

public sealed record RoomMatchesResponse(
	string RoomId,
	string ServiceType,
	string ServerId,
	IReadOnlyList<int> TmdbIds);
