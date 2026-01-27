using Tindarr.Infrastructure.Security;

namespace Tindarr.UnitTests;

public sealed class PasswordHasherTests
{
	[Fact]
	public void Hash_and_verify_roundtrip_succeeds()
	{
		var hasher = new Pbkdf2PasswordHasher();
		var hashed = hasher.Hash("correct horse battery staple", iterations: 50_000);

		Assert.NotNull(hashed.Hash);
		Assert.NotNull(hashed.Salt);
		Assert.True(hashed.Hash.Length > 0);
		Assert.True(hashed.Salt.Length > 0);

		var ok = hasher.Verify("correct horse battery staple", hashed.Hash, hashed.Salt, hashed.Iterations);
		Assert.True(ok);
	}

	[Fact]
	public void Verify_with_wrong_password_fails()
	{
		var hasher = new Pbkdf2PasswordHasher();
		var hashed = hasher.Hash("p@ssw0rd", iterations: 50_000);

		var ok = hasher.Verify("wrong", hashed.Hash, hashed.Salt, hashed.Iterations);
		Assert.False(ok);
	}

	[Fact]
	public void Hash_uses_per_user_salt()
	{
		var hasher = new Pbkdf2PasswordHasher();
		var a = hasher.Hash("same", iterations: 50_000);
		var b = hasher.Hash("same", iterations: 50_000);

		Assert.NotEqual(Convert.ToBase64String(a.Salt), Convert.ToBase64String(b.Salt));
		Assert.NotEqual(Convert.ToBase64String(a.Hash), Convert.ToBase64String(b.Hash));
	}
}