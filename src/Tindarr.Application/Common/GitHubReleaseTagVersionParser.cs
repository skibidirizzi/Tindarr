namespace Tindarr.Application.Common;

public static class GitHubReleaseTagVersionParser
{
	public static bool TryParse(string? tagName, out Version version)
	{
		version = new Version(0, 0, 0);
		if (string.IsNullOrWhiteSpace(tagName))
		{
			return false;
		}

		var s = tagName.Trim();
		if (s.StartsWith('v') || s.StartsWith('V'))
		{
			s = s[1..];
		}

		// Drop prerelease/build metadata (e.g. 1.2.3-beta.1 or 1.2.3+build.7)
		var dash = s.IndexOf('-');
		if (dash >= 0)
		{
			s = s[..dash];
		}
		var plus = s.IndexOf('+');
		if (plus >= 0)
		{
			s = s[..plus];
		}

		s = s.Trim();
		if (string.IsNullOrWhiteSpace(s))
		{
			return false;
		}

		// Support tags like "0.5" by normalizing to "0.5.0".
		var dotCount = 0;
		foreach (var ch in s)
		{
			if (ch == '.') dotCount++;
		}
		if (dotCount == 1)
		{
			s += ".0";
		}

		if (!Version.TryParse(s, out var parsed))
		{
			return false;
		}

		version = parsed;
		return true;
	}
}
