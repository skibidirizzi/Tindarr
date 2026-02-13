using Tindarr.Contracts.Interactions;

namespace Tindarr.Contracts.Rooms;

public sealed record RoomSwipeResponse(
	int TmdbId,
	SwipeActionDto Action,
	string CreatedAtUtc);
