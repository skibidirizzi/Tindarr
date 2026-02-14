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

public sealed record UpstreamPlaybackRequest(
	Uri Uri,
	IReadOnlyDictionary<string, string> Headers);
