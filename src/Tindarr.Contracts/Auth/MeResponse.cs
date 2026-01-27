namespace Tindarr.Contracts.Auth;

public sealed record MeResponse(string UserId, string DisplayName, IReadOnlyList<string> Roles);

