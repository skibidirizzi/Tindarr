namespace Tindarr.Application.Abstractions.Persistence;

public interface IUserPreferencesRepository
{
	Task<UserPreferencesRecord?> GetAsync(string userId, CancellationToken cancellationToken);

	Task UpsertAsync(string userId, UserPreferencesUpsert upsert, DateTimeOffset updatedAtUtc, CancellationToken cancellationToken);
}

public sealed record UserPreferencesRecord(
	bool IncludeAdult,
	int? MinReleaseYear,
	int? MaxReleaseYear,
	double? MinRating,
	double? MaxRating,
	IReadOnlyList<int> PreferredGenres,
	IReadOnlyList<int> ExcludedGenres,
	IReadOnlyList<string> PreferredOriginalLanguages,
	IReadOnlyList<string> ExcludedOriginalLanguages,
	IReadOnlyList<string> PreferredRegions,
	IReadOnlyList<string> ExcludedRegions,
	string SortBy,
	DateTimeOffset UpdatedAtUtc);

public sealed record UserPreferencesUpsert(
	bool IncludeAdult,
	int? MinReleaseYear,
	int? MaxReleaseYear,
	double? MinRating,
	double? MaxRating,
	IReadOnlyList<int> PreferredGenres,
	IReadOnlyList<int> ExcludedGenres,
	IReadOnlyList<string> PreferredOriginalLanguages,
	IReadOnlyList<string> ExcludedOriginalLanguages,
	IReadOnlyList<string> PreferredRegions,
	IReadOnlyList<string> ExcludedRegions,
	string SortBy);

