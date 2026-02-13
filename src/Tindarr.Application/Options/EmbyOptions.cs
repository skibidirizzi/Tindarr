namespace Tindarr.Application.Options;

public sealed class EmbyOptions
{
	public const string SectionName = "Emby";

	public int LibrarySyncMinutes { get; init; } = 60;

	public bool IsValid()
	{
		return LibrarySyncMinutes is >= 1 and <= 1440;
	}
}
