using System.Net;
using Microsoft.Extensions.Logging;
using Tindarr.Application.Interfaces.Ops;

namespace Tindarr.Infrastructure.Ops;

/// <summary>
/// Resolves the machine's public IPv4 via external plain-text endpoints (best-effort). Tries multiple HTTPS
/// services in order; caches result briefly since public IP does not change every second.
/// </summary>
public sealed class WanAddressResolver(IHttpClientFactory httpClientFactory, ILogger<WanAddressResolver> logger) : IWanAddressResolver
{
	private const string ClientName = "WanAddressResolver";
	private const int CacheMinutes = 5;

	private static readonly string[] Endpoints =
	{
		"https://api.ipify.org",
		"https://checkip.amazonaws.com",
		"https://ifconfig.me/ip"
	};

	private string? _cachedIp;
	private DateTimeOffset _cachedAt;
	private readonly object _lock = new();

	public async Task<string?> GetPublicIPv4Async(CancellationToken cancellationToken = default)
	{
		lock (_lock)
		{
			if (_cachedIp is not null && (DateTimeOffset.UtcNow - _cachedAt).TotalMinutes < CacheMinutes)
			{
				return _cachedIp;
			}
		}

		try
		{
			var client = httpClientFactory.CreateClient(ClientName);
			foreach (var url in Endpoints)
			{
				try
				{
					var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
					response.EnsureSuccessStatusCode();
					var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
					var ip = body?.Trim();
					if (string.IsNullOrWhiteSpace(ip))
						continue;
					if (!IPAddress.TryParse(ip, out var addr) || addr.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
						continue;

					lock (_lock)
					{
						_cachedIp = ip;
						_cachedAt = DateTimeOffset.UtcNow;
					}
					logger.LogDebug("Detected public IPv4: {WanIp} via {Endpoint}", ip, url);
					return ip;
				}
				catch (OperationCanceledException)
				{
					throw;
				}
				catch (Exception ex)
				{
					logger.LogDebug(ex, "WAN IP endpoint failed: {Endpoint}", url);
				}
			}
			logger.LogDebug("WAN IP detection failed: all endpoints failed.");
			return null;
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex)
		{
			logger.LogDebug(ex, "WAN IP detection failed (best-effort).");
			return null;
		}
	}
}
