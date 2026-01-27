namespace Tindarr.Contracts.Auth;

public sealed record LoginRequest(string UserId, string Password);

