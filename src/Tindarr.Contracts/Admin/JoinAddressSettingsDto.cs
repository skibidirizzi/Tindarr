namespace Tindarr.Contracts.Admin;

public sealed record JoinAddressSettingsDto(
	string? LanHostPort,
	string? WanHostPort,
	int? RoomLifetimeMinutes,
	int? GuestSessionLifetimeMinutes,
	string UpdatedAtUtc);
