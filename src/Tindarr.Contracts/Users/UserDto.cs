namespace Tindarr.Contracts.Users;

public sealed record UserDto(
	string UserId,
	string DisplayName,
	DateTimeOffset CreatedAtUtc,
	IReadOnlyList<string> Roles,
	bool HasPassword);

