namespace Tindarr.Contracts.Casting;

public sealed record CastDeviceDto(
	string Id,
	string Name,
	string? Address,
	int Port);
