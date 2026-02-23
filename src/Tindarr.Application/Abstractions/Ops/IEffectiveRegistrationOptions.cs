namespace Tindarr.Application.Abstractions.Ops;

/// <summary>
/// Provides effective registration settings (DB overrides merged with config defaults).
/// Used by auth and admin; invalidate after updating via Admin API.
/// </summary>
public interface IEffectiveRegistrationOptions
{
	bool AllowOpenRegistration { get; }

	bool RequireAdminApprovalForNewUsers { get; }

	string DefaultRole { get; }

	void Invalidate();
}
