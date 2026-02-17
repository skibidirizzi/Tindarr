using Tindarr.Domain.Common;

namespace Tindarr.Application.Interfaces.Playback;

public interface IPlaybackProvider
{
	ServiceType ServiceType { get; }

	/// <summary>
	/// Builds the upstream request for a movie stream.
	/// The returned request MUST contain provider credentials as headers (never in the returned URL).
	/// </summary>
	Task<UpstreamPlaybackRequest> BuildMovieStreamRequestAsync(ServiceScope scope, int tmdbId, CancellationToken cancellationToken);
}

/// <summary>
/// Optional extension for providers that can generate a direct-play URL suitable for cast devices.
/// Cast devices generally cannot send custom auth headers, so providers implementing this interface
/// must embed any required auth in the returned URL (e.g. querystring token).
/// </summary>
public interface IDirectPlaybackProvider : IPlaybackProvider
{
	Task<Uri?> TryBuildDirectMovieStreamUrlAsync(ServiceScope scope, int tmdbId, CancellationToken cancellationToken);
}

public sealed record UpstreamPlaybackRequest(
	Uri Uri,
	IReadOnlyDictionary<string, string> Headers);
