namespace Tindarr.Contracts.Admin;

public sealed record UpdateJoinAddressSettingsRequest(
	string? LanHostPort,
	string? WanHostPort);
