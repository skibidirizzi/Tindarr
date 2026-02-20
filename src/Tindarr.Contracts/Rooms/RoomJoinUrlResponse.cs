namespace Tindarr.Contracts.Rooms;

/// <summary>
/// Join URL(s) for a room. <see cref="Url"/> is the default (auto-selected).
/// <see cref="LanUrl"/> and <see cref="WanUrl"/> are set when both are configured for hotswapping.
/// </summary>
public sealed record RoomJoinUrlResponse(string Url, string? LanUrl = null, string? WanUrl = null);
