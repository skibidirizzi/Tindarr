namespace Tindarr.Contracts.Casting;

public sealed record GetMovieCastUrlRequest(
	string ServiceType,
	string ServerId,
	int TmdbId,
	string? Title,
	string? DeviceId = null);
