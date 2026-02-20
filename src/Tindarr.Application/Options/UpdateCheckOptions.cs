namespace Tindarr.Application.Options;

public sealed class UpdateCheckOptions
{
	public const string SectionName = "UpdateCheck";

	/// <summary>
	/// GitHub repository owner/org to check for releases.
	/// </summary>
	public string RepositoryOwner { get; init; } = "skibidirizzi";

	/// <summary>
	/// GitHub repository name to check for releases.
	/// </summary>
	public string RepositoryName { get; init; } = "Tindarr";

	/// <summary>
	/// When true, allow prerelease versions to be considered the latest.
	/// </summary>
	public bool IncludePreReleases { get; init; } = false;

	/// <summary>
	/// Cache duration for update checks to avoid GitHub rate limiting.
	/// </summary>
	public int CacheMinutes { get; init; } = 15;

	public bool IsValid()
		=> !string.IsNullOrWhiteSpace(RepositoryOwner)
		&& !string.IsNullOrWhiteSpace(RepositoryName)
		&& CacheMinutes is >= 0 and <= 24 * 60;
}
