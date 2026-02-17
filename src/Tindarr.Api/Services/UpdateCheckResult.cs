namespace Tindarr.Api.Services;

public sealed record UpdateCheckResult(
	string CurrentVersion,
	string? LatestVersion,
	bool UpdateAvailable,
	string CheckedAtUtc,
	string? LatestReleaseUrl,
	string? LatestReleaseName,
	string? PublishedAtUtc,
	bool? IsPreRelease,
	string? ReleaseNotes,
	string? Error);
