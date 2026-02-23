namespace Tindarr.Infrastructure.Persistence.Entities;

public sealed class RegistrationSettingsEntity
{
	public long Id { get; set; }
	public bool? AllowOpenRegistration { get; set; }
	public bool? RequireAdminApprovalForNewUsers { get; set; }
	public string? DefaultRole { get; set; }
	public DateTimeOffset UpdatedAtUtc { get; set; }
}
