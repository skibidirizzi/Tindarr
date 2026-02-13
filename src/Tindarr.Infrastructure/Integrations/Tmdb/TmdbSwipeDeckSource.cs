using Tindarr.Application.Abstractions.Integrations;
using Tindarr.Application.Interfaces.Interactions;
using Tindarr.Application.Interfaces.Preferences;
using Tindarr.Application.Options;
using Tindarr.Domain.Common;
using Tindarr.Domain.Interactions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Tindarr.Infrastructure.Integrations.Tmdb;

public sealed class TmdbSwipeDeckSource(
	ITmdbClient tmdbClient,
	IUserPreferencesService preferencesService,
	ITmdbMetadataStore metadataStore,
	IOptions<TmdbOptions> tmdbOptions,
	ILogger<TmdbSwipeDeckSource> logger) : ISwipeDeckSource
{
	public async Task<IReadOnlyList<SwipeCard>> GetCandidatesAsync(string userId, ServiceScope scope, CancellationToken cancellationToken)
	{
		// If TMDB isn't configured yet (public MSI install), fall back to the demo deck.
		if (!tmdbOptions.Value.HasCredentials)
		{
			return await new InMemorySwipeDeckSource().GetCandidatesAsync(userId, scope, cancellationToken).ConfigureAwait(false);
		}

		var sw = Stopwatch.StartNew();
		var prefs = await preferencesService.GetOrDefaultAsync(userId, cancellationToken).ConfigureAwait(false);

		// Prefer local pool for near-instant decks.
		var settings = await metadataStore.GetSettingsAsync(cancellationToken).ConfigureAwait(false);
		var pool = await metadataStore.GetUserPoolAsync(userId, limit: 50, cancellationToken).ConfigureAwait(false);

		// If pool is low, backfill it from TMDB discover (background jobs should usually keep it full).
		if (pool.Count < 25)
		{
			var discovered = await tmdbClient.DiscoverMoviesAsync(prefs, page: 1, limit: 50, cancellationToken).ConfigureAwait(false);
			await metadataStore.AddToUserPoolAsync(userId, discovered, cancellationToken).ConfigureAwait(false);
			pool = await metadataStore.GetUserPoolAsync(userId, limit: 50, cancellationToken).ConfigureAwait(false);
		}

		var cards = pool.Select(m => new SwipeCard(
			TmdbId: m.Id,
			Title: (m.Title ?? m.OriginalTitle ?? $"TMDB:{m.Id}").Trim(),
			Overview: m.Overview,
			PosterUrl: BuildImageUrl(settings, tmdbOptions.Value.ImageBaseUrl, m.PosterPath, tmdbOptions.Value.PosterSize),
			BackdropUrl: BuildImageUrl(settings, tmdbOptions.Value.ImageBaseUrl, m.BackdropPath, tmdbOptions.Value.BackdropSize),
			ReleaseYear: TryParseYear(m.ReleaseDate),
			Rating: m.VoteAverage)).ToList();

		sw.Stop();
		logger.LogDebug(
			"tmdb deck source produced candidates. Source={Source} ServiceType={ServiceType} ServerId={ServerId} Count={Count} ElapsedMs={ElapsedMs}",
			"local_pool",
			scope.ServiceType.ToString().ToLowerInvariant(),
			scope.ServerId,
			cards.Count,
			sw.ElapsedMilliseconds);

		return cards;
	}

	private static string? BuildImageUrl(TmdbMetadataSettings settings, string imageBaseUrl, string? path, string size)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return null;
		}

		var normalizedPath = path.Trim();
		if (normalizedPath.StartsWith('/'))
		{
			normalizedPath = normalizedPath.TrimStart('/');
		}

		var normalizedSize = (size ?? string.Empty).Trim().Trim('/');
		if (string.IsNullOrWhiteSpace(normalizedSize))
		{
			normalizedSize = "original";
		}

		if (settings.PosterMode == TmdbPosterMode.LocalProxy && settings.ImageCacheMaxMb > 0)
		{
			return $"/api/v1/tmdb/images/{normalizedSize}/{normalizedPath}";
		}

		var normalizedBase = (imageBaseUrl ?? string.Empty).TrimEnd('/');
		return $"{normalizedBase}/{normalizedSize}/{normalizedPath}";
	}

	private static int? TryParseYear(string? releaseDate)
	{
		if (string.IsNullOrWhiteSpace(releaseDate) || releaseDate.Length < 4)
		{
			return null;
		}

		return int.TryParse(releaseDate.AsSpan(0, 4), out var year) ? year : null;
	}
}

