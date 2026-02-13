namespace Tindarr.Infrastructure.PlexCache.Entities;

public sealed class PlexLibraryCacheItemEntity
{
	public long Id { get; set; }

	public string ServerId { get; set; } = "";
	public int TmdbId { get; set; }

	public string? RatingKey { get; set; }
	public string? Title { get; set; }

	public DateTimeOffset UpdatedAtUtc { get; set; }
}
