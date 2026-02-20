using Tindarr.Application.Common;

namespace Tindarr.UnitTests.Application;

public sealed class ReleaseNotesSanitizerTests
{
	[Fact]
	public void EscapeHtml_with_null_returns_null()
	{
		Assert.Null(ReleaseNotesSanitizer.EscapeHtml(null));
	}

	[Theory]
	[InlineData("")]
	[InlineData("\n")]
	public void EscapeHtml_with_empty_or_whitespace_is_returned_unchanged(string value)
	{
		Assert.Equal(value, ReleaseNotesSanitizer.EscapeHtml(value));
	}

	[Fact]
	public void EscapeHtml_escapes_angle_brackets_and_ampersands()
	{
		var input = "<script>alert(1)</script>";
		var escaped = ReleaseNotesSanitizer.EscapeHtml(input);

		Assert.Equal("&lt;script&gt;alert(1)&lt;/script&gt;", escaped);
		Assert.DoesNotContain("<script>", escaped, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void EscapeHtml_prevents_entity_decoding_bypass()
	{
		// If a UI uses innerHTML, a raw '&lt;script&gt;' could be interpreted during parsing.
		// Encoding '&' prevents this from becoming a tag.
		var input = "&lt;script&gt;alert(1)&lt;/script&gt;";
		var escaped = ReleaseNotesSanitizer.EscapeHtml(input);

		Assert.Equal("&amp;lt;script&amp;gt;alert(1)&amp;lt;/script&amp;gt;", escaped);
	}
}
