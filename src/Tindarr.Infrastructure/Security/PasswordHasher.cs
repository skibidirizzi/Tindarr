using System.Security.Cryptography;
using Tindarr.Application.Abstractions.Security;

namespace Tindarr.Infrastructure.Security;

/// <summary>
/// PBKDF2-SHA256 password hasher (INV-0005).
/// </summary>
public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
	private const int SaltSizeBytes = 16;
	private const int HashSizeBytes = 32;

	public PasswordHash Hash(string password, int iterations)
	{
		if (string.IsNullOrEmpty(password))
		{
			throw new ArgumentException("Password is required.", nameof(password));
		}

		if (iterations <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(iterations), "Iterations must be > 0.");
		}

		var salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
		var hash = Rfc2898DeriveBytes.Pbkdf2(
			password,
			salt,
			iterations,
			HashAlgorithmName.SHA256,
			HashSizeBytes);

		return new PasswordHash(hash, salt, iterations);
	}

	public bool Verify(string password, byte[] hash, byte[] salt, int iterations)
	{
		if (string.IsNullOrEmpty(password) || hash is null || salt is null)
		{
			return false;
		}

		if (iterations <= 0)
		{
			return false;
		}

		var computed = Rfc2898DeriveBytes.Pbkdf2(
			password,
			salt,
			iterations,
			HashAlgorithmName.SHA256,
			hash.Length);

		return CryptographicOperations.FixedTimeEquals(computed, hash);
	}
}

