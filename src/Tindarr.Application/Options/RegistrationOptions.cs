namespace Tindarr.Application.Options;

public sealed class RegistrationOptions
{
	public const string SectionName = "Registration";

	/// <summary>
	/// If false, only Admins can create users (via admin endpoints).
	/// </summary>
	public bool AllowOpenRegistration { get; init; } = true;

	/// <summary>
	/// Default role assigned to newly registered users.
	/// </summary>
	public string DefaultRole { get; init; } = "Contributor";

	/// <summary>
	/// PBKDF2 iteration count for password hashing (PBKDF2-SHA256).
	/// </summary>
	public int PasswordHashIterations { get; init; } = 100_000;

	public bool IsValid()
	{
		if (PasswordHashIterations < 10_000 || PasswordHashIterations > 10_000_000)
		{
			return false;
		}

		return !string.IsNullOrWhiteSpace(DefaultRole);
	}
}

