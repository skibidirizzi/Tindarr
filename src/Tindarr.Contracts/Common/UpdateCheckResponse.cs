namespace Tindarr.Contracts.Common;

public sealed record UpdateCheckResponse(
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
