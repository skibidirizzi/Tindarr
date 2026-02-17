using Tindarr.Infrastructure.Playback.Hls;

namespace Tindarr.UnitTests.Playback;

public sealed class ManifestRewriterTests
{
	[Fact]
	public void Rewrite_Rewrites_Segment_Lines_And_Uri_Attributes()
	{
		var rewriter = new ManifestRewriter();
		var playlistUri = new Uri("https://upstream.example/hls/master.m3u8?foo=1", UriKind.Absolute);
		var playlist = "#EXTM3U\n" +
			"#EXT-X-VERSION:3\n" +
			"#EXT-X-KEY:METHOD=AES-128,URI=\"key.key\"\n" +
			"sub/variant.m3u8\n" +
			"#EXTINF:6.0,\n" +
			"seg-0001.ts\n";

		var rewritten = rewriter.Rewrite(playlist, playlistUri, u => "/gw?u=" + Uri.EscapeDataString(u.ToString()));

		Assert.Contains("URI=\"/gw?u=https%3A%2F%2Fupstream.example%2Fhls%2Fkey.key\"", rewritten);
		Assert.Contains("/gw?u=https%3A%2F%2Fupstream.example%2Fhls%2Fsub%2Fvariant.m3u8", rewritten);
		Assert.Contains("/gw?u=https%3A%2F%2Fupstream.example%2Fhls%2Fseg-0001.ts", rewritten);
	}

	[Fact]
	public void Rewrite_Does_Not_Rewrite_NonHttp_Uri_Attributes()
	{
		var rewriter = new ManifestRewriter();
		var playlistUri = new Uri("https://upstream.example/hls/master.m3u8", UriKind.Absolute);
		var playlist = "#EXTM3U\n#EXT-X-KEY:METHOD=SAMPLE-AES,URI=\"skd://license\"\n";

		var rewritten = rewriter.Rewrite(playlist, playlistUri, u => "SHOULD_NOT_HAPPEN");
		Assert.Contains("URI=\"skd://license\"", rewritten);
		Assert.DoesNotContain("SHOULD_NOT_HAPPEN", rewritten);
	}
}
