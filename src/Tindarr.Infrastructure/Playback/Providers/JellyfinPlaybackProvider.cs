using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Tindarr.Application.Abstractions.Persistence;
using Tindarr.Application.Interfaces.Playback;
using Tindarr.Domain.Common;

namespace Tindarr.Infrastructure.Playback.Providers;

public sealed class JellyfinPlaybackProvider(
	IServiceSettingsRepository settingsRepo,
	ICastingSettingsRepository castingSettingsRepo,
	HttpClient httpClient,
	ILogger<JellyfinPlaybackProvider> logger) : IDirectPlaybackProvider, ICastPlaybackProvider
{
	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

	public ServiceType ServiceType => ServiceType.Jellyfin;

	public async Task<Uri?> TryBuildDirectMovieStreamUrlAsync(ServiceScope scope, int tmdbId, CancellationToken cancellationToken)
	{
		// Direct URLs are only used for cast devices; prefer a cast-compatible request.
		var upstream = await BuildMovieCastStreamRequestAsync(scope, tmdbId, cancellationToken).ConfigureAwait(false);
		if (!upstream.Headers.TryGetValue("X-Emby-Token", out var token) || string.IsNullOrWhiteSpace(token))
		{
			return null;
		}

		// Cast devices can't send headers; include token in query.
		// Jellyfin/Emby commonly accept either api_key or X-Emby-Token as query param.
		var uri = AppendOrReplaceQuery(upstream.Uri, "api_key", token);
		uri = AppendOrReplaceQuery(uri, "X-Emby-Token", token);
		return uri;
	}

	public async Task<UpstreamPlaybackRequest> BuildMovieStreamRequestAsync(ServiceScope scope, int tmdbId, CancellationToken cancellationToken)
	{
		var (baseUrl, apiKey, _userId, itemId, streamSelection) = await ResolveContextAsync(scope, tmdbId, cancellationToken).ConfigureAwait(false);

		var queryParts = new List<string>(capacity: 10)
		{
			$"api_key={Uri.EscapeDataString(apiKey)}",
			"static=true"
		};
		if (streamSelection.AudioStreamIndex is not null)
		{
			queryParts.Add($"audioStreamIndex={streamSelection.AudioStreamIndex.Value}");
		}
		if (streamSelection.SubtitleStreamIndex is not null)
		{
			queryParts.Add($"subtitleStreamIndex={streamSelection.SubtitleStreamIndex.Value}");
		}
		if (!string.IsNullOrWhiteSpace(streamSelection.SubtitleMethod))
		{
			queryParts.Add($"subtitleMethod={Uri.EscapeDataString(streamSelection.SubtitleMethod!)}");
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
		var (baseUrl, apiKey, _userId, itemId, streamSelection) = await ResolveContextAsync(scope, tmdbId, cancellationToken).ConfigureAwait(false);

		// Chromecast audio decoding is more limited than Jellyfin's normal web clients.
		// Force an mp4 container + AAC audio, and cap channels to stereo as a safe default.
		var queryParts = new List<string>(capacity: 16)
		{
			$"api_key={Uri.EscapeDataString(apiKey)}",
			"static=false",
			"audioCodec=aac",
			"maxAudioChannels=2",
			"transcodingMaxAudioChannels=2"
		};
		if (streamSelection.AudioStreamIndex is not null)
		{
			queryParts.Add($"audioStreamIndex={streamSelection.AudioStreamIndex.Value}");
		}
		// IMPORTANT: MP4 cannot mux common text subtitle codecs (e.g. SRT/subrip).
		// Only request subtitles when we are burning them into video (Encode).
		if (streamSelection.SubtitleStreamIndex is not null
			&& string.Equals(streamSelection.SubtitleMethod, "Encode", StringComparison.OrdinalIgnoreCase))
		{
			queryParts.Add($"subtitleStreamIndex={streamSelection.SubtitleStreamIndex.Value}");
			queryParts.Add("subtitleMethod=Encode");
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

		var castingSettings = await castingSettingsRepo.GetAsync(cancellationToken).ConfigureAwait(false);
		var streamSelection = await TrySelectStreamsAsync(httpClient, baseUrl, apiKey, userId, itemId, castingSettings, cancellationToken).ConfigureAwait(false);

		return (baseUrl, apiKey, userId, itemId, streamSelection);
	}

	private static Uri AppendOrReplaceQuery(Uri uri, string key, string value)
	{
		var builder = new UriBuilder(uri);
		var all = ParseQuery(builder.Query);

		all[key] = value;
		builder.Query = string.Join("&", all.Select(kvp => $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));
		return builder.Uri;
	}

	private static Dictionary<string, string> ParseQuery(string? query)
	{
		var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		if (string.IsNullOrWhiteSpace(query))
		{
			return dict;
		}

		var q = query.Trim();
		if (q.StartsWith("?", StringComparison.Ordinal))
		{
			q = q[1..];
		}
		if (string.IsNullOrWhiteSpace(q))
		{
			return dict;
		}

		foreach (var part in q.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
		{
			var kv = part.Split('=', 2);
			if (kv.Length == 0 || string.IsNullOrWhiteSpace(kv[0]))
			{
				continue;
			}

			var k = Uri.UnescapeDataString(kv[0]);
			if (dict.ContainsKey(k))
			{
				continue;
			}

			var v = kv.Length == 2 ? Uri.UnescapeDataString(kv[1]) : string.Empty;
			dict[k] = v;
		}

		return dict;
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

		// If not burning, choose a delivery method that matches source where possible.
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
		// Best-effort: Jellyfin/Emby often report ISO-639-2 (3-letter), while UI inputs are ISO-639-1 (2-letter).
		// Keep this small and targeted to our top language list.
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

	private static string? NormalizeOption(string? value)
	{
		return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
	}

	private static async Task<ItemDetailsDto?> TryGetItemDetailsAsync(
		HttpClient client,
		string baseUrl,
		string apiKey,
		string userId,
		string itemId,
		CancellationToken cancellationToken)
	{
		// Prefer user-scoped endpoint to ensure permissions and consistent response.
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

		// Jellyfin: query the library using the documented /Items endpoint and scan for ProviderIds.Tmdb.
		// This avoids relying on undocumented provider-id query operators that may be unsupported.
		const int pageSize = 200;
		const int maxToScan = 5000;
		for (var startIndex = 0; startIndex < maxToScan; startIndex += pageSize)
		{
			var query = $"Items?userId={Uri.EscapeDataString(userId)}&includeItemTypes=Movie&recursive=true&hasTmdbId=true&fields=ProviderIds&startIndex={startIndex}&limit={pageSize}";
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
				logger.LogDebug("jellyfin item id lookup: found TMDB match. TmdbId={TmdbId} StartIndex={StartIndex}", tmdbId, startIndex);
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

	private sealed record ItemDto(
		[property: JsonPropertyName("Id")] string? Id,
		[property: JsonPropertyName("ProviderIds")] Dictionary<string, string>? ProviderIds);

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
