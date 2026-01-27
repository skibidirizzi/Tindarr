namespace Tindarr.Application.Abstractions.Security;

public interface ITokenSigningKeyStore
{
	/// <summary>
	/// Returns the currently active signing key (used for issuance).
	/// </summary>
	SigningKey GetActiveSigningKey();

	/// <summary>
	/// Returns all known signing keys (used for validation / rotation support).
	/// </summary>
	IReadOnlyCollection<SigningKey> GetAllSigningKeys();

	/// <summary>
	/// Returns the active key id ("kid") for outgoing tokens.
	/// </summary>
	string GetActiveKeyId();
}

public sealed record SigningKey(string KeyId, byte[] KeyMaterial);

