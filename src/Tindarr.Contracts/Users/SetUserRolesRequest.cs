namespace Tindarr.Contracts.Users;

public sealed record SetUserRolesRequest(IReadOnlyList<string> Roles);

