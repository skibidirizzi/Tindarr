namespace Tindarr.Infrastructure.Persistence.Entities;

public sealed class UserPreferencesEntity
{
	public string UserId { get; set; } = "";
	public UserEntity User { get; set; } = null!;

	// Minimal, future-friendly preference columns.
	public bool IncludeAdult { get; set; } = false;
	public int? MinReleaseYear { get; set; }
	public int? MaxReleaseYear { get; set; }

	public double? MinRating { get; set; }
	public double? MaxRating { get; set; }

	// Store lists as JSON until Contracts/Domain types are finalized.
	public string PreferredGenresJson { get; set; } = "[]";
	public string ExcludedGenresJson { get; set; } = "[]";
	public string PreferredOriginalLanguagesJson { get; set; } = "[]";
	public string ExcludedOriginalLanguagesJson { get; set; } = "[]";
	public string PreferredRegionsJson { get; set; } = "[]";
	public string ExcludedRegionsJson { get; set; } = "[]";

	// TMDB sort key (e.g. "popularity.desc", "vote_average.desc").
	public string SortBy { get; set; } = "popularity.desc";

	public DateTimeOffset UpdatedAtUtc { get; set; }
}

