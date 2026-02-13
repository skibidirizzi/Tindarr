namespace Tindarr.Application.Options;

public sealed class JellyfinOptions
{
	public const string SectionName = "Jellyfin";

	public int LibrarySyncMinutes { get; init; } = 60;

	public bool IsValid()
	{
		return LibrarySyncMinutes is >= 1 and <= 1440;
	}
}
