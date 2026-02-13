namespace Tindarr.Contracts.Admin;

public sealed record JoinAddressSettingsDto(
	string? LanHostPort,
	string? WanHostPort,
	string UpdatedAtUtc);
