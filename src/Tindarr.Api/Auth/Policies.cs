namespace Tindarr.Api.Auth;

public static class Policies
{
	public const string AdminOnly = "AdminOnly";
	public const string CuratorOnly = "CuratorOnly";
	public const string ContributorOrCurator = "ContributorOrCurator";

	public const string AdminRole = "Admin";
	public const string CuratorRole = "Curator";
	public const string ContributorRole = "Contributor";
}
