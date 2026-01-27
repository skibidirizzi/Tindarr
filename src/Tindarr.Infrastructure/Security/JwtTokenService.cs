using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Tindarr.Application.Abstractions.Security;
using Tindarr.Application.Options;

namespace Tindarr.Infrastructure.Security;

public sealed class JwtTokenService(IOptions<JwtOptions> jwtOptions, ITokenSigningKeyStore keyStore) : ITokenService
{
	private readonly JwtOptions options = jwtOptions.Value;
	private readonly JwtSecurityTokenHandler handler = new();

	public TokenResult IssueAccessToken(string userId, IReadOnlyCollection<string> roles)
	{
		var now = DateTimeOffset.UtcNow;
		var expires = now.AddMinutes(options.AccessTokenMinutes);

		var claims = new List<Claim>
		{
			new(JwtRegisteredClaimNames.Sub, userId),
			new(ClaimTypes.NameIdentifier, userId),
			new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
		};

		foreach (var role in roles.Distinct(StringComparer.OrdinalIgnoreCase))
		{
			claims.Add(new Claim(ClaimTypes.Role, role));
		}

		var active = keyStore.GetActiveSigningKey();
		var signingKey = new SymmetricSecurityKey(active.KeyMaterial) { KeyId = active.KeyId };
		var creds = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

		var token = new JwtSecurityToken(
			issuer: options.Issuer,
			audience: options.Audience,
			claims: claims,
			notBefore: now.UtcDateTime,
			expires: expires.UtcDateTime,
			signingCredentials: creds);

		// Rotation support: emit a "kid" header that matches the active signing key.
		token.Header["kid"] = keyStore.GetActiveKeyId();

		var jwt = handler.WriteToken(token);
		return new TokenResult(jwt, expires);
	}
}

