namespace Tindarr.Application.Abstractions.Security;

public interface ITokenService
{
	TokenResult IssueAccessToken(string userId, IReadOnlyCollection<string> roles);
}

public sealed record TokenResult(string AccessToken, DateTimeOffset ExpiresAtUtc);

