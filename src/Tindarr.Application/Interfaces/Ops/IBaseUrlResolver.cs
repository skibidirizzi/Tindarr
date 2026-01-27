using System.Net;

namespace Tindarr.Application.Interfaces.Ops;

/// <summary>
/// Resolves a safe server-configured base URL (LAN/WAN) for generating externally visible links.
/// </summary>
public interface IBaseUrlResolver
{
	/// <summary>
	/// Returns the best base URI for a client, based on configuration and (optional) client IP.
	/// </summary>
	Uri GetBaseUri(IPAddress? clientIp = null);

	/// <summary>
	/// Combines a base URI with a relative path (and optional query).
	/// </summary>
	Uri Combine(string relativePathAndQuery, IPAddress? clientIp = null);

	/// <summary>
	/// True if the provided IP is considered LAN in current configuration.
	/// </summary>
	bool IsLanClient(IPAddress ip);
}
