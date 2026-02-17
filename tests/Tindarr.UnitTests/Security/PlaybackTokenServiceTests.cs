using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.Security.Cryptography;
using System.Text;
using Tindarr.Application.Abstractions.Security;
using Tindarr.Application.Options;
using Tindarr.Domain.Common;
using Tindarr.Infrastructure.Security;

namespace Tindarr.UnitTests.Security;

public sealed class PlaybackTokenServiceTests
{
	[Fact]
	public void Issue_And_Validate_Works_For_Same_Scope_And_TmdbId()
	{
		var keys = new[]
		{
			new SigningKey("k1", new byte[32] { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2 })
		};
		var store = new TestKeyStore("k1", keys);
		var opts = Options.Create(new PlaybackOptions { TokenMinutes = 10 });
		var svc = new PlaybackTokenService(store, opts);

		var scope = new ServiceScope(ServiceType.Plex, "server-1");
		var now = DateTimeOffset.UtcNow;
		var token = svc.IssueMovieToken(scope, 123, now);

		Assert.True(svc.TryValidateMovieToken(token, scope, 123, now.AddMinutes(1)));
	}

	[Fact]
	public void Validate_Fails_When_Expired()
	{
		var keys = new[]
		{
			new SigningKey("k1", new byte[32] { 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8 })
		};
		var store = new TestKeyStore("k1", keys);
		var opts = Options.Create(new PlaybackOptions { TokenMinutes = 1 });
		var svc = new PlaybackTokenService(store, opts);

		var scope = new ServiceScope(ServiceType.Jellyfin, "server-2");
		var now = DateTimeOffset.UtcNow;
		var token = svc.IssueMovieToken(scope, 555, now);

		Assert.False(svc.TryValidateMovieToken(token, scope, 555, now.AddMinutes(5)));
	}

	[Fact]
	public void Validate_Fails_When_Scope_Differs()
	{
		var keys = new[]
		{
			new SigningKey("k1", new byte[32] { 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4 })
		};
		var store = new TestKeyStore("k1", keys);
		var opts = Options.Create(new PlaybackOptions { TokenMinutes = 10 });
		var svc = new PlaybackTokenService(store, opts);

		var scope = new ServiceScope(ServiceType.Emby, "server-9");
		var now = DateTimeOffset.UtcNow;
		var token = svc.IssueMovieToken(scope, 42, now);

		var otherScope = new ServiceScope(ServiceType.Emby, "server-other");
		Assert.False(svc.TryValidateMovieToken(token, otherScope, 42, now.AddMinutes(1)));
	}

	[Fact]
	public void Validate_Succeeds_When_Kid_Is_Unknown_By_Trying_All_Keys()
	{
		var key1 = new SigningKey("k1", new byte[32] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32 });
		var key2 = new SigningKey("k2", new byte[32] { 32, 31, 30, 29, 28, 27, 26, 25, 24, 23, 22, 21, 20, 19, 18, 17, 16, 15, 14, 13, 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1 });
		var store = new TestKeyStore("k1", new[] { key1, key2 });
		var svc = new PlaybackTokenService(store, Options.Create(new PlaybackOptions { TokenMinutes = 10 }));

		var scope = new ServiceScope(ServiceType.Plex, "server-1");
		var now = DateTimeOffset.UtcNow;
		var exp = now.AddMinutes(10).ToUnixTimeSeconds();

		// kid intentionally unknown, but token is signed with k2.
		var payloadJson = $"{{\"kid\":\"unknown\",\"svc\":\"plex\",\"sid\":\"server-1\",\"tmdb\":123,\"exp\":{exp}}}";
		var token = BuildToken(payloadJson, key2.KeyMaterial);

		Assert.True(svc.TryValidateMovieToken(token, scope, 123, now.AddMinutes(1)));
	}

	[Fact]
	public void Validate_Succeeds_When_Kid_Is_Missing_By_Trying_All_Keys()
	{
		var key1 = new SigningKey("k1", new byte[32] { 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6 });
		var key2 = new SigningKey("k2", new byte[32] { 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8 });
		var store = new TestKeyStore("k1", new[] { key1, key2 });
		var svc = new PlaybackTokenService(store, Options.Create(new PlaybackOptions { TokenMinutes = 10 }));

		var scope = new ServiceScope(ServiceType.Emby, "server-9");
		var now = DateTimeOffset.UtcNow;
		var exp = now.AddMinutes(10).ToUnixTimeSeconds();

		// kid omitted (legacy token). Signed with k2.
		var payloadJson = $"{{\"svc\":\"emby\",\"sid\":\"server-9\",\"tmdb\":42,\"exp\":{exp}}}";
		var token = BuildToken(payloadJson, key2.KeyMaterial);

		Assert.True(svc.TryValidateMovieToken(token, scope, 42, now.AddMinutes(1)));
	}

	private static string BuildToken(string payloadJson, byte[] keyMaterial)
	{
		var payloadBytes = Encoding.UTF8.GetBytes(payloadJson);
		var payloadB64 = Base64UrlEncoder.Encode(payloadBytes);
		using var hmac = new HMACSHA256(keyMaterial);
		var sigBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(payloadB64));
		var sigB64 = Base64UrlEncoder.Encode(sigBytes);
		return $"{payloadB64}.{sigB64}";
	}

	private sealed class TestKeyStore(string activeKid, IReadOnlyCollection<SigningKey> keys) : ITokenSigningKeyStore
	{
		public SigningKey GetActiveSigningKey() => keys.First(k => k.KeyId == activeKid);
		public IReadOnlyCollection<SigningKey> GetAllSigningKeys() => keys;
		public string GetActiveKeyId() => activeKid;
	}
}
