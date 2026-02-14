using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Tindarr.Application.Abstractions.Persistence;
using Tindarr.Application.Interfaces.Playback;
using Tindarr.Domain.Common;

namespace Tindarr.Infrastructure.Playback.Providers;

public sealed class JellyfinPlaybackProvider(
	IServiceSettingsRepository settingsRepo,
	HttpClient httpClient,
	ILogger<JellyfinPlaybackProvider> logger) : IPlaybackProvider
{
	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

	public ServiceType ServiceType => ServiceType.Jellyfin;

	public async Task<UpstreamPlaybackRequest> BuildMovieStreamRequestAsync(ServiceScope scope, int tmdbId, CancellationToken cancellationToken)
	{
		var settings = await settingsRepo.GetAsync(scope, cancellationToken).ConfigureAwait(false);
		if (settings is null || string.IsNullOrWhiteSpace(settings.JellyfinBaseUrl) || string.IsNullOrWhiteSpace(settings.JellyfinApiKey))
		{
			throw new InvalidOperationException("Jellyfin server is not configured.");
		}

		var baseUrl = NormalizeJellyfinBaseUrl(settings.JellyfinBaseUrl);
		var apiKey = settings.JellyfinApiKey!;

		var userId = await ResolveAdminUserIdAsync(httpClient, baseUrl, apiKey, cancellationToken).ConfigureAwait(false);
		if (string.IsNullOrWhiteSpace(userId))
		{
			throw new InvalidOperationException("Jellyfin did not return any users.");
		}

		var itemId = await FindMovieItemIdAsync(httpClient, baseUrl, apiKey, userId, tmdbId, cancellationToken).ConfigureAwait(false);
		if (string.IsNullOrWhiteSpace(itemId))
		{
			throw new InvalidOperationException("Jellyfin item not found.");
		}

		var upstreamUri = new Uri($"{baseUrl}Videos/{Uri.EscapeDataString(itemId)}/stream?static=true", UriKind.Absolute);
		return new UpstreamPlaybackRequest(
			Uri: upstreamUri,
			Headers: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
			{
				["X-Emby-Token"] = apiKey
			});
	}

	private static async Task<string?> ResolveAdminUserIdAsync(HttpClient client, string baseUrl, string apiKey, CancellationToken cancellationToken)
	{
		var uri = new Uri($"{baseUrl}Users", UriKind.Absolute);
		using var request = new HttpRequestMessage(HttpMethod.Get, uri);
		request.Headers.Accept.ParseAdd("application/json");
		request.Headers.Add("X-Emby-Token", apiKey);

		using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
		response.EnsureSuccessStatusCode();

		var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
		var users = JsonSerializer.Deserialize<List<UserDto>>(json, Json) ?? [];
		if (users.Count == 0)
		{
			return null;
		}

		var admin = users.FirstOrDefault(u => u.Policy?.IsAdministrator == true);
		return (admin ?? users[0]).Id?.Trim();
	}

	private async Task<string?> FindMovieItemIdAsync(
		HttpClient client,
		string baseUrl,
		string apiKey,
		string userId,
		int tmdbId,
		CancellationToken cancellationToken)
	{
		// Jellyfin doesn't have a single stable query shape across all versions/plugins.
		// Try a few safe variants.
		var candidates = new[]
		{
			$"Users/{Uri.EscapeDataString(userId)}/Items?IncludeItemTypes=Movie&Recursive=true&Limit=1&Fields=ProviderIds&AnyProviderIdEquals=tmdb.{tmdbId}",
			$"Users/{Uri.EscapeDataString(userId)}/Items?IncludeItemTypes=Movie&Recursive=true&Limit=1&Fields=ProviderIds&AnyProviderIdEquals=Tmdb.{tmdbId}",
		};

		foreach (var query in candidates)
		{
			var uri = new Uri($"{baseUrl}{query}", UriKind.Absolute);
			using var request = new HttpRequestMessage(HttpMethod.Get, uri);
			request.Headers.Accept.ParseAdd("application/json");
			request.Headers.Add("X-Emby-Token", apiKey);
			using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
			if (!response.IsSuccessStatusCode)
			{
				continue;
			}

			var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
			var dto = JsonSerializer.Deserialize<ItemsResponseDto>(json, Json);
			var id = dto?.Items?.FirstOrDefault()?.Id;
			if (!string.IsNullOrWhiteSpace(id))
			{
				return id.Trim();
			}
		}

		logger.LogDebug("jellyfin item id lookup failed. TmdbId={TmdbId}", tmdbId);
		return null;
	}

	private static string NormalizeJellyfinBaseUrl(string baseUrl)
	{
		var trimmed = (baseUrl ?? string.Empty).Trim().TrimEnd('/');
		return string.IsNullOrWhiteSpace(trimmed) ? string.Empty : trimmed + "/";
	}

	private sealed record UserDto(
		[property: JsonPropertyName("Id")] string? Id,
		[property: JsonPropertyName("Policy")] UserPolicyDto? Policy);

	private sealed record UserPolicyDto([property: JsonPropertyName("IsAdministrator")] bool IsAdministrator);

	private sealed record ItemsResponseDto(
		[property: JsonPropertyName("Items")] List<ItemDto>? Items,
		[property: JsonPropertyName("TotalRecordCount")] int TotalRecordCount);

	private sealed record ItemDto([property: JsonPropertyName("Id")] string? Id);
}
