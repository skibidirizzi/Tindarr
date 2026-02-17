using System.Text;
using System.Text.RegularExpressions;

namespace Tindarr.Infrastructure.Playback.Hls;

public sealed class ManifestRewriter
{
	private static readonly Regex UriAttributeRegex = new(
		"URI=\"(?<uri>[^\"]+)\"",
		RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

	/// <summary>
	/// Rewrites a HLS playlist (m3u8) so that all sub-resource URLs point back to the Tindarr gateway.
	/// </summary>
	public string Rewrite(string playlistText, Uri playlistUri, Func<Uri, string> toGatewayUrl)
	{
		if (playlistText is null)
		{
			throw new ArgumentNullException(nameof(playlistText));
		}
		if (playlistUri is null)
		{
			throw new ArgumentNullException(nameof(playlistUri));
		}
		if (toGatewayUrl is null)
		{
			throw new ArgumentNullException(nameof(toGatewayUrl));
		}

		// HLS is line-oriented; preserve line endings as \n for consistency.
		var lines = playlistText
			.Replace("\r\n", "\n", StringComparison.Ordinal)
			.Replace("\r", "\n", StringComparison.Ordinal)
			.Split('\n');

		var sb = new StringBuilder(playlistText.Length + 256);
		for (var i = 0; i < lines.Length; i++)
		{
			var line = lines[i];
			var rewritten = RewriteLine(line, playlistUri, toGatewayUrl);
			if (i > 0)
			{
				sb.Append('\n');
			}
			sb.Append(rewritten);
		}

		return sb.ToString();
	}

	private static string RewriteLine(string line, Uri playlistUri, Func<Uri, string> toGatewayUrl)
	{
		if (string.IsNullOrWhiteSpace(line))
		{
			return line;
		}

		// Comment / tag lines can contain embedded URIs.
		if (line.StartsWith('#'))
		{
			return UriAttributeRegex.Replace(line, match =>
			{
				var uriText = match.Groups["uri"].Value;
				if (string.IsNullOrWhiteSpace(uriText))
				{
					return match.Value;
				}

				if (!TryResolveHttpUri(playlistUri, uriText, out var resolved))
				{
					return match.Value;
				}

				var gateway = toGatewayUrl(resolved);
				return $"URI=\"{gateway}\"";
			});
		}

		// Resource URI line (segment, nested playlist, etc.)
		if (!TryResolveHttpUri(playlistUri, line.Trim(), out var resourceUri))
		{
			return line;
		}

		return toGatewayUrl(resourceUri);
	}

	private static bool TryResolveHttpUri(Uri playlistUri, string uriText, out Uri resolved)
	{
		resolved = null!;

		if (!Uri.TryCreate(uriText, UriKind.RelativeOrAbsolute, out var parsed) || parsed is null)
		{
			return false;
		}

		if (!parsed.IsAbsoluteUri)
		{
			resolved = new Uri(playlistUri, parsed);
			return resolved.Scheme is "http" or "https";
		}

		if (parsed.Scheme is not ("http" or "https"))
		{
			return false;
		}

		resolved = parsed;
		return true;
	}
}

