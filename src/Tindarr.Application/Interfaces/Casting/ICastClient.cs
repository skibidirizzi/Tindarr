namespace Tindarr.Application.Interfaces.Casting;

public interface ICastClient
{
	Task<IReadOnlyList<CastDevice>> DiscoverAsync(CancellationToken cancellationToken);

	Task CastAsync(string deviceId, CastMedia media, CancellationToken cancellationToken);
}

public sealed record CastDevice(
	string Id,
	string Name,
	string? Address,
	int Port);

public sealed record CastMedia(
	string ContentUrl,
	string ContentType,
	string? Title,
	string? SubTitle);
