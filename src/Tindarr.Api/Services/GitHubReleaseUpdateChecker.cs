using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Tindarr.Application.Common;
using Tindarr.Application.Options;

namespace Tindarr.Api.Services;

public sealed class GitHubReleaseUpdateChecker(
	HttpClient http,
	IMemoryCache cache,
	IOptions<UpdateCheckOptions> optionsAccessor,
	ILogger<GitHubReleaseUpdateChecker> logger) : IUpdateChecker
{
	private readonly UpdateCheckOptions options = optionsAccessor.Value;

	private const string CacheKey = "updatecheck:github:latest";

	public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken)
	{
		var checkedAtUtc = DateTimeOffset.UtcNow.ToString("O");
		var current = TindarrVersion.Current;
		var currentText = current.ToString(3);

		if (options.CacheMinutes > 0 && cache.TryGetValue(CacheKey, out UpdateCheckResult? cached) && cached is not null)
		{
			return cached with { CheckedAtUtc = checkedAtUtc };
		}

		try
		{
			GitHubReleaseDto? latest = null;
			if (!options.IncludePreReleases)
			{
				latest = await GetLatestReleaseViaLatestEndpointAsync(cancellationToken).ConfigureAwait(false);
			}
			else
			{
				latest = await GetLatestReleaseViaListEndpointAsync(includePreReleases: true, cancellationToken).ConfigureAwait(false);
			}

			if (latest is null)
			{
				return CacheAndReturn(new UpdateCheckResult(
					CurrentVersion: currentText,
					LatestVersion: null,
					UpdateAvailable: false,
					CheckedAtUtc: checkedAtUtc,
					LatestReleaseUrl: null,
					LatestReleaseName: null,
					PublishedAtUtc: null,
					IsPreRelease: null,
					ReleaseNotes: null,
					Error: "No releases found."));
			}

			if (!GitHubReleaseTagVersionParser.TryParse(latest.TagName, out var latestVer))
			{
				return CacheAndReturn(new UpdateCheckResult(
					CurrentVersion: currentText,
					LatestVersion: latest.TagName,
					UpdateAvailable: false,
					CheckedAtUtc: checkedAtUtc,
					LatestReleaseUrl: latest.HtmlUrl,
					LatestReleaseName: latest.Name,
					PublishedAtUtc: latest.PublishedAt?.ToString("O"),
					IsPreRelease: latest.PreRelease,
					ReleaseNotes: latest.Body,
					Error: $"Latest release tag '{latest.TagName}' is not a version."));
			}

			var updateAvailable = latestVer > current;
			return CacheAndReturn(new UpdateCheckResult(
				CurrentVersion: currentText,
				LatestVersion: latestVer.ToString(3),
				UpdateAvailable: updateAvailable,
				CheckedAtUtc: checkedAtUtc,
				LatestReleaseUrl: latest.HtmlUrl,
				LatestReleaseName: latest.Name,
				PublishedAtUtc: latest.PublishedAt?.ToString("O"),
				IsPreRelease: latest.PreRelease,
				ReleaseNotes: latest.Body,
				Error: null));
		}
		catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			return CacheAndReturn(new UpdateCheckResult(
				CurrentVersion: currentText,
				LatestVersion: null,
				UpdateAvailable: false,
				CheckedAtUtc: checkedAtUtc,
				LatestReleaseUrl: null,
				LatestReleaseName: null,
				PublishedAtUtc: null,
				IsPreRelease: null,
				ReleaseNotes: null,
				Error: "Update check timed out."));
		}
		catch (Exception ex)
		{
			logger.LogWarning(ex, "Update check failed.");
			return CacheAndReturn(new UpdateCheckResult(
				CurrentVersion: currentText,
				LatestVersion: null,
				UpdateAvailable: false,
				CheckedAtUtc: checkedAtUtc,
				LatestReleaseUrl: null,
				LatestReleaseName: null,
				PublishedAtUtc: null,
				IsPreRelease: null,
				ReleaseNotes: null,
				Error: "Update check failed."));
		}
	}

	private UpdateCheckResult CacheAndReturn(UpdateCheckResult result)
	{
		if (options.CacheMinutes > 0)
		{
			cache.Set(CacheKey, result, TimeSpan.FromMinutes(options.CacheMinutes));
		}

		return result;
	}

	private async Task<GitHubReleaseDto?> GetLatestReleaseViaLatestEndpointAsync(CancellationToken cancellationToken)
	{
		var path = $"repos/{options.RepositoryOwner}/{options.RepositoryName}/releases/latest";
		using var request = new HttpRequestMessage(HttpMethod.Get, path);
		using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

		if (response.StatusCode == HttpStatusCode.NotFound)
		{
			// No releases.
			return null;
		}
		if (!response.IsSuccessStatusCode)
		{
			throw new HttpRequestException($"GitHub latest release request failed: {(int)response.StatusCode} {response.ReasonPhrase}");
		}

		await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
		return await JsonSerializer.DeserializeAsync<GitHubReleaseDto>(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
	}

	private async Task<GitHubReleaseDto?> GetLatestReleaseViaListEndpointAsync(bool includePreReleases, CancellationToken cancellationToken)
	{
		var path = $"repos/{options.RepositoryOwner}/{options.RepositoryName}/releases?per_page=10";
		using var request = new HttpRequestMessage(HttpMethod.Get, path);
		using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

		if (response.StatusCode == HttpStatusCode.NotFound)
		{
			return null;
		}
		if (!response.IsSuccessStatusCode)
		{
			throw new HttpRequestException($"GitHub releases list request failed: {(int)response.StatusCode} {response.ReasonPhrase}");
		}

		await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
		var items = await JsonSerializer.DeserializeAsync<List<GitHubReleaseDto>>(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
		if (items is null || items.Count == 0)
		{
			return null;
		}

		foreach (var rel in items)
		{
			if (rel.Draft == true)
			{
				continue;
			}
			if (!includePreReleases && rel.PreRelease == true)
			{
				continue;
			}
			return rel;
		}

		return null;
	}

	private sealed record GitHubReleaseDto(
		[property: JsonPropertyName("tag_name")] string? TagName,
		[property: JsonPropertyName("html_url")] string? HtmlUrl,
		[property: JsonPropertyName("name")] string? Name,
		[property: JsonPropertyName("published_at")] DateTimeOffset? PublishedAt,
		[property: JsonPropertyName("prerelease")] bool? PreRelease,
		[property: JsonPropertyName("draft")] bool? Draft,
		[property: JsonPropertyName("body")] string? Body);
}
