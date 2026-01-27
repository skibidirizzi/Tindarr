namespace Tindarr.Application.Abstractions.Security;

public interface IPasswordHasher
{
	PasswordHash Hash(string password, int iterations);

	bool Verify(string password, byte[] hash, byte[] salt, int iterations);
}

public sealed record PasswordHash(byte[] Hash, byte[] Salt, int Iterations);

