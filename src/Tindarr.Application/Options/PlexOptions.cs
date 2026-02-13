namespace Tindarr.Application.Options;

public sealed class PlexOptions
{
	public const string SectionName = "Plex";

	public int LibrarySyncMinutes { get; init; } = 15;

	public int EnrichmentConcurrency { get; init; } = 4;

	public string Product { get; init; } = "Tindarr";

	public string Platform { get; init; } = "Tindarr";

	public string Device { get; init; } = "Tindarr";

	public string Version { get; init; } = "1.0";

	// Optional shared secret for Plex webhook ingestion.
	// If set, callers must provide it via X-Tindarr-Webhook-Token header or ?token= query.
	public string WebhookToken { get; init; } = "";

	public bool IsValid()
	{
		return LibrarySyncMinutes is >= 1 and <= 1440
			&& EnrichmentConcurrency is >= 1 and <= 32
			&& !string.IsNullOrWhiteSpace(Product)
			&& !string.IsNullOrWhiteSpace(Platform)
			&& !string.IsNullOrWhiteSpace(Device)
			&& !string.IsNullOrWhiteSpace(Version);
	}
}
