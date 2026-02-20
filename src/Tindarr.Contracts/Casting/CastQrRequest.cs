namespace Tindarr.Contracts.Casting;

/// <summary>
/// Request to cast the room join QR. When both LAN and WAN are configured, use <see cref="Variant"/> to pick which QR to cast.
/// </summary>
public sealed record CastQrRequest(string DeviceId, string? Variant = null);
