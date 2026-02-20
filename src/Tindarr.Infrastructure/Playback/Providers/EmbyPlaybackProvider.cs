using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Tindarr.Application.Abstractions.Persistence;
using Tindarr.Application.Interfaces.Playback;
using Tindarr.Domain.Common;

namespace Tindarr.Infrastructure.Playback.Providers;

public sealed class EmbyPlaybackProvider(
	IServiceSettingsRepository settingsRepo,
	ICastingSettingsRepository castingSettingsRepo,
	HttpClient httpClient,
	ILogger<EmbyPlaybackProvider> logger) : IDirectPlaybackProvider, ICastPlaybackProvider
{
	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

	public ServiceType ServiceType => ServiceType.Emby;

	public async Task<Uri?> TryBuildDirectMovieStreamUrlAsync(ServiceScope scope, int tmdbId, CancellationToken cancellationToken)
	{
		// Always use Tindarr as passthrough for Emby casting. Chromecast cannot send custom headers,
		// and Emby stream endpoints may not accept or may restrict api_key-in-query for direct URLs.
		// Routing through Tindarr ensures X-Emby-Token is sent in headers and media is served reliably.
		await Task.CompletedTask.ConfigureAwait(false);
		return null;
	}

	public async Task<UpstreamPlaybackRequest> BuildMovieStreamRequestAsync(ServiceScope scope, int tmdbId, CancellationToken cancellationToken)
	{
		var (baseUrl, apiKey, _userId, itemId, streamSelection) = await ResolveContextAsync(scope, tmdbId, cancellationToken).ConfigureAwait(false);

		var queryParts = new List<string>(capacity: 10)
		{
			$"api_key={Uri.EscapeDataString(apiKey)}",
			"Static=true"
		};
		if (streamSelection.AudioStreamIndex is not null)
		{
			queryParts.Add($"AudioStreamIndex={streamSelection.AudioStreamIndex.Value}");
		}
		if (streamSelection.SubtitleStreamIndex is not null)
		{
			queryParts.Add($"SubtitleStreamIndex={streamSelection.SubtitleStreamIndex.Value}");
		}
		if (!string.IsNullOrWhiteSpace(streamSelection.SubtitleMethod))
		{
			queryParts.Add($"SubtitleMethod={Uri.EscapeDataString(streamSelection.SubtitleMethod!)}");
		}

		var upstreamUri = new Uri($"{baseUrl}Videos/{Uri.EscapeDataString(itemId)}/stream?{string.Join("&", queryParts)}", UriKind.Absolute);
		return new UpstreamPlaybackRequest(
			Uri: upstreamUri,
			Headers: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
			{
				["X-Emby-Token"] = apiKey
			});
	}

	public async Task<UpstreamPlaybackRequest> BuildMovieCastStreamRequestAsync(ServiceScope scope, int tmdbId, CancellationToken cancellationToken)
	{
		var (baseUrl, apiKey, userId, itemId, streamSelection) = await ResolveContextAsync(scope, tmdbId, cancellationToken).ConfigureAwait(false);
		var mediaSourceId = await TryGetMediaSourceIdAsync(httpClient, baseUrl, apiKey, userId, itemId, cancellationToken).ConfigureAwait(false);

		// Chromecast audio decoding is more limited than Emby's normal web clients.
		// Force an mp4 container + AAC audio, and cap channels to stereo as a safe default.
		var queryParts = new List<string>(capacity: 16)
		{
			$"api_key={Uri.EscapeDataString(apiKey)}",
			"Static=false",
			"AudioCodec=aac",
			"MaxAudioChannels=2",
			$"DeviceId={Uri.EscapeDataString(BuildCastDeviceId(scope))}",
			"VideoCodec=h264",
			"MaxWidth=1920",
			"MaxHeight=1080",
			"VideoBitRate=8000000"
		};
		if (!string.IsNullOrWhiteSpace(mediaSourceId))
		{
			queryParts.Add($"MediaSourceId={Uri.EscapeDataString(mediaSourceId)}");
		}
		if (streamSelection.AudioStreamIndex is not null)
		{
			queryParts.Add($"AudioStreamIndex={streamSelection.AudioStreamIndex.Value}");
		}
		// IMPORTANT: MP4 cannot mux common text subtitle codecs (e.g. SRT/subrip).
		// Only request subtitles when we are burning them into video (Encode).
		if (streamSelection.SubtitleStreamIndex is not null
			&& string.Equals(streamSelection.SubtitleMethod, "Encode", StringComparison.OrdinalIgnoreCase))
		{
			queryParts.Add($"SubtitleStreamIndex={streamSelection.SubtitleStreamIndex.Value}");
			queryParts.Add("SubtitleMethod=Encode");
		}

		var upstreamUri = new Uri($"{baseUrl}Videos/{Uri.EscapeDataString(itemId)}/stream.mp4?{string.Join("&", queryParts)}", UriKind.Absolute);
		return new UpstreamPlaybackRequest(
			Uri: upstreamUri,
			Headers: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
			{
				["X-Emby-Token"] = apiKey
			});
	}

	private async Task<(string BaseUrl, string ApiKey, string UserId, string ItemId, StreamSelection StreamSelection)> ResolveContextAsync(
		ServiceScope scope,
		int tmdbId,
		CancellationToken cancellationToken)
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

		var castingSettings = await castingSettingsRepo.GetAsync(cancellationToken).ConfigureAwait(false);
		var streamSelection = await TrySelectStreamsAsync(httpClient, baseUrl, apiKey, userId, itemId, castingSettings, cancellationToken).ConfigureAwait(false);

		return (baseUrl, apiKey, userId, itemId, streamSelection);
	}

	private static string BuildCastDeviceId(ServiceScope scope)
	{
		var serverId = (scope.ServerId ?? string.Empty).Trim();
		if (string.IsNullOrWhiteSpace(serverId))
		{
			return "tindarr-cast";
		}

		return $"tindarr-cast:{serverId}";
	}

	private static async Task<string?> TryGetMediaSourceIdAsync(
		HttpClient client,
		string baseUrl,
		string apiKey,
		string userId,
		string itemId,
		CancellationToken cancellationToken)
	{
		var uri = new Uri($"{baseUrl}Items/{Uri.EscapeDataString(itemId)}/PlaybackInfo?UserId={Uri.EscapeDataString(userId)}", UriKind.Absolute);
		using var request = new HttpRequestMessage(HttpMethod.Get, uri);
		request.Headers.Accept.ParseAdd("application/json");
		request.Headers.Add("X-Emby-Token", apiKey);
		using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
		if (!response.IsSuccessStatusCode)
		{
			return null;
		}

		var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
		var dto = JsonSerializer.Deserialize<PlaybackInfoResponseDto>(json, Json);
		var id = dto?.MediaSources?.FirstOrDefault(ms => !string.IsNullOrWhiteSpace(ms.Id))?.Id;
		return string.IsNullOrWhiteSpace(id) ? null : id.Trim();
	}

	private sealed record StreamSelection(int? AudioStreamIndex, int? SubtitleStreamIndex, string? SubtitleMethod);

	private async Task<StreamSelection> TrySelectStreamsAsync(
		HttpClient client,
		string baseUrl,
		string apiKey,
		string userId,
		string itemId,
		CastingSettingsRecord? castingSettings,
		CancellationToken cancellationToken)
	{
		if (castingSettings is null)
		{
			return new StreamSelection(null, null, null);
		}

		var details = await TryGetItemDetailsAsync(client, baseUrl, apiKey, userId, itemId, cancellationToken).ConfigureAwait(false);
		if (details?.MediaStreams is not { Count: > 0 })
		{
			return new StreamSelection(null, null, null);
		}

		var audioIndex = SelectAudioStreamIndex(details.MediaStreams, castingSettings);
		var (subtitleIndex, subtitleMethod) = SelectSubtitleStream(details.MediaStreams, castingSettings);
		return new StreamSelection(audioIndex, subtitleIndex, subtitleMethod);
	}

	private static int? SelectAudioStreamIndex(List<MediaStreamDto> streams, CastingSettingsRecord settings)
	{
		var candidates = streams
			.Where(s => string.Equals(s.Type, "Audio", StringComparison.OrdinalIgnoreCase) && s.Index is not null)
			.ToList();
		if (candidates.Count == 0)
		{
			return null;
		}

		var preferredLang = NormalizeLanguage(settings.PreferredAudioLanguage);
		var fallbackLang = NormalizeLanguage(settings.AudioLanguageFallback);
		var preferredStyle = NormalizeOption(settings.PreferredAudioStyle);
		var fallbackStyle = NormalizeOption(settings.AudioFallback);
		var preferredKind = NormalizeOption(settings.PreferredAudioTrackKind);
		var fallbackKind = NormalizeOption(settings.AudioTrackKindFallback);

		MediaStreamDto? best = null;
		var bestScore = int.MinValue;
		foreach (var s in candidates)
		{
			var score = 0;
			var lang = NormalizeLanguage(s.Language);
			if (LanguageMatches(lang, preferredLang))
			{
				score += 100;
			}
			else if (LanguageMatches(lang, fallbackLang))
			{
				score += 50;
			}

			if (MatchesAudioStyle(s, preferredStyle) || MatchesAudioStyle(s, fallbackStyle))
			{
				score += 10;
			}

			if (MatchesAudioTrackKind(s, preferredKind))
			{
				score += 20;
			}
			else if (MatchesAudioTrackKind(s, fallbackKind))
			{
				score += 10;
			}

			if (s.IsDefault == true)
			{
				score += 5;
			}

			if (score > bestScore)
			{
				best = s;
				bestScore = score;
			}
		}

		return best?.Index;
	}

	private static bool MatchesAudioTrackKind(MediaStreamDto stream, string? kind)
	{
		if (string.IsNullOrWhiteSpace(kind) || string.Equals(kind, "auto", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		var lower = kind.Trim().ToLowerInvariant();
		var isCommentary = stream.IsCommentary == true ||
			((stream.Title ?? stream.DisplayTitle) ?? string.Empty)
				.Contains("commentary", StringComparison.OrdinalIgnoreCase);

		return lower switch
		{
			"commentary" => isCommentary,
			"main" => !isCommentary,
			_ => false
		};
	}

	private static (int? Index, string? SubtitleMethod) SelectSubtitleStream(List<MediaStreamDto> streams, CastingSettingsRecord settings)
	{
		var mode = NormalizeOption(settings.PreferredSubtitleSource);
		if (string.Equals(mode, "none", StringComparison.OrdinalIgnoreCase))
		{
			return (null, null);
		}

		var preferredLang = NormalizeLanguage(settings.PreferredSubtitleLanguage);
		var fallbackLang = NormalizeLanguage(settings.SubtitleLanguageFallback);
		var preferredSource = NormalizeOption(settings.PreferredSubtitleTrackSource);
		var fallbackSource = NormalizeOption(settings.SubtitleTrackSourceFallback);

		var candidates = streams
			.Where(s => string.Equals(s.Type, "Subtitle", StringComparison.OrdinalIgnoreCase) && s.Index is not null)
			.ToList();
		if (candidates.Count == 0)
		{
			return (null, null);
		}

		MediaStreamDto? best = null;
		var bestScore = int.MinValue;
		foreach (var s in candidates)
		{
			var score = 0;
			var lang = NormalizeLanguage(s.Language);
			if (LanguageMatches(lang, preferredLang))
			{
				score += 100;
			}
			else if (LanguageMatches(lang, fallbackLang))
			{
				score += 50;
			}

			if (MatchesSubtitleSource(s, preferredSource) || MatchesSubtitleSource(s, fallbackSource))
			{
				score += 10;
			}

			if (s.IsForced == true)
			{
				score += 2;
			}

			if (s.IsDefault == true)
			{
				score += 1;
			}

			if (score > bestScore)
			{
				best = s;
				bestScore = score;
			}
		}

		if (best?.Index is null)
		{
			return (null, null);
		}

		var burn = string.Equals(mode, "burn", StringComparison.OrdinalIgnoreCase);
		if (burn)
		{
			return (best.Index, "Encode");
		}

		if (best.IsExternal == true)
		{
			return (best.Index, "External");
		}

		return (best.Index, "Embed");
	}

	private static bool MatchesSubtitleSource(MediaStreamDto stream, string? source)
	{
		if (string.IsNullOrWhiteSpace(source) || string.Equals(source, "auto", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		if (string.Equals(source, "external", StringComparison.OrdinalIgnoreCase))
		{
			return stream.IsExternal == true;
		}

		if (string.Equals(source, "embedded", StringComparison.OrdinalIgnoreCase))
		{
			return stream.IsExternal != true;
		}

		return false;
	}

	private static bool MatchesAudioStyle(MediaStreamDto stream, string? style)
	{
		if (string.IsNullOrWhiteSpace(style) || string.Equals(style, "auto", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		var codec = (stream.Codec ?? string.Empty).Trim().ToLowerInvariant();
		var channels = stream.Channels;
		var s = style.Trim().ToLowerInvariant();
		return s switch
		{
			"dts" => codec is "dts" or "dca",
			"dd" or "dolbydigital" => codec is "ac3" or "eac3",
			"5.1" or "5_1" or "surround" or "surround5.1" => channels is not null && channels.Value >= 6,
			"2ch" or "2.0" or "2_0" or "stereo" or "stereo2.0" => channels is not null && channels.Value <= 2,
			_ => false
		};
	}

	private static string? NormalizeLanguage(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return null;
		}

		return value.Trim().Replace('_', '-').ToLowerInvariant();
	}

	private static string? NormalizeOption(string? value)
	{
		return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
	}

	private static bool LanguageMatches(string? streamLanguage, string? desiredLanguage)
	{
		if (string.IsNullOrWhiteSpace(streamLanguage) || string.IsNullOrWhiteSpace(desiredLanguage))
		{
			return false;
		}

		var stream = streamLanguage.Trim().ToLowerInvariant();
		var desired = desiredLanguage.Trim().ToLowerInvariant();
		if (string.Equals(stream, desired, StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		foreach (var variant in ExpandLanguageVariants(desired))
		{
			if (string.Equals(stream, variant, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}

		return false;
	}

	private static IEnumerable<string> ExpandLanguageVariants(string normalizedLanguage)
	{
		yield return normalizedLanguage;

		var baseCode = normalizedLanguage.Split('-', 2)[0];
		yield return baseCode;

		switch (baseCode)
		{
			case "en":
				yield return "eng";
				break;
			case "es":
				yield return "spa";
				break;
			case "fr":
				yield return "fra";
				yield return "fre";
				break;
			case "de":
				yield return "deu";
				yield return "ger";
				break;
			case "it":
				yield return "ita";
				break;
			case "pt":
				yield return "por";
				break;
			case "ru":
				yield return "rus";
				break;
			case "ja":
				yield return "jpn";
				break;
			case "ko":
				yield return "kor";
				break;
			case "zh":
				yield return "zho";
				yield return "chi";
				break;
			case "hi":
				yield return "hin";
				break;
			case "ar":
				yield return "ara";
				break;
		}
	}

	private static async Task<ItemDetailsDto?> TryGetItemDetailsAsync(
		HttpClient client,
		string baseUrl,
		string apiKey,
		string userId,
		string itemId,
		CancellationToken cancellationToken)
	{
		var uri = new Uri($"{baseUrl}Users/{Uri.EscapeDataString(userId)}/Items/{Uri.EscapeDataString(itemId)}?Fields=MediaStreams", UriKind.Absolute);
		using var request = new HttpRequestMessage(HttpMethod.Get, uri);
		request.Headers.Accept.ParseAdd("application/json");
		request.Headers.Add("X-Emby-Token", apiKey);
		using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
		if (!response.IsSuccessStatusCode)
		{
			return null;
		}

		var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
		return JsonSerializer.Deserialize<ItemDetailsDto>(json, Json);
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
		static bool HasRequestedTmdbId(ItemDto item, int requestedTmdbId)
		{
			if (item.ProviderIds is null || item.ProviderIds.Count == 0)
			{
				return false;
			}

			var expected = requestedTmdbId.ToString(CultureInfo.InvariantCulture);
			foreach (var kvp in item.ProviderIds)
			{
				var key = (kvp.Key ?? string.Empty).Trim();
				if (!key.Contains("tmdb", StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				var value = (kvp.Value ?? string.Empty).Trim();
				if (string.Equals(value, expected, StringComparison.OrdinalIgnoreCase)
					|| string.Equals(value, $"tmdb.{expected}", StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
			}

			return false;
		}

		var candidates = new[]
		{
			$"Users/{Uri.EscapeDataString(userId)}/Items?IncludeItemTypes=Movie&Recursive=true&Limit=10&Fields=ProviderIds&AnyProviderIdEquals=tmdb.{tmdbId}",
			$"Users/{Uri.EscapeDataString(userId)}/Items?IncludeItemTypes=Movie&Recursive=true&Limit=10&Fields=ProviderIds&AnyProviderIdEquals=Tmdb.{tmdbId}",
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
			var match = dto?.Items?.FirstOrDefault(i => i is not null && HasRequestedTmdbId(i, tmdbId));
			var id = match?.Id;
			if (!string.IsNullOrWhiteSpace(id))
			{
				return id.Trim();
			}
		}

		// Fallback: page through the user's movie library and locate an item whose ProviderIds contains the TMDB id.
		// This avoids incorrectly returning the library's first movie if server-side filtering is unavailable.
		const int pageSize = 200;
		const int maxToScan = 5000;
		for (var startIndex = 0; startIndex < maxToScan; startIndex += pageSize)
		{
			var query = $"Users/{Uri.EscapeDataString(userId)}/Items?IncludeItemTypes=Movie&Recursive=true&Fields=ProviderIds&StartIndex={startIndex}&Limit={pageSize}";
			var uri = new Uri($"{baseUrl}{query}", UriKind.Absolute);
			using var request = new HttpRequestMessage(HttpMethod.Get, uri);
			request.Headers.Accept.ParseAdd("application/json");
			request.Headers.Add("X-Emby-Token", apiKey);

			using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
			if (!response.IsSuccessStatusCode)
			{
				break;
			}

			var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
			var dto = JsonSerializer.Deserialize<ItemsResponseDto>(json, Json);
			if (dto?.Items is not { Count: > 0 })
			{
				break;
			}

			var match = dto.Items.FirstOrDefault(i => i is not null && HasRequestedTmdbId(i, tmdbId));
			var id = match?.Id;
			if (!string.IsNullOrWhiteSpace(id))
			{
				logger.LogDebug("emby item id lookup: found TMDB match via fallback scan. TmdbId={TmdbId} StartIndex={StartIndex}", tmdbId, startIndex);
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

	private sealed record ItemDto(
		[property: JsonPropertyName("Id")] string? Id,
		[property: JsonPropertyName("ProviderIds")] Dictionary<string, string>? ProviderIds);

	private sealed record PlaybackInfoResponseDto(
		[property: JsonPropertyName("MediaSources")] List<MediaSourceInfoDto>? MediaSources,
		[property: JsonPropertyName("PlaySessionId")] string? PlaySessionId,
		[property: JsonPropertyName("ErrorCode")] string? ErrorCode);

	private sealed record MediaSourceInfoDto([property: JsonPropertyName("Id")] string? Id);

	private sealed record ItemDetailsDto([property: JsonPropertyName("MediaStreams")] List<MediaStreamDto>? MediaStreams);

	private sealed record MediaStreamDto(
		[property: JsonPropertyName("Type")] string? Type,
		[property: JsonPropertyName("Index")] int? Index,
		[property: JsonPropertyName("Language")] string? Language,
		[property: JsonPropertyName("Codec")] string? Codec,
		[property: JsonPropertyName("Channels")] int? Channels,
		[property: JsonPropertyName("Title")] string? Title,
		[property: JsonPropertyName("DisplayTitle")] string? DisplayTitle,
		[property: JsonPropertyName("IsCommentary")] bool? IsCommentary,
		[property: JsonPropertyName("IsDefault")] bool? IsDefault,
		[property: JsonPropertyName("IsForced")] bool? IsForced,
		[property: JsonPropertyName("IsExternal")] bool? IsExternal);
}
