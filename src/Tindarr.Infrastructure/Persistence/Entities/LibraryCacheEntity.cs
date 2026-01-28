using Tindarr.Domain.Common;

namespace Tindarr.Infrastructure.Persistence.Entities;

public sealed class LibraryCacheEntity
{
	public long Id { get; set; }

	public ServiceType ServiceType { get; set; }
	public string ServerId { get; set; } = "";

	public int TmdbId { get; set; }
	public DateTimeOffset SyncedAtUtc { get; set; }
}
