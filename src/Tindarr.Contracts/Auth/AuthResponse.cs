namespace Tindarr.Contracts.Auth;

public sealed record AuthResponse(
	string AccessToken,
	DateTimeOffset ExpiresAtUtc,
	string UserId,
	string DisplayName,
	IReadOnlyList<string> Roles);

