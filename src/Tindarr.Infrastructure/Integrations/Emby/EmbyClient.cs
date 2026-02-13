using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Tindarr.Application.Abstractions.Integrations;

namespace Tindarr.Infrastructure.Integrations.Emby;

public sealed class EmbyClient(HttpClient httpClient, ILogger<EmbyClient> logger) : IEmbyClient
{
	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

	public async Task<EmbyConnectionTestResult> TestConnectionAsync(EmbyConnection connection, CancellationToken cancellationToken)
	{
		try
		{
			_ = await GetSystemInfoAsync(connection, cancellationToken).ConfigureAwait(false);
			return new EmbyConnectionTestResult(true, null);
		}
		catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
		{
			logger.LogWarning(ex, "emby connection test failed. BaseUrl={BaseUrl}", connection.BaseUrl);
			return new EmbyConnectionTestResult(false, "Unable to reach Emby.");
		}
	}

	public async Task<EmbySystemInfo> GetSystemInfoAsync(EmbyConnection connection, CancellationToken cancellationToken)
	{
		var uri = BuildApiUri(connection.BaseUrl, "System/Info");
		using var response = await SendAsync(connection, HttpMethod.Get, uri, content: null, cancellationToken).ConfigureAwait(false);
		if (!response.IsSuccessStatusCode)
		{
			throw new HttpRequestException($"Emby request failed. Status {(int)response.StatusCode} {response.StatusCode}.", null, response.StatusCode);
		}

		var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
		var dto = JsonSerializer.Deserialize<SystemInfoDto>(body, Json)
			?? throw new JsonException("Failed to deserialize Emby system info.");

		if (string.IsNullOrWhiteSpace(dto.Id))
		{
			throw new InvalidOperationException("Emby system info did not include an Id.");
		}

		return new EmbySystemInfo(dto.Id.Trim(), (dto.ServerName ?? string.Empty).Trim(), (dto.Version ?? string.Empty).Trim());
	}

	public async Task<IReadOnlyList<int>> GetLibraryTmdbIdsAsync(EmbyConnection connection, CancellationToken cancellationToken)
	{
		var userId = await ResolveUserIdAsync(connection, cancellationToken).ConfigureAwait(false);
		if (string.IsNullOrWhiteSpace(userId))
		{
			return [];
		}

		var tmdbIds = new HashSet<int>();
		var startIndex = 0;
		const int pageSize = 200;

		while (true)
		{
			var query = $"Users/{Uri.EscapeDataString(userId)}/Items?IncludeItemTypes=Movie&Recursive=true&Fields=ProviderIds&StartIndex={startIndex}&Limit={pageSize}";
			var uri = BuildApiUri(connection.BaseUrl, query);
			using var response = await SendAsync(connection, HttpMethod.Get, uri, content: null, cancellationToken).ConfigureAwait(false);
			if (!response.IsSuccessStatusCode)
			{
				throw new HttpRequestException($"Emby request failed. Status {(int)response.StatusCode} {response.StatusCode}.", null, response.StatusCode);
			}

			var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
			var dto = JsonSerializer.Deserialize<ItemsResponseDto>(body, Json) ?? new ItemsResponseDto([], 0);

			foreach (var item in dto.Items)
			{
				var providerIds = item.ProviderIds;
				if (providerIds is null)
				{
					continue;
				}

				if (providerIds.TryGetValue("Tmdb", out var tmdb) || providerIds.TryGetValue("tmdb", out tmdb))
				{
					if (int.TryParse(tmdb, out var id) && id > 0)
					{
						tmdbIds.Add(id);
					}
				}
			}

			startIndex += dto.Items.Count;
			if (dto.Items.Count < pageSize)
			{
				break;
			}
		}

		return tmdbIds.ToList();
	}

	private async Task<string?> ResolveUserIdAsync(EmbyConnection connection, CancellationToken cancellationToken)
	{
		var uri = BuildApiUri(connection.BaseUrl, "Users");
		using var response = await SendAsync(connection, HttpMethod.Get, uri, content: null, cancellationToken).ConfigureAwait(false);
		if (!response.IsSuccessStatusCode)
		{
			throw new HttpRequestException($"Emby request failed. Status {(int)response.StatusCode} {response.StatusCode}.", null, response.StatusCode);
		}

		var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
		var users = JsonSerializer.Deserialize<List<UserDto>>(body, Json) ?? [];
		if (users.Count == 0)
		{
			return null;
		}

		var admin = users.FirstOrDefault(u => u.Policy?.IsAdministrator == true);
		return (admin ?? users[0]).Id?.Trim();
	}

	private async Task<HttpResponseMessage> SendAsync(
		EmbyConnection connection,
		HttpMethod method,
		Uri uri,
		HttpContent? content,
		CancellationToken cancellationToken)
	{
		using var request = new HttpRequestMessage(method, uri);
		request.Headers.Accept.ParseAdd("application/json");
		request.Headers.Add("X-Emby-Token", connection.ApiKey);
		if (content is not null)
		{
			request.Content = content;
		}

		return await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
	}

	private static Uri BuildApiUri(string baseUrl, string pathAndQuery)
	{
		var normalized = NormalizeBaseUrl(baseUrl);
		var trimmed = (pathAndQuery ?? string.Empty).TrimStart('/');
		return new Uri($"{normalized}{trimmed}");
	}

	private static string NormalizeBaseUrl(string baseUrl)
	{
		var trimmed = (baseUrl ?? string.Empty).Trim();
		trimmed = trimmed.TrimEnd('/');
		if (string.IsNullOrWhiteSpace(trimmed))
		{
			return string.Empty;
		}

		// Emby expects the API surface under /emby/{apipath}.
		// Allow users to configure either `http(s)://host:port` OR `http(s)://host:port/emby`.
		if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
		{
			var absolutePath = (uri.AbsolutePath ?? string.Empty).TrimEnd('/');
			var endsWithEmby = absolutePath.EndsWith("/emby", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(absolutePath, "emby", StringComparison.OrdinalIgnoreCase);

			return endsWithEmby
				? trimmed + "/"
				: trimmed + "/emby/";
		}

		return trimmed.EndsWith("/emby", StringComparison.OrdinalIgnoreCase)
			? trimmed + "/"
			: trimmed + "/emby/";
	}

	private sealed record SystemInfoDto(
		[property: JsonPropertyName("Id")] string? Id,
		[property: JsonPropertyName("ServerName")] string? ServerName,
		[property: JsonPropertyName("Version")] string? Version);

	private sealed record ItemsResponseDto(
		[property: JsonPropertyName("Items")] List<ItemDto> Items,
		[property: JsonPropertyName("TotalRecordCount")] int TotalRecordCount);

	private sealed record ItemDto([property: JsonPropertyName("ProviderIds")] Dictionary<string, string>? ProviderIds);

	private sealed record UserDto(
		[property: JsonPropertyName("Id")] string? Id,
		[property: JsonPropertyName("Policy")] UserPolicyDto? Policy);

	private sealed record UserPolicyDto([property: JsonPropertyName("IsAdministrator")] bool IsAdministrator);
}

