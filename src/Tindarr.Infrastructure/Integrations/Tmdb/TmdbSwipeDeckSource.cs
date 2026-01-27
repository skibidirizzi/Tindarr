using Tindarr.Application.Abstractions.Integrations;
using Tindarr.Application.Interfaces.Interactions;
using Tindarr.Application.Interfaces.Preferences;
using Tindarr.Application.Options;
using Tindarr.Domain.Common;
using Tindarr.Domain.Interactions;
using Microsoft.Extensions.Options;

namespace Tindarr.Infrastructure.Integrations.Tmdb;

public sealed class TmdbSwipeDeckSource(
	ITmdbClient tmdbClient,
	IUserPreferencesService preferencesService,
	IOptions<TmdbOptions> tmdbOptions) : ISwipeDeckSource
{
	public async Task<IReadOnlyList<SwipeCard>> GetCandidatesAsync(string userId, ServiceScope scope, CancellationToken cancellationToken)
	{
		// If TMDB isn't configured yet (public MSI install), fall back to the demo deck.
		if (!tmdbOptions.Value.HasCredentials)
		{
			return await new InMemorySwipeDeckSource().GetCandidatesAsync(userId, scope, cancellationToken).ConfigureAwait(false);
		}

		var prefs = await preferencesService.GetOrDefaultAsync(userId, cancellationToken).ConfigureAwait(false);

		// Pull a decent pool; the deck service will filter by seen items and apply the final limit.
		return await tmdbClient.DiscoverAsync(prefs, page: 1, limit: 50, cancellationToken).ConfigureAwait(false);
	}
}

