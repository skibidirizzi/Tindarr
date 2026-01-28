using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tindarr.Application.Abstractions.Integrations;
using Tindarr.Application.Options;

namespace Tindarr.Infrastructure.Integrations.Plex;

public sealed class PlexLibraryClient(HttpClient httpClient, IOptions<PlexOptions> options, ILogger<PlexLibraryClient> logger) : IPlexLibraryClient
{
	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
	{
		PropertyNameCaseInsensitive = true
	};

	private readonly PlexOptions _options = options.Value;

	private static readonly Regex TmdbIdRegex = new(@"tmdb[^\d]*(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

	public async Task<IReadOnlyList<PlexLibrarySection>> GetMovieSectionsAsync(PlexServerConnectionInfo connection, CancellationToken cancellationToken)
	{
		var uri = BuildUri(connection.BaseUrl, "library/sections");
		using var response = await SendAsync(connection, HttpMethod.Get, uri, cancellationToken).ConfigureAwait(false);

		if (!response.IsSuccessStatusCode)
		{
			throw new HttpRequestException($"Plex library sections failed. Status {(int)response.StatusCode} {response.StatusCode}.", null, response.StatusCode);
		}

		var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
		var parsed = JsonSerializer.Deserialize<PlexContainer<PlexSectionsContainer>>(body, Json);
		var sections = parsed?.MediaContainer?.Directory ?? [];

		return sections
			.Where(s => !string.IsNullOrWhiteSpace(s.Key) && !string.IsNullOrWhiteSpace(s.Type))
			.Where(s => string.Equals(s.Type, "movie", StringComparison.OrdinalIgnoreCase))
			.Select(s => new PlexLibrarySection(
				Key: int.TryParse(s.Key, out var key) ? key : 0,
				Title: string.IsNullOrWhiteSpace(s.Title) ? s.Key ?? "Movies" : s.Title,
				Type: s.Type!))
			.Where(s => s.Key > 0)
			.ToList();
	}

	public async Task<IReadOnlyList<PlexLibraryItem>> GetLibraryItemsAsync(PlexServerConnectionInfo connection, int sectionKey, CancellationToken cancellationToken)
	{
		var uri = BuildUri(connection.BaseUrl, $"library/sections/{sectionKey}/all?includeGuids=1");
		using var response = await SendAsync(connection, HttpMethod.Get, uri, cancellationToken).ConfigureAwait(false);

		if (response.StatusCode == HttpStatusCode.NotFound)
		{
			return [];
		}

		if (!response.IsSuccessStatusCode)
		{
			throw new HttpRequestException($"Plex library fetch failed. Status {(int)response.StatusCode} {response.StatusCode}.", null, response.StatusCode);
		}

		var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
		var parsed = JsonSerializer.Deserialize<PlexContainer<PlexItemsContainer>>(body, Json);
		var items = parsed?.MediaContainer?.Metadata ?? [];

		var results = new List<PlexLibraryItem>(items.Count);
		foreach (var item in items)
		{
			if (item is null)
			{
				continue;
			}

			var guid = ExtractGuid(item);
			var tmdbId = TryParseTmdbId(item) ?? 0;
			var title = string.IsNullOrWhiteSpace(item.Title) ? $"TMDB:{tmdbId}" : item.Title!.Trim();

			if (tmdbId <= 0)
			{
				continue;
			}

			results.Add(new PlexLibraryItem(tmdbId, title, item.RatingKey, guid));
		}

		return results;
	}

	private async Task<HttpResponseMessage> SendAsync(PlexServerConnectionInfo connection, HttpMethod method, Uri uri, CancellationToken cancellationToken)
	{
		using var request = new HttpRequestMessage(method, uri);
		request.Headers.Accept.ParseAdd("application/json");
		request.Headers.Add("X-Plex-Client-Identifier", connection.ClientIdentifier);
		request.Headers.Add("X-Plex-Product", _options.Product);
		request.Headers.Add("X-Plex-Platform", _options.Platform);
		request.Headers.Add("X-Plex-Device", _options.Device);
		request.Headers.Add("X-Plex-Version", _options.Version);
		request.Headers.Add("X-Plex-Token", connection.AccessToken);

		try
		{
			return await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
		}
		catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
		{
			logger.LogWarning(ex, "plex library request failed. Uri={Uri}", uri);
			throw;
		}
	}

	private static Uri BuildUri(string baseUrl, string pathAndQuery)
	{
		var trimmedBase = (baseUrl ?? string.Empty).Trim().TrimEnd('/');
		var trimmedPath = pathAndQuery.TrimStart('/');
		return new Uri($"{trimmedBase}/{trimmedPath}", UriKind.Absolute);
	}

	private static int? TryParseTmdbId(PlexItemDto item)
	{
		foreach (var candidate in EnumerateGuids(item))
		{
			if (string.IsNullOrWhiteSpace(candidate))
			{
				continue;
			}

			if (!candidate.Contains("tmdb", StringComparison.OrdinalIgnoreCase)
				&& !candidate.Contains("themoviedb", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			var direct = ExtractDigitsAfterScheme(candidate);
			if (direct is not null)
			{
				return direct;
			}

			var match = TmdbIdRegex.Match(candidate);
			if (match.Success && int.TryParse(match.Groups[1].Value, out var id))
			{
				return id;
			}
		}

		return null;
	}

	private static int? ExtractDigitsAfterScheme(string candidate)
	{
		var idx = candidate.IndexOf("://", StringComparison.OrdinalIgnoreCase);
		if (idx < 0)
		{
			return null;
		}

		var remainder = candidate[(idx + 3)..];
		var digits = new string(remainder.TakeWhile(char.IsDigit).ToArray());
		return int.TryParse(digits, out var id) ? id : null;
	}

	private static IEnumerable<string?> EnumerateGuids(PlexItemDto item)
	{
		if (!string.IsNullOrWhiteSpace(item.Guid))
		{
			yield return item.Guid;
		}

		if (item.GuidList is not null)
		{
			foreach (var guid in item.GuidList)
			{
				yield return guid?.Id;
			}
		}
	}

	private static string? ExtractGuid(PlexItemDto item)
	{
		return EnumerateGuids(item).FirstOrDefault(g => !string.IsNullOrWhiteSpace(g));
	}

	private sealed record PlexContainer<T>(
		[property: JsonPropertyName("MediaContainer")] T? MediaContainer);

	private sealed record PlexSectionsContainer(
		[property: JsonPropertyName("Directory")] List<PlexSectionDto>? Directory);

	private sealed record PlexSectionDto(
		[property: JsonPropertyName("key")] string? Key,
		[property: JsonPropertyName("title")] string? Title,
		[property: JsonPropertyName("type")] string? Type);

	private sealed record PlexItemsContainer(
		[property: JsonPropertyName("Metadata")] List<PlexItemDto>? Metadata);

	private sealed record PlexItemDto(
		[property: JsonPropertyName("guid")] string? Guid,
		[property: JsonPropertyName("Guid")] List<PlexGuidDto>? GuidList,
		[property: JsonPropertyName("title")] string? Title,
		[property: JsonPropertyName("ratingKey")] string? RatingKey);

	private sealed record PlexGuidDto([property: JsonPropertyName("id")] string? Id);
}
