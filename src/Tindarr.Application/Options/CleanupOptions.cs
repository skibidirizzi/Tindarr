namespace Tindarr.Application.Options;

public sealed class CleanupOptions
{
	public const string SectionName = "Cleanup";

	public bool Enabled { get; init; } = true;

	// How often the cleanup worker should run.
	public TimeSpan Interval { get; init; } = TimeSpan.FromHours(6);

	public bool PurgeGuestUsers { get; init; } = true;

	// Guest users older than this age are purged.
	public TimeSpan GuestUserMaxAge { get; init; } = TimeSpan.FromDays(1);

	public bool IsValid()
	{
		if (!Enabled)
		{
			return true;
		}

		if (Interval <= TimeSpan.Zero)
		{
			return false;
		}

		if (PurgeGuestUsers && GuestUserMaxAge <= TimeSpan.Zero)
		{
			return false;
		}

		return true;
	}
}
