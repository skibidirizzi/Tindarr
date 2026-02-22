namespace Tindarr.Application.Interfaces.Ops;

/// <summary>
/// Resolves the machine's public (WAN) IPv4 address via an external service (best-effort).
/// </summary>
public interface IWanAddressResolver
{
	/// <summary>
	/// Returns the public IPv4 address for the current host, or null if detection fails or times out.
	/// </summary>
	Task<string?> GetPublicIPv4Async(CancellationToken cancellationToken = default);
}
