namespace Tindarr.Application.Options;

[Flags]
public enum OutgoingWebhookEvents
{
	None = 0,
	Likes = 1 << 0,
	Matches = 1 << 1,
	RoomCreated = 1 << 2,
	Login = 1 << 3,
	UserCreated = 1 << 4,
	AuthFailures = 1 << 5,
}

public sealed record OutgoingWebhookSettings(
	bool Enabled,
	IReadOnlyList<string> Urls,
	OutgoingWebhookEvents Events);
