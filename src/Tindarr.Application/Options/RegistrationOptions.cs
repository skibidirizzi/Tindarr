namespace Tindarr.Application.Options;

public sealed class RegistrationOptions
{
	public const string SectionName = "Registration";

	/// <summary>
	/// If false, only Admins can create users (via admin endpoints).
	/// </summary>
	public bool AllowOpenRegistration { get; init; } = true;

	/// <summary>
	/// When true, new users created via the create-account flow get the PendingApproval role only
	/// and cannot log in until an admin approves them (e.g. sets their role to Contributor).
	/// </summary>
	public bool RequireAdminApprovalForNewUsers { get; init; }

	/// <summary>
	/// Default role assigned to newly registered users (and to users when an admin approves them).
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

