namespace Tindarr.Contracts.Playback;

public sealed record PreparePlaybackResponse(
	string ContentUrl,
	string ContentType,
	long ExpiresAtUnixSeconds);
