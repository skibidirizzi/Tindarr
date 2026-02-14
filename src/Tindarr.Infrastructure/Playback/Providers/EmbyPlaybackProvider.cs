using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Tindarr.Application.Abstractions.Persistence;
using Tindarr.Application.Interfaces.Playback;
using Tindarr.Domain.Common;

namespace Tindarr.Infrastructure.Playback.Providers;

public sealed class EmbyPlaybackProvider(
	IServiceSettingsRepository settingsRepo,
	HttpClient httpClient,
	ILogger<EmbyPlaybackProvider> logger) : IPlaybackProvider
{
	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

	public ServiceType ServiceType => ServiceType.Emby;

	public async Task<UpstreamPlaybackRequest> BuildMovieStreamRequestAsync(ServiceScope scope, int tmdbId, CancellationToken cancellationToken)
	{
		var settings = await settingsRepo.GetAsync(scope, cancellationToken).ConfigureAwait(false);
		if (settings is null || string.IsNullOrWhiteSpace(settings.EmbyBaseUrl) || string.IsNullOrWhiteSpace(settings.EmbyApiKey))
		{
			throw new InvalidOperationException("Emby server is not configured.");
		}

		var baseUrl = NormalizeEmbyBaseUrl(settings.EmbyBaseUrl);
		var apiKey = settings.EmbyApiKey!;

		var userId = await ResolveAdminUserIdAsync(httpClient, baseUrl, apiKey, cancellationToken).ConfigureAwait(false);
		if (string.IsNullOrWhiteSpace(userId))
		{
			throw new InvalidOperationException("Emby did not return any users.");
		}

		var itemId = await FindMovieItemIdAsync(httpClient, baseUrl, apiKey, userId, tmdbId, cancellationToken).ConfigureAwait(false);
		if (string.IsNullOrWhiteSpace(itemId))
		{
			throw new InvalidOperationException("Emby item not found.");
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

		logger.LogDebug("emby item id lookup failed. TmdbId={TmdbId}", tmdbId);
		return null;
	}

	private static string NormalizeEmbyBaseUrl(string baseUrl)
	{
		var trimmed = (baseUrl ?? string.Empty).Trim().TrimEnd('/');
		if (string.IsNullOrWhiteSpace(trimmed))
		{
			return string.Empty;
		}

		// Emby API is under /emby/ unless user already configured it.
		if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
		{
			var absolutePath = (uri.AbsolutePath ?? string.Empty).TrimEnd('/');
			var endsWithEmby = absolutePath.EndsWith("/emby", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(absolutePath, "emby", StringComparison.OrdinalIgnoreCase);
			return endsWithEmby ? trimmed + "/" : trimmed + "/emby/";
		}

		return trimmed.EndsWith("/emby", StringComparison.OrdinalIgnoreCase)
			? trimmed + "/"
			: trimmed + "/emby/";
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
