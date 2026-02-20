namespace Tindarr.Infrastructure.EmbyCache.Entities;

public sealed class EmbyLibraryCacheItemEntity
{
	public long Id { get; set; }

	public string ServerId { get; set; } = "";
	public int TmdbId { get; set; }

	public string? ItemId { get; set; }
	public string? Title { get; set; }

	public DateTimeOffset UpdatedAtUtc { get; set; }
}
