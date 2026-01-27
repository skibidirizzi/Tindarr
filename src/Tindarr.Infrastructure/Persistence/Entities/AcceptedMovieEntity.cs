using Tindarr.Domain.Common;

namespace Tindarr.Infrastructure.Persistence.Entities;

public sealed class AcceptedMovieEntity
{
	public long Id { get; set; }

	public ServiceType ServiceType { get; set; }
	public string ServerId { get; set; } = "";

	public int TmdbId { get; set; }

	// Optional audit (who triggered acceptance).
	public string? AcceptedByUserId { get; set; }
	public DateTimeOffset AcceptedAtUtc { get; set; }
}

