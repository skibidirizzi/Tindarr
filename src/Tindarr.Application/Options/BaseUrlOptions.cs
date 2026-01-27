using System.Globalization;
using System.Net;

namespace Tindarr.Application.Options;

public sealed class BaseUrlOptions
{
	public const string SectionName = "BaseUrl";

	/// <summary>
	/// Base URL reachable from the local network (LAN). Example: http://tindarr.lan:5000
	/// </summary>
	public string? Lan { get; init; }

	/// <summary>
	/// Base URL reachable from outside the local network (WAN). Example: https://tindarr.example.com
	/// </summary>
	public string? Wan { get; init; }

	/// <summary>
	/// How to choose between LAN/WAN URLs.
	/// </summary>
	public BaseUrlMode Mode { get; init; } = BaseUrlMode.Auto;

	/// <summary>
	/// Optional list of CIDR blocks considered "LAN clients" for Auto mode (e.g. 192.168.0.0/16).
	/// If empty, Auto mode falls back to standard private/loopback/link-local ranges.
	/// </summary>
	public string[] LanNetworks { get; init; } = [];

	public bool IsValid()
	{
		if (!Enum.IsDefined(typeof(BaseUrlMode), Mode))
		{
			return false;
		}

		var hasLan = !string.IsNullOrWhiteSpace(Lan);
		var hasWan = !string.IsNullOrWhiteSpace(Wan);
		if (!hasLan && !hasWan)
		{
			return false;
		}

		if (hasLan && !IsValidAbsoluteHttpUrl(Lan!))
		{
			return false;
		}

		if (hasWan && !IsValidAbsoluteHttpUrl(Wan!))
		{
			return false;
		}

		foreach (var cidr in LanNetworks)
		{
			if (string.IsNullOrWhiteSpace(cidr))
			{
				continue;
			}

			if (!CidrBlock.TryParse(cidr, out _))
			{
				return false;
			}
		}

		return true;
	}

	private static bool IsValidAbsoluteHttpUrl(string value)
	{
		if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri))
		{
			return false;
		}

		if (uri.Scheme is not ("http" or "https"))
		{
			return false;
		}

		// Force host presence; also blocks weird partial forms.
		if (string.IsNullOrWhiteSpace(uri.Host))
		{
			return false;
		}

		// No secrets should ever be configured in query/fragment.
		if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
		{
			return false;
		}

		return true;
	}

	internal readonly record struct CidrBlock(byte[] NetworkBytes, int PrefixLength)
	{
		public static bool TryParse(string value, out CidrBlock cidr)
		{
			cidr = default;

			var parts = value.Trim().Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
			if (parts.Length != 2)
			{
				return false;
			}

			if (!IPAddress.TryParse(parts[0], out var ip))
			{
				return false;
			}

			if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var prefix))
			{
				return false;
			}

			var bytes = ip.GetAddressBytes();
			var max = bytes.Length * 8;
			if (prefix < 0 || prefix > max)
			{
				return false;
			}

			cidr = new CidrBlock(bytes, prefix);
			return true;
		}
	}
}

public enum BaseUrlMode
{
	Auto = 0,
	ForceLan = 1,
	ForceWan = 2
}
