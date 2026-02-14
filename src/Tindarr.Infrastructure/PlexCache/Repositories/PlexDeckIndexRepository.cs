using Microsoft.EntityFrameworkCore;
using Tindarr.Application.Abstractions.Persistence;
using Tindarr.Contracts.Movies;
using Tindarr.Domain.Common;
using Tindarr.Domain.Interactions;
using Tindarr.Infrastructure.PlexCache.Entities;

namespace Tindarr.Infrastructure.PlexCache.Repositories;

public sealed class PlexDeckIndexRepository(PlexCacheDbContext db) : IPlexDeckIndexRepository
{
	public async Task UpsertAsync(ServiceScope scope, IReadOnlyCollection<MovieDetailsDto> details, DateTimeOffset updatedAtUtc, CancellationToken cancellationToken)
	{
		if (scope.ServiceType != ServiceType.Plex)
		{
			return;
		}
		if (details.Count == 0)
		{
			return;
		}

		var serverId = scope.ServerId;
		var tmdbIds = details.Where(d => d.TmdbId > 0).Select(d => d.TmdbId).Distinct().ToList();
		if (tmdbIds.Count == 0)
		{
			return;
		}

		// SQLite has a relatively small parameter limit; chunk deletes.
		const int chunkSize = 400;
		await using var tx = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			for (var i = 0; i < tmdbIds.Count; i += chunkSize)
			{
				var chunk = tmdbIds.Skip(i).Take(chunkSize).ToList();

				await db.DeckGenres
					.Where(x => x.ServerId == serverId && chunk.Contains(x.TmdbId))
					.ExecuteDeleteAsync(cancellationToken)
					.ConfigureAwait(false);

				await db.DeckRegions
					.Where(x => x.ServerId == serverId && chunk.Contains(x.TmdbId))
					.ExecuteDeleteAsync(cancellationToken)
					.ConfigureAwait(false);

				await db.DeckEntries
					.Where(x => x.ServerId == serverId && chunk.Contains(x.TmdbId))
					.ExecuteDeleteAsync(cancellationToken)
					.ConfigureAwait(false);
			}

			var entryRows = new List<PlexDeckEntryEntity>(details.Count);
			var genreRows = new List<PlexDeckGenreEntity>();
			var regionRows = new List<PlexDeckRegionEntity>();

			foreach (var d in details)
			{
				if (d.TmdbId <= 0)
				{
					continue;
				}

				var isAdult = IsAdultRating(d.MpaaRating);
				entryRows.Add(new PlexDeckEntryEntity
				{
					ServerId = serverId,
					TmdbId = d.TmdbId,
					Title = (d.Title ?? string.Empty).Trim(),
					Overview = d.Overview,
					PosterUrl = d.PosterUrl,
					BackdropUrl = d.BackdropUrl,
					ReleaseYear = d.ReleaseYear,
					MpaaRating = string.IsNullOrWhiteSpace(d.MpaaRating) ? null : d.MpaaRating.Trim(),
					Rating = d.Rating,
					VoteCount = d.VoteCount,
					OriginalLanguage = string.IsNullOrWhiteSpace(d.OriginalLanguage) ? null : d.OriginalLanguage.Trim(),
					RuntimeMinutes = d.RuntimeMinutes,
					IsAdult = isAdult,
					UpdatedAtUtc = updatedAtUtc
				});

				if (d.Genres is { Count: > 0 })
				{
					foreach (var g in d.Genres)
					{
						var genre = (g ?? string.Empty).Trim();
						if (genre.Length == 0)
						{
							continue;
						}
						genreRows.Add(new PlexDeckGenreEntity { ServerId = serverId, TmdbId = d.TmdbId, Genre = genre });
					}
				}

				if (d.Regions is { Count: > 0 })
				{
					foreach (var r in d.Regions)
					{
						var region = (r ?? string.Empty).Trim();
						if (region.Length == 0)
						{
							continue;
						}
						regionRows.Add(new PlexDeckRegionEntity { ServerId = serverId, TmdbId = d.TmdbId, Region = region });
					}
				}
			}

			if (entryRows.Count == 0)
			{
				await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
				return;
			}

			db.DeckEntries.AddRange(entryRows);
			if (genreRows.Count > 0)
			{
				db.DeckGenres.AddRange(genreRows);
			}
			if (regionRows.Count > 0)
			{
				db.DeckRegions.AddRange(regionRows);
			}

			await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
			await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
		}
		catch
		{
			await tx.RollbackAsync(cancellationToken).ConfigureAwait(false);
			throw;
		}
	}

	public async Task<IReadOnlyList<SwipeCard>> QueryAsync(ServiceScope scope, UserPreferencesRecord preferences, int limit, CancellationToken cancellationToken)
	{
		if (scope.ServiceType != ServiceType.Plex)
		{
			return [];
		}
		limit = Math.Clamp(limit, 1, 500);

	var serverId = scope.ServerId;
	var q = db.DeckEntries.AsNoTracking().Where(x => x.ServerId == serverId);

		if (preferences.MinReleaseYear is { } minYear)
		{
			q = q.Where(x => x.ReleaseYear != null && x.ReleaseYear.Value >= minYear);
		}

		if (preferences.MaxReleaseYear is { } maxYear)
		{
			q = q.Where(x => x.ReleaseYear != null && x.ReleaseYear.Value <= maxYear);
		}

		if (preferences.MinRating is { } minRating)
		{
			q = q.Where(x => x.Rating != null && x.Rating.Value >= minRating);
		}

		if (preferences.MaxRating is { } maxRating)
		{
			q = q.Where(x => x.Rating != null && x.Rating.Value <= maxRating);
		}

		if (!preferences.IncludeAdult)
		{
			q = q.Where(x => !x.IsAdult);
		}

		if (preferences.PreferredOriginalLanguages is { Count: > 0 })
		{
			var langs = preferences.PreferredOriginalLanguages.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim().ToLowerInvariant()).Distinct().ToList();
			if (langs.Count > 0)
			{
				q = q.Where(x => x.OriginalLanguage != null && langs.Contains(x.OriginalLanguage.ToLower()));
			}
		}

		if (preferences.ExcludedOriginalLanguages is { Count: > 0 })
		{
			var langs = preferences.ExcludedOriginalLanguages.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim().ToLowerInvariant()).Distinct().ToList();
			if (langs.Count > 0)
			{
				q = q.Where(x => x.OriginalLanguage == null || !langs.Contains(x.OriginalLanguage.ToLower()));
			}
		}

		// Genre filtering: preferences store TMDB genre IDs, but details store genre names.
		// Map IDs -> names using the same hardcoded map from PlexService.
		var preferredGenreNames = MapGenreNames(preferences.PreferredGenres);
		if (preferredGenreNames.Count > 0)
		{
			q = q.Where(e => db.DeckGenres.AsNoTracking().Any(g => g.ServerId == serverId && g.TmdbId == e.TmdbId && preferredGenreNames.Contains(g.Genre)));
		}

		var excludedGenreNames = MapGenreNames(preferences.ExcludedGenres);
		if (excludedGenreNames.Count > 0)
		{
			q = q.Where(e => !db.DeckGenres.AsNoTracking().Any(g => g.ServerId == serverId && g.TmdbId == e.TmdbId && excludedGenreNames.Contains(g.Genre)));
		}

		if (preferences.PreferredRegions is { Count: > 0 })
		{
			var regions = preferences.PreferredRegions.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct().ToList();
			if (regions.Count > 0)
			{
				q = q.Where(e => db.DeckRegions.AsNoTracking().Any(r => r.ServerId == serverId && r.TmdbId == e.TmdbId && regions.Contains(r.Region)));
			}
		}

		if (preferences.ExcludedRegions is { Count: > 0 })
		{
			var regions = preferences.ExcludedRegions.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct().ToList();
			if (regions.Count > 0)
			{
				q = q.Where(e => !db.DeckRegions.AsNoTracking().Any(r => r.ServerId == serverId && r.TmdbId == e.TmdbId && regions.Contains(r.Region)));
			}
		}

		var rows = await q
			.OrderByDescending(x => x.UpdatedAtUtc)
			.ThenBy(x => x.TmdbId)
			.Take(limit)
			.Select(x => new SwipeCard(
				x.TmdbId,
				x.Title,
				x.Overview,
				x.PosterUrl,
				x.BackdropUrl,
				x.ReleaseYear,
				x.Rating))
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);

		return rows;
	}

	private static bool IsAdultRating(string? mpaa)
	{
		if (string.IsNullOrWhiteSpace(mpaa))
		{
			return false;
		}

		var v = mpaa.Trim();
		return v.Equals("NC-17", StringComparison.OrdinalIgnoreCase)
			|| v.Equals("X", StringComparison.OrdinalIgnoreCase)
			|| v.Equals("XXX", StringComparison.OrdinalIgnoreCase)
			|| v.Equals("TV-MA", StringComparison.OrdinalIgnoreCase);
	}

	private static HashSet<string> MapGenreNames(IReadOnlyList<int> genreIds)
	{
		if (genreIds is not { Count: > 0 })
		{
			return new HashSet<string>(StringComparer.Ordinal);
		}

		// Keep in sync with PlexService.TmdbGenreMap.
		static string? Map(int id) => id switch
		{
			28 => "Action",
			12 => "Adventure",
			16 => "Animation",
			35 => "Comedy",
			80 => "Crime",
			99 => "Documentary",
			18 => "Drama",
			10751 => "Family",
			14 => "Fantasy",
			36 => "History",
			27 => "Horror",
			10402 => "Music",
			9648 => "Mystery",
			10749 => "Romance",
			878 => "Science Fiction",
			10770 => "TV Movie",
			53 => "Thriller",
			10752 => "War",
			37 => "Western",
			_ => null
		};

		var set = new HashSet<string>(StringComparer.Ordinal);
		foreach (var id in genreIds)
		{
			var name = Map(id);
			if (!string.IsNullOrWhiteSpace(name))
			{
				set.Add(name);
			}
		}
		return set;
	}
}
