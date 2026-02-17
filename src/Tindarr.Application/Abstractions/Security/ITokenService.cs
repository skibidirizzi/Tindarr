using System.Security.Claims;

namespace Tindarr.Application.Abstractions.Security;

public interface ITokenService
{
	TokenResult IssueAccessToken(
		string userId,
		IReadOnlyCollection<string> roles,
		IReadOnlyCollection<Claim>? additionalClaims = null,
		TimeSpan? lifetimeOverride = null);
}

public sealed record TokenResult(string AccessToken, DateTimeOffset ExpiresAtUtc);

public static class TindarrClaimTypes
{
	public const string RoomId = "tindarr:roomId";
	public const string DisplayName = "tindarr:displayName";
	public const string IsGuest = "tindarr:isGuest";
}

