using System.Net;

namespace Tindarr.Application.Common;

public static class ReleaseNotesSanitizer
{
	/// <summary>
	/// Escapes release notes so they are safe to render in an HTML context.
	/// This defends against XSS if the UI later renders the string as HTML.
	/// </summary>
	public static string? EscapeHtml(string? releaseNotes)
	{
		if (string.IsNullOrEmpty(releaseNotes))
		{
			return releaseNotes;
		}

		return WebUtility.HtmlEncode(releaseNotes);
	}
}
