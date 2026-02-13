using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Tindarr.Application.Interfaces.Ops;

namespace Tindarr.Application.Services;

public sealed class LanAddressResolver(ILogger<LanAddressResolver> logger) : ILanAddressResolver
{
	private IPAddress? _cached;
	private bool _logged;

	public IPAddress? GetLanIPv4()
	{
		if (_cached is not null)
		{
			return _cached;
		}

		_cached = ResolveLanIPv4();

		if (!_logged)
		{
			_logged = true;
			if (_cached is not null)
			{
				logger.LogInformation("Detected LAN IPv4 address: {LanIp}", _cached);
			}
			else
			{
				logger.LogInformation("No LAN IPv4 address detected; using discovered provider URLs.");
			}
		}

		return _cached;
	}

	private static IPAddress? ResolveLanIPv4()
	{
		try
		{
			foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
			{
				if (ni.OperationalStatus != OperationalStatus.Up)
				{
					continue;
				}

				if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
				{
					continue;
				}

				var props = ni.GetIPProperties();
				foreach (var uni in props.UnicastAddresses)
				{
					var ip = uni.Address;
					if (ip.AddressFamily != AddressFamily.InterNetwork)
					{
						continue;
					}

					if (IPAddress.IsLoopback(ip))
					{
						continue;
					}

					if (IsPrivateOrLinkLocalV4(ip))
					{
						return ip;
					}
				}
			}
		}
		catch
		{
			// Best-effort only.
		}

		return null;
	}

	private static bool IsPrivateOrLinkLocalV4(IPAddress ip)
	{
		var b = ip.GetAddressBytes();

		// 10.0.0.0/8
		if (b[0] == 10) return true;

		// 172.16.0.0/12
		if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return true;

		// 192.168.0.0/16
		if (b[0] == 192 && b[1] == 168) return true;

		// 169.254.0.0/16 (link-local)
		if (b[0] == 169 && b[1] == 254) return true;

		return false;
	}
}
