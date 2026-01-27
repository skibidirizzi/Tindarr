namespace Tindarr.Application.Options;

public sealed class JwtOptions
{
	public const string SectionName = "Jwt";

	/// <summary>
	/// Token issuer (iss). Example: "tindarr".
	/// </summary>
	public string Issuer { get; init; } = "tindarr";

	/// <summary>
	/// Token audience (aud). Example: "tindarr-client".
	/// </summary>
	public string Audience { get; init; } = "tindarr-client";

	/// <summary>
	/// Access token lifetime in minutes.
	/// </summary>
	public int AccessTokenMinutes { get; init; } = 60;

	/// <summary>
	/// File name (or absolute path) used to persist signing keys.
	/// If relative, it is resolved from the host base directory.
	/// </summary>
	public string SigningKeysFileName { get; init; } = "jwt-signing-keys.json";

	/// <summary>
	/// DEV ONLY: allow X-User-Id/X-User-Role header auth when no Bearer token is present.
	/// </summary>
	public bool AllowDevHeaderFallback { get; init; } = true;

	public bool IsValid()
	{
		if (string.IsNullOrWhiteSpace(Issuer) || string.IsNullOrWhiteSpace(Audience))
		{
			return false;
		}

		if (AccessTokenMinutes < 1 || AccessTokenMinutes > 24 * 60)
		{
			return false;
		}

		return !string.IsNullOrWhiteSpace(SigningKeysFileName);
	}
}

