using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tindarr.Application.Abstractions.Integrations;
using Tindarr.Application.Options;

namespace Tindarr.Infrastructure.Integrations.Plex;

public sealed class PlexAuthClient(HttpClient httpClient, IOptions<PlexOptions> options, ILogger<PlexAuthClient> logger) : IPlexAuthClient
{
	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
	private static readonly Uri PlexTvBaseUri = new("https://plex.tv/");
	private readonly PlexOptions _options = options.Value;

	public async Task<PlexPinResult> CreatePinAsync(string clientIdentifier, CancellationToken cancellationToken)
	{
		var request = new HttpRequestMessage(HttpMethod.Post, BuildPlexTvUri("api/v2/pins?strong=true"));
		ApplyHeaders(request, clientIdentifier, authToken: null);

		using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
		if (!response.IsSuccessStatusCode)
		{
			logger.LogWarning("plex pin request failed. Status={Status}", (int)response.StatusCode);
			throw new HttpRequestException($"Plex PIN request failed. Status {(int)response.StatusCode} {response.StatusCode}.", null, response.StatusCode);
		}

		var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
		var parsed = JsonSerializer.Deserialize<PlexPinDto>(body, Json);
		if (parsed is null || parsed.Id <= 0 || string.IsNullOrWhiteSpace(parsed.Code))
		{
			throw new HttpRequestException("Plex PIN response was invalid.");
		}

		return new PlexPinResult(parsed.Id, parsed.Code, parsed.ExpiresAt, parsed.AuthToken);
	}

	public async Task<PlexPinResult?> GetPinAsync(string clientIdentifier, long pinId, CancellationToken cancellationToken)
	{
		var request = new HttpRequestMessage(HttpMethod.Get, BuildPlexTvUri($"api/v2/pins/{pinId}"));
		ApplyHeaders(request, clientIdentifier, authToken: null);

		using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
		if (response.StatusCode == HttpStatusCode.NotFound)
		{
			return null;
		}

		if (!response.IsSuccessStatusCode)
		{
			logger.LogWarning("plex pin status failed. Status={Status}", (int)response.StatusCode);
			throw new HttpRequestException($"Plex PIN status failed. Status {(int)response.StatusCode} {response.StatusCode}.", null, response.StatusCode);
		}

		var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
		var parsed = JsonSerializer.Deserialize<PlexPinDto>(body, Json);
		if (parsed is null || parsed.Id <= 0 || string.IsNullOrWhiteSpace(parsed.Code))
		{
			return null;
		}

		return new PlexPinResult(parsed.Id, parsed.Code, parsed.ExpiresAt, parsed.AuthToken);
	}

	public async Task<PlexTokenValidationResult> ValidateTokenAsync(string clientIdentifier, string authToken, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(authToken))
		{
			return new PlexTokenValidationResult(false, "Plex token is missing.");
		}

		var request = new HttpRequestMessage(HttpMethod.Get, BuildPlexTvUri("api/v2/user"));
		ApplyHeaders(request, clientIdentifier, authToken);

		using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
		if (response.IsSuccessStatusCode)
		{
			return new PlexTokenValidationResult(true, null);
		}

		if (response.StatusCode == HttpStatusCode.Unauthorized)
		{
			return new PlexTokenValidationResult(false, "Plex token rejected.");
		}

		return new PlexTokenValidationResult(false, $"Plex token validation failed. Status {(int)response.StatusCode} {response.StatusCode}.");
	}

	public async Task<IReadOnlyList<PlexServerResource>> GetServersAsync(string clientIdentifier, string authToken, CancellationToken cancellationToken)
	{
		var request = new HttpRequestMessage(HttpMethod.Get, BuildPlexTvUri("api/v2/resources?includeHttps=1&includeRelay=1&includeIPv6=1"));
		ApplyHeaders(request, clientIdentifier, authToken);

		using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
		if (!response.IsSuccessStatusCode)
		{
			logger.LogWarning("plex resources fetch failed. Status={Status}", (int)response.StatusCode);
			throw new HttpRequestException($"Plex resources request failed. Status {(int)response.StatusCode} {response.StatusCode}.", null, response.StatusCode);
		}

		var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
		return ParseServers(body);
	}

	private IReadOnlyList<PlexServerResource> ParseServers(string body)
	{
		if (string.IsNullOrWhiteSpace(body))
		{
			return [];
		}

		try
		{
			if (LooksLikeXml(body))
			{
				return ParseServersFromXml(body);
			}

			var resources = ExtractResourcesFromJson(body);
			return MapServers(resources);
		}
		catch (Exception ex)
		{
			logger.LogWarning(ex, "plex resources response could not be parsed");
			throw new HttpRequestException("Plex resources response was invalid.");
		}
	}

	private static bool LooksLikeXml(string body)
	{
		for (var i = 0; i < body.Length; i++)
		{
			var ch = body[i];
			if (char.IsWhiteSpace(ch))
			{
				continue;
			}

			return ch == '<';
		}

		return false;
	}

	private static List<PlexResourceDto> ExtractResourcesFromJson(string body)
	{
		using var doc = JsonDocument.Parse(body);
		var root = doc.RootElement;

		return root.ValueKind switch
		{
			JsonValueKind.Array => JsonSerializer.Deserialize<List<PlexResourceDto>>(root.GetRawText(), Json) ?? [],
			JsonValueKind.Object => ExtractResourcesFromJsonObject(root),
			_ => []
		};
	}

	private static List<PlexResourceDto> ExtractResourcesFromJsonObject(JsonElement root)
	{
		if (root.TryGetProperty("resources", out var resourcesElement) && resourcesElement.ValueKind == JsonValueKind.Array)
		{
			return JsonSerializer.Deserialize<List<PlexResourceDto>>(resourcesElement.GetRawText(), Json) ?? [];
		}

		if (root.TryGetProperty("MediaContainer", out var mediaContainer)
			&& mediaContainer.ValueKind == JsonValueKind.Object
			&& mediaContainer.TryGetProperty("Device", out var devices)
			&& devices.ValueKind == JsonValueKind.Array)
		{
			return JsonSerializer.Deserialize<List<PlexResourceDto>>(devices.GetRawText(), Json) ?? [];
		}

		return [];
	}

	private static IReadOnlyList<PlexServerResource> MapServers(IEnumerable<PlexResourceDto?> resources)
	{
		var servers = new List<PlexServerResource>();
		foreach (var resource in resources)
		{
			if (resource is null)
			{
				continue;
			}

			var machineId = string.IsNullOrWhiteSpace(resource.MachineIdentifier)
				? resource.ClientIdentifier
				: resource.MachineIdentifier;

			if (string.IsNullOrWhiteSpace(machineId))
			{
				continue;
			}

			if (string.IsNullOrWhiteSpace(resource.Provides) || !resource.Provides.Contains("server", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			var connections = resource.Connections?
				.Where(c => !string.IsNullOrWhiteSpace(c.Uri))
				.Select(c => new PlexServerConnection(
					Uri: c.Uri!,
					Local: c.Local ?? false,
					Relay: c.Relay ?? false,
					Protocol: string.IsNullOrWhiteSpace(c.Protocol) ? "http" : c.Protocol!))
				.ToList() ?? [];

			servers.Add(new PlexServerResource(
				machineId!,
				resource.Name ?? machineId!,
				resource.ProductVersion,
				resource.Platform,
				resource.Owned ?? false,
				resource.Presence ?? false,
				resource.AccessToken,
				connections));
		}

		return servers;
	}

	private static IReadOnlyList<PlexServerResource> ParseServersFromXml(string body)
	{
		var doc = XDocument.Parse(body);
		var mediaContainer = doc.Root;
		if (mediaContainer is null)
		{
			return [];
		}

		var devices = mediaContainer.Elements().Where(e => string.Equals(e.Name.LocalName, "Device", StringComparison.OrdinalIgnoreCase));
		var servers = new List<PlexServerResource>();
		foreach (var device in devices)
		{
			var provides = (string?)device.Attribute("provides");
			if (string.IsNullOrWhiteSpace(provides) || !provides.Contains("server", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			var machineId = (string?)device.Attribute("machineIdentifier")
				?? (string?)device.Attribute("clientIdentifier");
			if (string.IsNullOrWhiteSpace(machineId))
			{
				continue;
			}

			var name = (string?)device.Attribute("name") ?? machineId;
			var version = (string?)device.Attribute("productVersion");
			var platform = (string?)device.Attribute("platform");
			var accessToken = (string?)device.Attribute("accessToken");
			var owned = ParseXmlBool(device.Attribute("owned"));
			var presence = ParseXmlBool(device.Attribute("presence"));

			var connections = device.Elements().Where(e => string.Equals(e.Name.LocalName, "Connection", StringComparison.OrdinalIgnoreCase))
				.Select(c => new PlexServerConnection(
					Uri: (string?)c.Attribute("uri") ?? string.Empty,
					Local: ParseXmlBool(c.Attribute("local")),
					Relay: ParseXmlBool(c.Attribute("relay")),
					Protocol: (string?)c.Attribute("protocol") ?? "http"))
				.Where(c => !string.IsNullOrWhiteSpace(c.Uri))
				.ToList();

			servers.Add(new PlexServerResource(
				machineId,
				name,
				version,
				platform,
				owned,
				presence,
				accessToken,
				connections));
		}

		return servers;
	}

	private static bool ParseXmlBool(XAttribute? attribute)
	{
		var raw = attribute?.Value;
		if (string.IsNullOrWhiteSpace(raw))
		{
			return false;
		}

		if (bool.TryParse(raw, out var parsed))
		{
			return parsed;
		}

		return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) && number != 0;
	}

	private void ApplyHeaders(HttpRequestMessage request, string clientIdentifier, string? authToken)
	{
		request.Headers.Accept.ParseAdd("application/json");
		request.Headers.Add("X-Plex-Client-Identifier", clientIdentifier);
		request.Headers.Add("X-Plex-Product", _options.Product);
		request.Headers.Add("X-Plex-Platform", _options.Platform);
		request.Headers.Add("X-Plex-Device", _options.Device);
		request.Headers.Add("X-Plex-Version", _options.Version);
		request.Headers.Add("X-Plex-Provides", "controller");

		if (!string.IsNullOrWhiteSpace(authToken))
		{
			request.Headers.Add("X-Plex-Token", authToken);
		}
	}

	private static Uri BuildPlexTvUri(string pathAndQuery)
	{
		var trimmed = pathAndQuery.TrimStart('/');
		return new Uri(PlexTvBaseUri, trimmed);
	}

	private sealed record PlexPinDto(
		[property: JsonPropertyName("id")] long Id,
		[property: JsonPropertyName("code")] string? Code,
		[property: JsonPropertyName("expiresAt")] DateTimeOffset? ExpiresAt,
		[property: JsonPropertyName("authToken")] string? AuthToken);

	private sealed record PlexResourceDto(
		[property: JsonPropertyName("name")] string? Name,
		[property: JsonPropertyName("clientIdentifier")] string? ClientIdentifier,
		[property: JsonPropertyName("machineIdentifier")] string? MachineIdentifier,
		[property: JsonPropertyName("accessToken")] string? AccessToken,
		[property: JsonPropertyName("provides")] string? Provides,
		[property: JsonPropertyName("owned")] bool? Owned,
		[property: JsonPropertyName("presence")] bool? Presence,
		[property: JsonPropertyName("productVersion")] string? ProductVersion,
		[property: JsonPropertyName("platform")] string? Platform,
		[property: JsonPropertyName("connections")] List<PlexConnectionDto>? Connections);

	private sealed record PlexConnectionDto(
		[property: JsonPropertyName("uri")] string? Uri,
		[property: JsonPropertyName("protocol")] string? Protocol,
		[property: JsonPropertyName("local")] bool? Local,
		[property: JsonPropertyName("relay")] bool? Relay);
}
