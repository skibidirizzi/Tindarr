namespace Tindarr.Contracts.Admin;

public sealed record AdminUpdateCheckResponse(
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
