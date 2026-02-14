namespace Tindarr.Application.Options;

public sealed class PlaybackOptions
{
	public const string SectionName = "Playback";

	/// <summary>
	/// How long a playback token is valid for.
	/// Cast devices cannot send auth headers, so the token is embedded in the URL.
	/// </summary>
	public int TokenMinutes { get; init; } = 10;

	public bool IsValid()
	{
		return TokenMinutes is >= 1 and <= 60;
	}
}
