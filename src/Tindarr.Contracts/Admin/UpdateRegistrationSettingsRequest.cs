namespace Tindarr.Contracts.Admin;

public sealed record UpdateRegistrationSettingsRequest(
	bool AllowOpenRegistration,
	bool RequireAdminApprovalForNewUsers,
	string? DefaultRole);
