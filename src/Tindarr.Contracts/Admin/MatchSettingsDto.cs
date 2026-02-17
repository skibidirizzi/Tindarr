namespace Tindarr.Contracts.Admin;

public sealed record MatchSettingsDto(
	string ServiceType,
	string ServerId,
	int? MinUsers,
	int? MinUserPercent,
	string UpdatedAtUtc);
