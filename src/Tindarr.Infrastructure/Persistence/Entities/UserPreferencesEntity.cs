namespace Tindarr.Infrastructure.Persistence.Entities;

public sealed class UserPreferencesEntity
{
	public string UserId { get; set; } = "";
	public UserEntity User { get; set; } = null!;

	// Minimal, future-friendly preference columns.
	public bool IncludeAdult { get; set; } = false;
	public int? MinReleaseYear { get; set; }
	public int? MaxReleaseYear { get; set; }

	// Store lists as JSON until Contracts/Domain types are finalized.
	public string PreferredGenresJson { get; set; } = "[]";
	public string PreferredOriginalLanguagesJson { get; set; } = "[]";

	public DateTimeOffset UpdatedAtUtc { get; set; }
}

