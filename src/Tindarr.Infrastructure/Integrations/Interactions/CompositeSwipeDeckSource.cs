using Tindarr.Application.Interfaces.Interactions;
using Tindarr.Domain.Common;
using Tindarr.Domain.Interactions;
using Tindarr.Infrastructure.Integrations.Emby;
using Tindarr.Infrastructure.Integrations.Jellyfin;
using Tindarr.Infrastructure.Integrations.Plex;
using Tindarr.Infrastructure.Integrations.Tmdb;

namespace Tindarr.Infrastructure.Integrations.Interactions;

public sealed class CompositeSwipeDeckSource(
	TmdbSwipeDeckSource tmdbSource,
	JellyfinSwipeDeckSource jellyfinSource,
	EmbySwipeDeckSource embySource,
	PlexSwipeDeckSource plexSource) : ISwipeDeckSource
{
	public Task<IReadOnlyList<SwipeCard>> GetCandidatesAsync(string userId, ServiceScope scope, CancellationToken cancellationToken)
	{
		return scope.ServiceType switch
		{
			ServiceType.Plex => plexSource.GetCandidatesAsync(userId, scope, cancellationToken),
			ServiceType.Jellyfin => jellyfinSource.GetCandidatesAsync(userId, scope, cancellationToken),
			ServiceType.Emby => embySource.GetCandidatesAsync(userId, scope, cancellationToken),
			_ => tmdbSource.GetCandidatesAsync(userId, scope, cancellationToken)
		};
	}
}
