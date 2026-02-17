using Tindarr.Application.Common;

namespace Tindarr.UnitTests.Application;

public sealed class GitHubReleaseTagVersionParserTests
{
	[Theory]
	[InlineData("0.5", 0, 5, 0)]
	[InlineData("v0.5", 0, 5, 0)]
	[InlineData("0.5.1", 0, 5, 1)]
	[InlineData("v0.5.1", 0, 5, 1)]
	[InlineData("v0.5.1-beta.2", 0, 5, 1)]
	[InlineData("0.5.1+build.7", 0, 5, 1)]
	[InlineData("  v1.2.3  ", 1, 2, 3)]
	public void TryParse_parses_common_github_tag_formats(string tag, int major, int minor, int patch)
	{
		var ok = GitHubReleaseTagVersionParser.TryParse(tag, out var version);
		Assert.True(ok);
		Assert.Equal(new Version(major, minor, patch), version);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData(" ")]
	[InlineData("release-0.5")]
	[InlineData("v")]
	[InlineData("v.")]
	[InlineData("v0")]
	public void TryParse_rejects_invalid_tags(string? tag)
	{
		var ok = GitHubReleaseTagVersionParser.TryParse(tag, out _);
		Assert.False(ok);
	}
}
