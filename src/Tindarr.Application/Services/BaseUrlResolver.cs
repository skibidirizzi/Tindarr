using System.Net;
using Tindarr.Application.Interfaces.Ops;
using Tindarr.Application.Options;

namespace Tindarr.Application.Services;

public sealed class BaseUrlResolver : IBaseUrlResolver
{
	private readonly BaseUrlOptions _options;
	private readonly Uri? _lan;
	private readonly Uri? _wan;
	private readonly BaseUrlOptions.CidrBlock[] _lanNetworks;

	public BaseUrlResolver(BaseUrlOptions options)
	{
		_options = options ?? throw new ArgumentNullException(nameof(options));
		_lan = Normalize(_options.Lan);
		_wan = Normalize(_options.Wan);
		_lanNetworks = ParseLanNetworks(_options.LanNetworks);
	}

	public Uri GetBaseUri(IPAddress? clientIp = null)
	{
		if (_lan is null && _wan is null)
		{
			throw new InvalidOperationException(
				$"{nameof(BaseUrlOptions)} is not configured. Set '{BaseUrlOptions.SectionName}:Lan' and/or '{BaseUrlOptions.SectionName}:Wan'.");
		}

		return _options.Mode switch
		{
			BaseUrlMode.ForceLan => _lan ?? _wan!,
			BaseUrlMode.ForceWan => _wan ?? _lan!,
			BaseUrlMode.Auto => ResolveAuto(clientIp),
			_ => throw new InvalidOperationException($"Unsupported {nameof(BaseUrlMode)} value '{_options.Mode}'.")
		};
	}

	public Uri Combine(string relativePathAndQuery, IPAddress? clientIp = null)
	{
		if (string.IsNullOrWhiteSpace(relativePathAndQuery))
		{
			throw new ArgumentException("Value cannot be empty.", nameof(relativePathAndQuery));
		}

		// Do not allow absolute URLs: callers must always combine with server-known bases.
		if (Uri.TryCreate(relativePathAndQuery, UriKind.Absolute, out _))
		{
			throw new ArgumentException("Must be a relative path.", nameof(relativePathAndQuery));
		}

		var baseUri = GetBaseUri(clientIp);
		var rel = relativePathAndQuery.StartsWith('/') ? relativePathAndQuery : "/" + relativePathAndQuery;
		return new Uri(baseUri, rel);
	}

	public bool IsLanClient(IPAddress ip)
	{
		if (IPAddress.IsLoopback(ip))
		{
			return true;
		}

		if (_lanNetworks.Length > 0)
		{
			return _lanNetworks.Any(c => Contains(c, ip));
		}

		return IsPrivateOrLinkLocal(ip);
	}

	private Uri ResolveAuto(IPAddress? clientIp)
	{
		if (_lan is null)
		{
			return _wan!;
		}

		if (_wan is null)
		{
			return _lan;
		}

		if (clientIp is null)
		{
			// Conservative default: assume WAN if we can't classify.
			return _wan;
		}

		return IsLanClient(clientIp) ? _lan : _wan;
	}

	private static Uri? Normalize(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return null;
		}

		if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri))
		{
			throw new InvalidOperationException($"Invalid base URL '{value}'.");
		}

		if (uri.Scheme is not ("http" or "https"))
		{
			throw new InvalidOperationException($"Base URL must be http/https, got '{uri.Scheme}'.");
		}

		var builder = new UriBuilder(uri)
		{
			Query = string.Empty,
			Fragment = string.Empty
		};

		// Normalize to no trailing slash (unless just "/").
		builder.Path = (builder.Path ?? "/").TrimEnd('/');
		if (string.IsNullOrEmpty(builder.Path))
		{
			builder.Path = "/";
		}

		return builder.Uri;
	}

	private static BaseUrlOptions.CidrBlock[] ParseLanNetworks(string[] lanNetworks)
	{
		if (lanNetworks.Length == 0)
		{
			return [];
		}

		var list = new List<BaseUrlOptions.CidrBlock>(lanNetworks.Length);
		foreach (var value in lanNetworks)
		{
			if (string.IsNullOrWhiteSpace(value))
			{
				continue;
			}

			if (!BaseUrlOptions.CidrBlock.TryParse(value, out var cidr))
			{
				throw new InvalidOperationException($"Invalid LAN CIDR block '{value}'.");
			}

			list.Add(cidr);
		}

		return list.ToArray();
	}

	private static bool Contains(BaseUrlOptions.CidrBlock cidr, IPAddress ip)
	{
		var ipBytes = ip.GetAddressBytes();
		if (ipBytes.Length != cidr.NetworkBytes.Length)
		{
			return false; // don't mix IPv4/IPv6
		}

		var fullBytes = cidr.PrefixLength / 8;
		var remainingBits = cidr.PrefixLength % 8;

		for (var i = 0; i < fullBytes; i++)
		{
			if (ipBytes[i] != cidr.NetworkBytes[i])
			{
				return false;
			}
		}

		if (remainingBits == 0)
		{
			return true;
		}

		var mask = (byte)(0xFF << (8 - remainingBits));
		return (ipBytes[fullBytes] & mask) == (cidr.NetworkBytes[fullBytes] & mask);
	}

	private static bool IsPrivateOrLinkLocal(IPAddress ip)
	{
		if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
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

		if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
		{
			if (ip.IsIPv6LinkLocal) return true;       // fe80::/10
			if (ip.IsIPv6SiteLocal) return true;       // deprecated but safe to treat as LAN

			var bytes = ip.GetAddressBytes();
			var first = bytes[0];

			// fc00::/7 (unique local)
			if ((first & 0xFE) == 0xFC) return true;

			return false;
		}

		return false;
	}
}

