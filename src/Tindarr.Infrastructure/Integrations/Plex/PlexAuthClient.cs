using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
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
		var parsed = JsonSerializer.Deserialize<List<PlexResourceDto>>(body, Json) ?? [];

		var servers = new List<PlexServerResource>();
		foreach (var resource in parsed)
		{
			if (resource is null || string.IsNullOrWhiteSpace(resource.MachineIdentifier))
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
				resource.MachineIdentifier!,
				resource.Name ?? resource.MachineIdentifier!,
				resource.ProductVersion,
				resource.Platform,
				resource.Owned ?? false,
				resource.Presence ?? false,
				resource.AccessToken,
				connections));
		}

		return servers;
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
