using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Tindarr.Application.Abstractions.Security;
using Tindarr.Application.Options;

namespace Tindarr.Api.Auth;

public sealed class ConfigureJwtBearerOptions(
	IOptions<JwtOptions> jwtOptions,
	ITokenSigningKeyStore signingKeyStore) : IConfigureNamedOptions<JwtBearerOptions>
{
	public void Configure(JwtBearerOptions options) => Configure(Options.DefaultName, options);

	public void Configure(string? name, JwtBearerOptions options)
	{
		if (!string.Equals(name, JwtBearerDefaults.AuthenticationScheme, StringComparison.Ordinal))
		{
			return;
		}

		var jwt = jwtOptions.Value;

		options.RequireHttpsMetadata = false;
		options.SaveToken = true;
		options.TokenValidationParameters = new TokenValidationParameters
		{
			ValidateIssuer = true,
			ValidIssuer = jwt.Issuer,
			ValidateAudience = true,
			ValidAudience = jwt.Audience,
			ValidateLifetime = true,
			ValidateIssuerSigningKey = true,
			IssuerSigningKeyResolver = (_, _, _, _) =>
				signingKeyStore.GetAllSigningKeys()
					.Select(k => (SecurityKey)new SymmetricSecurityKey(k.KeyMaterial) { KeyId = k.KeyId })
					.ToList()
		};
	}
}

