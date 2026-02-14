namespace Tindarr.Infrastructure.PlexCache.Entities;

public sealed class PlexDeckEntryEntity
{
	public int Id { get; set; }
	public string ServerId { get; set; } = string.Empty;
	public int TmdbId { get; set; }

	public string Title { get; set; } = string.Empty;
	public string? Overview { get; set; }
	public string? PosterUrl { get; set; }
	public string? BackdropUrl { get; set; }

	public int? ReleaseYear { get; set; }
	public string? MpaaRating { get; set; }
	public double? Rating { get; set; }
	public int? VoteCount { get; set; }
	public string? OriginalLanguage { get; set; }
	public int? RuntimeMinutes { get; set; }

	public bool IsAdult { get; set; }
	public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class PlexDeckGenreEntity
{
	public int Id { get; set; }
	public string ServerId { get; set; } = string.Empty;
	public int TmdbId { get; set; }
	public string Genre { get; set; } = string.Empty;
}

public sealed class PlexDeckRegionEntity
{
	public int Id { get; set; }
	public string ServerId { get; set; } = string.Empty;
	public int TmdbId { get; set; }
	public string Region { get; set; } = string.Empty;
}
