namespace Tindarr.Application.Abstractions.Persistence;

public interface IRegistrationSettingsRepository
{
	Task<RegistrationSettingsRecord?> GetAsync(CancellationToken cancellationToken);

	Task<RegistrationSettingsRecord> UpsertAsync(RegistrationSettingsUpsert upsert, CancellationToken cancellationToken);
}

public sealed record RegistrationSettingsRecord(
	bool? AllowOpenRegistration,
	bool? RequireAdminApprovalForNewUsers,
	string? DefaultRole,
	DateTimeOffset UpdatedAtUtc);

public sealed record RegistrationSettingsUpsert(
	bool? AllowOpenRegistration,
	bool? RequireAdminApprovalForNewUsers,
	string? DefaultRole);
