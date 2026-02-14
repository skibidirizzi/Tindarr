using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Tindarr.Application.Abstractions.Security;
using Tindarr.Application.Options;

namespace Tindarr.Infrastructure.Security;

public sealed class CastUrlTokenService(
	ITokenSigningKeyStore keyStore,
	IOptions<PlaybackOptions> options) : ICastUrlTokenService
{
	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
	private readonly PlaybackOptions _options = options.Value;

	public string IssueRoomQrToken(string roomId, DateTimeOffset nowUtc)
	{
		if (string.IsNullOrWhiteSpace(roomId))
		{
			throw new ArgumentException("roomId is required.", nameof(roomId));
		}

		var exp = nowUtc.AddMinutes(Math.Clamp(_options.TokenMinutes, 1, 60));
		var payload = new CastTokenPayload(
			Kid: keyStore.GetActiveKeyId(),
			Typ: "qr",
			Rid: roomId.Trim(),
			Exp: exp.ToUnixTimeSeconds());

		var payloadBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, Json));
		var payloadB64 = Base64UrlEncoder.Encode(payloadBytes);

		var active = keyStore.GetActiveSigningKey();
		var sigBytes = Sign(payloadB64, active.KeyMaterial);
		var sigB64 = Base64UrlEncoder.Encode(sigBytes);
		return $"{payloadB64}.{sigB64}";
	}

	public bool TryValidateRoomQrToken(string token, string roomId, DateTimeOffset nowUtc)
	{
		if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(roomId))
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

		CastTokenPayload? payload;
		try
		{
			payload = JsonSerializer.Deserialize<CastTokenPayload>(payloadBytes, Json);
		}
		catch
		{
			return false;
		}

		if (payload is null)
		{
			return false;
		}

		if (!string.Equals(payload.Typ, "qr", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		if (!string.Equals(payload.Rid, roomId.Trim(), StringComparison.Ordinal))
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

	private sealed record CastTokenPayload(
		string Kid,
		string Typ,
		string Rid,
		long Exp);
}
