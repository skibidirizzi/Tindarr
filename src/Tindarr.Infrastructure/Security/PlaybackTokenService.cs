using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Tindarr.Application.Abstractions.Security;
using Tindarr.Application.Options;
using Tindarr.Domain.Common;

namespace Tindarr.Infrastructure.Security;

public sealed class PlaybackTokenService(
	ITokenSigningKeyStore keyStore,
	IOptions<PlaybackOptions> options) : IPlaybackTokenService
{
	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
	private readonly PlaybackOptions _options = options.Value;

	public string IssueMovieToken(ServiceScope scope, int tmdbId, DateTimeOffset nowUtc)
	{
		if (tmdbId <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(tmdbId), "tmdbId must be positive.");
		}

		var exp = nowUtc.AddMinutes(Math.Clamp(_options.TokenMinutes, 1, 60));
		var payload = new PlaybackTokenPayload(
			Kid: keyStore.GetActiveKeyId(),
			Svc: scope.ServiceType.ToString().ToLowerInvariant(),
			Sid: scope.ServerId,
			Tmdb: tmdbId,
			Exp: exp.ToUnixTimeSeconds());

		var payloadBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, Json));
		var payloadB64 = Base64UrlEncoder.Encode(payloadBytes);

		var active = keyStore.GetActiveSigningKey();
		var sigBytes = Sign(payloadB64, active.KeyMaterial);
		var sigB64 = Base64UrlEncoder.Encode(sigBytes);
		return $"{payloadB64}.{sigB64}";
	}

	public bool TryValidateMovieToken(string token, ServiceScope scope, int tmdbId, DateTimeOffset nowUtc)
	{
		if (string.IsNullOrWhiteSpace(token))
		{
			return false;
		}

		var parts = token.Split('.', 2);
		if (parts.Length != 2)
		{
			return false;
		}

		var payloadB64 = parts[0];
		var sigB64 = parts[1];
		byte[] payloadBytes;
		byte[] sigBytes;
		try
		{
			payloadBytes = Base64UrlEncoder.DecodeBytes(payloadB64);
			sigBytes = Base64UrlEncoder.DecodeBytes(sigB64);
		}
		catch
		{
			return false;
		}

		PlaybackTokenPayload? payload;
		try
		{
			payload = JsonSerializer.Deserialize<PlaybackTokenPayload>(payloadBytes, Json);
		}
		catch
		{
			return false;
		}

		if (payload is null)
		{
			return false;
		}

		var expectedSvc = scope.ServiceType.ToString().ToLowerInvariant();
		if (!string.Equals(payload.Svc, expectedSvc, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		if (!string.Equals(payload.Sid, scope.ServerId, StringComparison.Ordinal))
		{
			return false;
		}

		if (payload.Tmdb != tmdbId)
		{
			return false;
		}

		if (payload.Exp <= 0 || nowUtc.ToUnixTimeSeconds() > payload.Exp)
		{
			return false;
		}

		var keys = keyStore.GetAllSigningKeys();
		foreach (var key in keys)
		{
			if (!string.IsNullOrWhiteSpace(payload.Kid)
				&& !string.Equals(key.KeyId, payload.Kid, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			var expectedSig = Sign(payloadB64, key.KeyMaterial);
			if (CryptographicOperations.FixedTimeEquals(expectedSig, sigBytes))
			{
				return true;
			}
		}

		// Rotation support: if kid is missing/unknown, try all keys.
		if (string.IsNullOrWhiteSpace(payload.Kid))
		{
			foreach (var key in keys)
			{
				var expectedSig = Sign(payloadB64, key.KeyMaterial);
				if (CryptographicOperations.FixedTimeEquals(expectedSig, sigBytes))
				{
					return true;
				}
			}
		}

		return false;
	}

	private static byte[] Sign(string payloadB64, byte[] keyMaterial)
	{
		using var hmac = new HMACSHA256(keyMaterial);
		return hmac.ComputeHash(Encoding.UTF8.GetBytes(payloadB64));
	}

	private sealed record PlaybackTokenPayload(
		string Kid,
		string Svc,
		string Sid,
		int Tmdb,
		long Exp);
}
