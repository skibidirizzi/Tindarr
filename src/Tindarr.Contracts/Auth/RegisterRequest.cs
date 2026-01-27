namespace Tindarr.Contracts.Auth;

public sealed record RegisterRequest(string UserId, string DisplayName, string Password);

