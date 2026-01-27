namespace Tindarr.Contracts.Users;

public sealed record CreateUserRequest(
	string UserId,
	string DisplayName,
	string Password,
	IReadOnlyList<string>? Roles);

