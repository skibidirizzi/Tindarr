namespace Tindarr.Contracts.Auth;

public sealed record GuestLoginRequest(string RoomId, string? DisplayName);
