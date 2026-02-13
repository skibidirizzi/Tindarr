namespace Tindarr.Application.Abstractions.Persistence;

public interface IJoinAddressSettingsRepository
{
	Task<JoinAddressSettingsRecord?> GetAsync(CancellationToken cancellationToken);

	Task<JoinAddressSettingsRecord> UpsertAsync(JoinAddressSettingsUpsert upsert, CancellationToken cancellationToken);
}

public sealed record JoinAddressSettingsRecord(
	string? LanHostPort,
	string? WanHostPort,
	DateTimeOffset UpdatedAtUtc);

public sealed record JoinAddressSettingsUpsert(
	string? LanHostPort,
	string? WanHostPort);
