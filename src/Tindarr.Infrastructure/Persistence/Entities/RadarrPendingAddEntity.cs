using Tindarr.Domain.Common;

namespace Tindarr.Infrastructure.Persistence.Entities;

public sealed class RadarrPendingAddEntity
{
	public long Id { get; set; }

	public ServiceType ServiceType { get; set; }
	public string ServerId { get; set; } = "";
	public string UserId { get; set; } = "";
	public int TmdbId { get; set; }

	public DateTimeOffset ReadyAtUtc { get; set; }
	public DateTimeOffset CreatedAtUtc { get; set; }
	public DateTimeOffset? CanceledAtUtc { get; set; }
	public DateTimeOffset? ProcessedAtUtc { get; set; }

	public int AttemptCount { get; set; }
	public string? LastError { get; set; }
}
