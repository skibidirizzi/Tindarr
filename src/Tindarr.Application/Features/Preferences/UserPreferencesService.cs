using Tindarr.Application.Abstractions.Persistence;
using Tindarr.Application.Interfaces.Preferences;

namespace Tindarr.Application.Features.Preferences;

public sealed class UserPreferencesService(IUserPreferencesRepository repo) : IUserPreferencesService
{
	public async Task<UserPreferencesRecord> GetOrDefaultAsync(string userId, CancellationToken cancellationToken)
	{
		var existing = await repo.GetAsync(userId, cancellationToken);
		if (existing is not null)
		{
			return existing;
		}

		// Defaults mirror the entity defaults.
		return new UserPreferencesRecord(
			IncludeAdult: false,
			MinReleaseYear: null,
			MaxReleaseYear: null,
			MinRating: null,
			MaxRating: null,
			PreferredGenres: [],
			ExcludedGenres: [],
			PreferredOriginalLanguages: [],
			PreferredRegions: [],
			SortBy: "popularity.desc",
			UpdatedAtUtc: DateTimeOffset.UtcNow);
	}

	public async Task<UserPreferencesRecord> UpdateAsync(string userId, UserPreferencesUpsert upsert, CancellationToken cancellationToken)
	{
		var now = DateTimeOffset.UtcNow;
		await repo.UpsertAsync(userId, upsert, now, cancellationToken);

		return new UserPreferencesRecord(
			upsert.IncludeAdult,
			upsert.MinReleaseYear,
			upsert.MaxReleaseYear,
			upsert.MinRating,
			upsert.MaxRating,
			upsert.PreferredGenres.ToList(),
			upsert.ExcludedGenres.ToList(),
			upsert.PreferredOriginalLanguages.ToList(),
			upsert.PreferredRegions.ToList(),
			upsert.SortBy,
			now);
	}
}

