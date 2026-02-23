namespace Tindarr.Contracts.Admin;

/// <summary>
/// Registration-related settings (from config). Used by Admin UI to display and optionally edit.
/// </summary>
public sealed record RegistrationSettingsDto(
	bool AllowOpenRegistration,
	bool RequireAdminApprovalForNewUsers,
	string DefaultRole);
