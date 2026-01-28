using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Tindarr.Application.Abstractions.Integrations;

namespace Tindarr.Infrastructure.Integrations.Radarr;

public sealed class RadarrClient(HttpClient httpClient, ILogger<RadarrClient> logger) : IRadarrClient
{
	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

	public async Task<RadarrConnectionTestResult> TestConnectionAsync(RadarrConnection connection, CancellationToken cancellationToken)
	{
		try
		{
			var uri = BuildApiUri(connection.BaseUrl, "system/status");
			using var response = await SendAsync(connection, HttpMethod.Get, uri, content: null, cancellationToken).ConfigureAwait(false);
			return response.IsSuccessStatusCode
				? new RadarrConnectionTestResult(true, null)
				: new RadarrConnectionTestResult(false, $"Status {(int)response.StatusCode} {response.StatusCode}.");
		}
		catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
		{
			logger.LogWarning(ex, "radarr connection test failed. BaseUrl={BaseUrl}", connection.BaseUrl);
			return new RadarrConnectionTestResult(false, "Unable to reach Radarr.");
		}
	}

	public async Task<IReadOnlyList<RadarrQualityProfile>> GetQualityProfilesAsync(RadarrConnection connection, CancellationToken cancellationToken)
	{
		var body = await ReadResponseAsync(connection, "qualityprofile", cancellationToken).ConfigureAwait(false);
		var profiles = JsonSerializer.Deserialize<List<RadarrQualityProfileDto>>(body, Json) ?? [];
		return profiles
			.Where(p => p.Id > 0 && !string.IsNullOrWhiteSpace(p.Name))
			.Select(p => new RadarrQualityProfile(p.Id, p.Name!.Trim()))
			.ToList();
	}

	public async Task<IReadOnlyList<RadarrRootFolder>> GetRootFoldersAsync(RadarrConnection connection, CancellationToken cancellationToken)
	{
		var body = await ReadResponseAsync(connection, "rootfolder", cancellationToken).ConfigureAwait(false);
		var folders = JsonSerializer.Deserialize<List<RadarrRootFolderDto>>(body, Json) ?? [];
		return folders
			.Where(f => f.Id > 0 && !string.IsNullOrWhiteSpace(f.Path))
			.Select(f => new RadarrRootFolder(f.Id, f.Path!.Trim(), f.FreeSpace))
			.ToList();
	}

	public async Task<IReadOnlyList<RadarrLibraryMovie>> GetLibraryAsync(RadarrConnection connection, CancellationToken cancellationToken)
	{
		var body = await ReadResponseAsync(connection, "movie", cancellationToken).ConfigureAwait(false);
		var movies = JsonSerializer.Deserialize<List<RadarrLibraryMovieDto>>(body, Json) ?? [];
		return movies
			.Where(m => m.TmdbId > 0)
			.Select(m => new RadarrLibraryMovie(m.TmdbId, m.Id, m.Title ?? $"TMDB:{m.TmdbId}"))
			.ToList();
	}

	public async Task<RadarrLookupMovie?> LookupMovieAsync(RadarrConnection connection, int tmdbId, CancellationToken cancellationToken)
	{
		if (tmdbId <= 0)
		{
			return null;
		}

		var body = await ReadResponseAsync(connection, $"movie/lookup/tmdb?tmdbId={tmdbId}", cancellationToken).ConfigureAwait(false);
		var movies = JsonSerializer.Deserialize<List<RadarrLookupMovieDto>>(body, Json);
		var first = movies?.FirstOrDefault();
		if (first is null)
		{
			return null;
		}

		var images = first.Images?
			.Select(i => new RadarrLookupImage(i.CoverType ?? "poster", i.Url, i.RemoteUrl))
			.ToList() ?? [];

		return new RadarrLookupMovie(
			first.TmdbId,
			first.Title ?? $"TMDB:{first.TmdbId}",
			first.TitleSlug,
			first.Year,
			images);
	}

	public async Task<int?> EnsureTagAsync(RadarrConnection connection, string tagLabel, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(tagLabel))
		{
			return null;
		}

		var normalized = tagLabel.Trim();
		try
		{
			var body = await ReadResponseAsync(connection, "tag", cancellationToken).ConfigureAwait(false);
			var tags = JsonSerializer.Deserialize<List<RadarrTagDto>>(body, Json) ?? [];
			var existing = tags.FirstOrDefault(t => string.Equals(t.Label, normalized, StringComparison.OrdinalIgnoreCase));
			if (existing is not null)
			{
				return existing.Id > 0 ? existing.Id : null;
			}

			var payload = JsonSerializer.Serialize(new { label = normalized }, Json);
			using var content = new StringContent(payload, Encoding.UTF8, "application/json");
			using var response = await SendAsync(connection, HttpMethod.Post, BuildApiUri(connection.BaseUrl, "tag"), content, cancellationToken).ConfigureAwait(false);
			if (!response.IsSuccessStatusCode)
			{
				return null;
			}

			var createdBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
			var created = JsonSerializer.Deserialize<RadarrTagDto>(createdBody, Json);
			return created?.Id > 0 ? created.Id : null;
		}
		catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
		{
			logger.LogWarning(ex, "radarr tag ensure failed. BaseUrl={BaseUrl}", connection.BaseUrl);
			return null;
		}
	}

	public async Task<RadarrAddMovieResult> AddMovieAsync(RadarrConnection connection, RadarrAddMovieRequest request, CancellationToken cancellationToken)
	{
		var payload = JsonSerializer.Serialize(new RadarrAddMoviePayload(
			Title: request.Lookup.Title,
			TitleSlug: request.Lookup.TitleSlug,
			TmdbId: request.Lookup.TmdbId,
			Year: request.Lookup.Year,
			QualityProfileId: request.QualityProfileId,
			RootFolderPath: request.RootFolderPath,
			Monitored: true,
			Images: request.Lookup.Images.Select(i => new RadarrImageDto(i.CoverType, i.Url, i.RemoteUrl)).ToList(),
			Tags: request.TagIds.ToList(),
			AddOptions: new RadarrAddOptionsDto(request.SearchForMovie)),
			Json);

		using var content = new StringContent(payload, Encoding.UTF8, "application/json");
		using var response = await SendAsync(connection, HttpMethod.Post, BuildApiUri(connection.BaseUrl, "movie"), content, cancellationToken).ConfigureAwait(false);

		if (response.IsSuccessStatusCode)
		{
			return new RadarrAddMovieResult(true, false, null);
		}

		var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
		if (response.StatusCode == HttpStatusCode.BadRequest && ContainsAlreadyExists(body))
		{
			return new RadarrAddMovieResult(false, true, "Movie already exists.");
		}

		return new RadarrAddMovieResult(false, false, $"Status {(int)response.StatusCode} {response.StatusCode}.");
	}

	private async Task<string> ReadResponseAsync(RadarrConnection connection, string path, CancellationToken cancellationToken)
	{
		var uri = BuildApiUri(connection.BaseUrl, path);
		using var response = await SendAsync(connection, HttpMethod.Get, uri, content: null, cancellationToken).ConfigureAwait(false);
		if (!response.IsSuccessStatusCode)
		{
			throw new HttpRequestException($"Radarr request failed. Status {(int)response.StatusCode} {response.StatusCode}.", null, response.StatusCode);
		}

		return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
	}

	private async Task<HttpResponseMessage> SendAsync(RadarrConnection connection, HttpMethod method, Uri uri, HttpContent? content, CancellationToken cancellationToken)
	{
		using var request = new HttpRequestMessage(method, uri);
		request.Headers.Accept.ParseAdd("application/json");
		request.Headers.Add("X-Api-Key", connection.ApiKey);
		if (content is not null)
		{
			request.Content = content;
		}

		return await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
	}

	private static Uri BuildApiUri(string baseUrl, string path)
	{
		var normalized = NormalizeBaseUrl(baseUrl);
		var trimmedPath = path.TrimStart('/');
		return new Uri($"{normalized}{trimmedPath}");
	}

	private static string NormalizeBaseUrl(string baseUrl)
	{
		var trimmed = (baseUrl ?? string.Empty).Trim();
		if (string.IsNullOrWhiteSpace(trimmed))
		{
			return trimmed;
		}

		trimmed = trimmed.TrimEnd('/');
		if (!trimmed.EndsWith("/api/v3", StringComparison.OrdinalIgnoreCase))
		{
			trimmed += "/api/v3";
		}

		return trimmed.EndsWith('/') ? trimmed : trimmed + "/";
	}

	private static bool ContainsAlreadyExists(string body)
	{
		if (string.IsNullOrWhiteSpace(body))
		{
			return false;
		}

		return body.Contains("already exists", StringComparison.OrdinalIgnoreCase)
			|| body.Contains("movie exists", StringComparison.OrdinalIgnoreCase);
	}

	private sealed record RadarrQualityProfileDto(
		[property: JsonPropertyName("id")] int Id,
		[property: JsonPropertyName("name")] string? Name);

	private sealed record RadarrRootFolderDto(
		[property: JsonPropertyName("id")] int Id,
		[property: JsonPropertyName("path")] string? Path,
		[property: JsonPropertyName("freeSpace")] long? FreeSpace);

	private sealed record RadarrLibraryMovieDto(
		[property: JsonPropertyName("id")] int Id,
		[property: JsonPropertyName("tmdbId")] int TmdbId,
		[property: JsonPropertyName("title")] string? Title);

	private sealed record RadarrLookupMovieDto(
		[property: JsonPropertyName("tmdbId")] int TmdbId,
		[property: JsonPropertyName("title")] string? Title,
		[property: JsonPropertyName("titleSlug")] string? TitleSlug,
		[property: JsonPropertyName("year")] int? Year,
		[property: JsonPropertyName("images")] List<RadarrImageDto>? Images);

	private sealed record RadarrImageDto(
		[property: JsonPropertyName("coverType")] string? CoverType,
		[property: JsonPropertyName("url")] string? Url,
		[property: JsonPropertyName("remoteUrl")] string? RemoteUrl);

	private sealed record RadarrTagDto(
		[property: JsonPropertyName("id")] int Id,
		[property: JsonPropertyName("label")] string? Label);

	private sealed record RadarrAddMoviePayload(
		[property: JsonPropertyName("title")] string Title,
		[property: JsonPropertyName("titleSlug")] string? TitleSlug,
		[property: JsonPropertyName("tmdbId")] int TmdbId,
		[property: JsonPropertyName("year")] int? Year,
		[property: JsonPropertyName("qualityProfileId")] int QualityProfileId,
		[property: JsonPropertyName("rootFolderPath")] string RootFolderPath,
		[property: JsonPropertyName("monitored")] bool Monitored,
		[property: JsonPropertyName("images")] List<RadarrImageDto> Images,
		[property: JsonPropertyName("tags")] List<int> Tags,
		[property: JsonPropertyName("addOptions")] RadarrAddOptionsDto AddOptions);

	private sealed record RadarrAddOptionsDto([property: JsonPropertyName("searchForMovie")] bool SearchForMovie);
}
