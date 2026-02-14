using Tindarr.Domain.Common;

namespace Tindarr.Application.Abstractions.Security;

public interface IPlaybackTokenService
{
	string IssueMovieToken(ServiceScope scope, int tmdbId, DateTimeOffset nowUtc);

	bool TryValidateMovieToken(string token, ServiceScope scope, int tmdbId, DateTimeOffset nowUtc);
}
