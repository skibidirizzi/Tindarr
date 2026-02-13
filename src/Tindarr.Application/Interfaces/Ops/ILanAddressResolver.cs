using System.Net;

namespace Tindarr.Application.Interfaces.Ops;

/// <summary>
/// Resolves the local machine's LAN address (best-effort) for same-host integrations.
/// </summary>
public interface ILanAddressResolver
{
	/// <summary>
	/// Returns a best-effort IPv4 LAN address for the current host.
	/// </summary>
	IPAddress? GetLanIPv4();
}
