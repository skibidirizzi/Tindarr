namespace Tindarr.Infrastructure.Persistence.Entities;

public sealed class JoinAddressSettingsEntity
{
	public long Id { get; set; }
	public string? LanHostPort { get; set; }
	public string? WanHostPort { get; set; }
	public DateTimeOffset UpdatedAtUtc { get; set; }
}
