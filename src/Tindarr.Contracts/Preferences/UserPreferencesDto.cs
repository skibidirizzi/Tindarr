namespace Tindarr.Contracts.Preferences;

public sealed record UserPreferencesDto(
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

