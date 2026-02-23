namespace Tindarr.Contracts.Setup;

public sealed record CheckReachabilityResponse(bool Reachable, string? Message);
