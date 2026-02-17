namespace Tindarr.Contracts.Admin;

public sealed record UpdateMatchSettingsRequest(
	int? MinUsers,
	int? MinUserPercent);
