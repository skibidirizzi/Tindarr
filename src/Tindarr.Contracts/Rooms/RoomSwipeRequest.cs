using Tindarr.Contracts.Interactions;

namespace Tindarr.Contracts.Rooms;

public sealed record RoomSwipeRequest(
	int TmdbId,
	SwipeActionDto Action);
