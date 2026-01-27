using Tindarr.Domain.Common;
using Tindarr.Domain.Interactions;

namespace Tindarr.Infrastructure.Persistence.Entities;

public sealed class InteractionEntity
{
	public long Id { get; set; }

	public string UserId { get; set; } = "";
	public ServiceType ServiceType { get; set; }
	public string ServerId { get; set; } = "";

	public int TmdbId { get; set; }
	public InteractionAction Action { get; set; }
	public DateTimeOffset CreatedAtUtc { get; set; }
}

