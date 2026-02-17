using System.Globalization;
using System.Net.Http;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tindarr.Application.Abstractions.Integrations;
using Tindarr.Application.Abstractions.Persistence;
using Tindarr.Application.Interfaces.Playback;
using Tindarr.Application.Options;
using Tindarr.Domain.Common;

namespace Tindarr.Infrastructure.Playback.Providers;

public sealed class PlexPlaybackProvider(
	IServiceSettingsRepository settingsRepo,
	ICastingSettingsRepository castingSettingsRepo,
	IPlexLibraryCacheRepository plexCache,
	HttpClient httpClient,
	IOptions<PlexOptions> plexOptions,
	ILogger<PlexPlaybackProvider> logger) : IPlaybackProvider
{
	private readonly PlexOptions _plexOptions = plexOptions.Value;

	// For casting, Plex must choose a playback profile that matches the actual receiver capabilities.
	// If we identify as an unknown platform/device, Plex refuses to pick a profile and will not transcode.
	private const string ChromecastPlatform = "Chromecast";
	private const string ChromecastDevice = "Chromecast";
	private const string ChromecastModel = "Chromecast";
	private const string ChromecastPlatformVersion = "1";

	public ServiceType ServiceType => ServiceType.Plex;

	public async Task<UpstreamPlaybackRequest> BuildMovieStreamRequestAsync(ServiceScope scope, int tmdbId, CancellationToken cancellationToken)
	{
		if (tmdbId <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(tmdbId));
		}

		var server = await settingsRepo.GetAsync(scope, cancellationToken).ConfigureAwait(false);
		if (server is null || string.IsNullOrWhiteSpace(server.PlexServerUri))
		{
			throw new InvalidOperationException("Plex server is not configured.");
		}

		var account = await settingsRepo.GetAsync(new ServiceScope(ServiceType.Plex, PlexConstants.AccountServerId), cancellationToken).ConfigureAwait(false);
		var accessToken = !string.IsNullOrWhiteSpace(server.PlexServerAccessToken)
			? server.PlexServerAccessToken!
			: account?.PlexAuthToken;

		if (string.IsNullOrWhiteSpace(accessToken))
		{
			throw new InvalidOperationException("Plex is not authenticated.");
		}

		var ratingKey = await plexCache.TryGetRatingKeyAsync(scope, tmdbId, cancellationToken).ConfigureAwait(false);
		if (string.IsNullOrWhiteSpace(ratingKey))
		{
			throw new InvalidOperationException("Plex item not found in cache. Sync Plex library first.");
		}

		var clientIdentifier = server.PlexClientIdentifier ?? account?.PlexClientIdentifier;
		if (string.IsNullOrWhiteSpace(clientIdentifier))
		{
			throw new InvalidOperationException("Plex client identifier is not configured.");
		}

		var castingSettings = await castingSettingsRepo.GetAsync(cancellationToken).ConfigureAwait(false);
		var streamSelection = await TrySelectStreamsAsync(
			httpClient,
			NormalizeBaseUrl(server.PlexServerUri!),
			accessToken,
			clientIdentifier.Trim(),
			ratingKey.Trim(),
			castingSettings,
			logger,
			cancellationToken).ConfigureAwait(false);

		// IMPORTANT:
		// Do NOT stream Plex Part.Key directly (or with download=1).
		// Chromecast frequently can't decode the source audio codec (e.g. DTS), resulting in silent playback.
		// Use Plex's universal transcode endpoint to force a Chromecast-friendly container/audio.
		var baseUrl = NormalizeBaseUrl(server.PlexServerUri!);
		var path = $"/library/metadata/{ratingKey.Trim()}";
		var queryParts = new List<string>(capacity: 16)
		{
			$"path={Uri.EscapeDataString(path)}",
			"mediaIndex=0",
			"partIndex=0",
			"protocol=http",
			"format=mp4",
			"directPlay=0",
			"directStream=1",
			"fastSeek=1",
			$"session={Uri.EscapeDataString(clientIdentifier.Trim())}"
		};

		ApplyCastingPolicy(queryParts, castingSettings, logger);

		if (streamSelection.AudioStreamId is not null)
		{
			queryParts.Add($"audioStreamID={streamSelection.AudioStreamId.Value.ToString(CultureInfo.InvariantCulture)}");
			logger.LogDebug("Applied Plex audioStreamID selection. Id={Id}", streamSelection.AudioStreamId);
		}

		var subtitleMode = NormalizePolicyValue(castingSettings?.PreferredSubtitleSource);
		if (!string.Equals(subtitleMode, "none", StringComparison.OrdinalIgnoreCase) && streamSelection.SubtitleStreamId is not null)
		{
			queryParts.Add($"subtitleStreamID={streamSelection.SubtitleStreamId.Value.ToString(CultureInfo.InvariantCulture)}");
			logger.LogDebug("Applied Plex subtitleStreamID selection. Id={Id}", streamSelection.SubtitleStreamId);
		}

		var upstreamUri = new Uri(
			$"{baseUrl}video/:/transcode/universal/start?{string.Join("&", queryParts)}",
			UriKind.Absolute);

		logger.LogDebug("plex upstream stream request built. TmdbId={TmdbId} RatingKey={RatingKey} Uri={Uri}", tmdbId, ratingKey, upstreamUri);

		var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			["X-Plex-Token"] = accessToken,
			["X-Plex-Client-Identifier"] = clientIdentifier.Trim(),
			["X-Plex-Product"] = _plexOptions.Product,
			// Identify as Chromecast so Plex can choose a matching client profile for audio/video compatibility.
			["X-Plex-Platform"] = ChromecastPlatform,
			["X-Plex-Platform-Version"] = ChromecastPlatformVersion,
			["X-Plex-Device"] = ChromecastDevice,
			["X-Plex-Model"] = ChromecastModel,
			["X-Plex-Version"] = _plexOptions.Version,
		};

		var acceptLanguage = ResolveAcceptLanguage(castingSettings);
		if (!string.IsNullOrWhiteSpace(acceptLanguage))
		{
			headers["Accept-Language"] = acceptLanguage;
		}

		return new UpstreamPlaybackRequest(
			Uri: upstreamUri,
			Headers: headers);
	}

	private sealed record PlexStreamSelection(int? AudioStreamId, int? SubtitleStreamId);

	private static async Task<PlexStreamSelection> TrySelectStreamsAsync(
		HttpClient client,
		string baseUrl,
		string accessToken,
		string clientIdentifier,
		string ratingKey,
		CastingSettingsRecord? castingSettings,
		ILogger logger,
		CancellationToken cancellationToken)
	{
		if (castingSettings is null)
		{
			return new PlexStreamSelection(null, null);
		}

		try
		{
			// includeExternalMedia=1 helps ensure external subtitles are returned in the stream list.
			var uri = new Uri($"{baseUrl}library/metadata/{Uri.EscapeDataString(ratingKey)}?includeExternalMedia=1", UriKind.Absolute);
			using var request = new HttpRequestMessage(HttpMethod.Get, uri);
			request.Headers.Accept.ParseAdd("application/xml");
			request.Headers.Add("X-Plex-Token", accessToken);
			request.Headers.Add("X-Plex-Client-Identifier", clientIdentifier);

			using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
			if (!response.IsSuccessStatusCode)
			{
				logger.LogDebug("Plex metadata request failed for stream selection. Status={StatusCode}", (int)response.StatusCode);
				return new PlexStreamSelection(null, null);
			}

			var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
			if (string.IsNullOrWhiteSpace(body))
			{
				return new PlexStreamSelection(null, null);
			}

			var (audioStreams, subtitleStreams) = TryParseStreamsFromMetadataXml(body);
			if (audioStreams.Count == 0 && subtitleStreams.Count == 0)
			{
				return new PlexStreamSelection(null, null);
			}

			var audioId = SelectAudioStreamId(audioStreams, castingSettings);
			var subtitleId = SelectSubtitleStreamId(subtitleStreams, castingSettings);
			return new PlexStreamSelection(audioId, subtitleId);
		}
		catch (Exception ex)
		{
			logger.LogDebug(ex, "Plex stream selection failed; falling back to best-effort parameters.");
			return new PlexStreamSelection(null, null);
		}
	}

	private sealed record PlexAudioStream(
		int Id,
		string? Language,
		string? Codec,
		int? Channels,
		string? Title,
		string? DisplayTitle,
		bool IsDefault);

	private sealed record PlexSubtitleStream(
		int Id,
		string? Language,
		string? Location,
		bool IsForced,
		bool IsDefault);

	private static (List<PlexAudioStream> Audio, List<PlexSubtitleStream> Subtitles) TryParseStreamsFromMetadataXml(string body)
	{
		var doc = XDocument.Parse(body);
		var root = doc.Root;
		if (root is null)
		{
			return ([], []);
		}

		// We request mediaIndex=0/partIndex=0 when streaming, so we mirror that here.
		var media = root.Descendants().FirstOrDefault(e => string.Equals(e.Name.LocalName, "Media", StringComparison.OrdinalIgnoreCase));
		var part = media?.Elements().FirstOrDefault(e => string.Equals(e.Name.LocalName, "Part", StringComparison.OrdinalIgnoreCase));
		if (part is null)
		{
			return ([], []);
		}

		var audio = new List<PlexAudioStream>();
		var subtitles = new List<PlexSubtitleStream>();

		foreach (var stream in part.Elements().Where(e => string.Equals(e.Name.LocalName, "Stream", StringComparison.OrdinalIgnoreCase)))
		{
			var streamType = TryParseInt(stream.Attribute("streamType"));
			var id = TryParseInt(stream.Attribute("id"));
			if (streamType is null || id is null)
			{
				continue;
			}

			var language = (string?)stream.Attribute("languageTag")
				?? (string?)stream.Attribute("languageCode")
				?? (string?)stream.Attribute("language");

			var isDefault = ParseXmlBool(stream.Attribute("default"));

			if (streamType.Value == 2)
			{
				audio.Add(new PlexAudioStream(
					Id: id.Value,
					Language: language,
					Codec: (string?)stream.Attribute("codec"),
					Channels: TryParseInt(stream.Attribute("channels")),
					Title: (string?)stream.Attribute("title"),
					DisplayTitle: (string?)stream.Attribute("displayTitle") ?? (string?)stream.Attribute("extendedDisplayTitle"),
					IsDefault: isDefault));
			}
			else if (streamType.Value == 3)
			{
				subtitles.Add(new PlexSubtitleStream(
					Id: id.Value,
					Language: language,
					Location: (string?)stream.Attribute("location"),
					IsForced: ParseXmlBool(stream.Attribute("forced")),
					IsDefault: isDefault));
			}
		}

		return (audio, subtitles);
	}

	private static int? SelectAudioStreamId(List<PlexAudioStream> streams, CastingSettingsRecord settings)
	{
		if (streams.Count == 0)
		{
			return null;
		}

		var preferredLang = NormalizeLanguage(settings.PreferredAudioLanguage);
		var fallbackLang = NormalizeLanguage(settings.AudioLanguageFallback);
		var preferredStyle = NormalizeOption(settings.PreferredAudioStyle);
		var fallbackStyle = NormalizeOption(settings.AudioFallback);
		var preferredKind = NormalizeOption(settings.PreferredAudioTrackKind);
		var fallbackKind = NormalizeOption(settings.AudioTrackKindFallback);

		PlexAudioStream? best = null;
		var bestScore = int.MinValue;
		foreach (var s in streams)
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

			if (s.IsDefault)
			{
				score += 5;
			}

			if (score > bestScore)
			{
				best = s;
				bestScore = score;
			}
		}

		return best?.Id;
	}

	private static int? SelectSubtitleStreamId(List<PlexSubtitleStream> streams, CastingSettingsRecord settings)
	{
		var mode = NormalizeOption(settings.PreferredSubtitleSource);
		if (string.Equals(mode, "none", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}

		if (streams.Count == 0)
		{
			return null;
		}

		var preferredLang = NormalizeLanguage(settings.PreferredSubtitleLanguage);
		var fallbackLang = NormalizeLanguage(settings.SubtitleLanguageFallback);
		var preferredSource = NormalizeOption(settings.PreferredSubtitleTrackSource);
		var fallbackSource = NormalizeOption(settings.SubtitleTrackSourceFallback);

		PlexSubtitleStream? best = null;
		var bestScore = int.MinValue;
		foreach (var s in streams)
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

			if (s.IsForced)
			{
				score += 2;
			}

			if (s.IsDefault)
			{
				score += 1;
			}

			if (score > bestScore)
			{
				best = s;
				bestScore = score;
			}
		}

		return best?.Id;
	}

	private static bool MatchesSubtitleSource(PlexSubtitleStream stream, string? source)
	{
		if (string.IsNullOrWhiteSpace(source) || string.Equals(source, "auto", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		var location = (stream.Location ?? string.Empty).Trim();
		var isExternal = location.Equals("external", StringComparison.OrdinalIgnoreCase);

		if (string.Equals(source, "external", StringComparison.OrdinalIgnoreCase))
		{
			return isExternal;
		}

		if (string.Equals(source, "embedded", StringComparison.OrdinalIgnoreCase))
		{
			return !isExternal;
		}

		return false;
	}

	private static bool MatchesAudioStyle(PlexAudioStream stream, string? style)
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

	private static bool MatchesAudioTrackKind(PlexAudioStream stream, string? kind)
	{
		if (string.IsNullOrWhiteSpace(kind) || string.Equals(kind, "auto", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		var lower = kind.Trim().ToLowerInvariant();
		var title = (stream.Title ?? stream.DisplayTitle) ?? string.Empty;
		var isCommentary = title.Contains("commentary", StringComparison.OrdinalIgnoreCase);
		return lower switch
		{
			"commentary" => isCommentary,
			"main" => !isCommentary,
			_ => false
		};
	}

	private static int? TryParseInt(XAttribute? attribute)
	{
		var raw = attribute?.Value;
		if (string.IsNullOrWhiteSpace(raw))
		{
			return null;
		}

		return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
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

	private static void ApplyCastingPolicy(List<string> queryParts, CastingSettingsRecord? settings, ILogger logger)
	{
		if (settings is null)
		{
			return;
		}

		ApplySubtitlePolicy(queryParts, settings.PreferredSubtitleSource, settings.SubtitleFallback, logger);
		ApplySubtitleLanguagePolicy(queryParts, settings.PreferredSubtitleLanguage, settings.SubtitleLanguageFallback, logger);
		ApplyAudioPolicy(queryParts, settings.PreferredAudioStyle, settings.AudioFallback, logger);
		ApplyAudioLanguagePolicy(queryParts, settings.PreferredAudioLanguage, settings.AudioLanguageFallback, logger);
	}

	private static void ApplySubtitleLanguagePolicy(List<string> queryParts, string? preferred, string? fallback, ILogger logger)
	{
		var effective = NormalizeLanguageCode(preferred);
		if (string.IsNullOrWhiteSpace(effective))
		{
			effective = NormalizeLanguageCode(fallback);
		}

		if (string.IsNullOrWhiteSpace(effective))
		{
			return;
		}

		// Best-effort for Plex. If unsupported upstream, it will be ignored.
		queryParts.Add($"subtitleLanguage={Uri.EscapeDataString(effective)}");
		logger.LogDebug("Applied casting subtitle language policy. Value={Value}", effective);
	}

	private static void ApplyAudioLanguagePolicy(List<string> queryParts, string? preferred, string? fallback, ILogger logger)
	{
		var effective = NormalizeLanguageCode(preferred);
		if (string.IsNullOrWhiteSpace(effective))
		{
			effective = NormalizeLanguageCode(fallback);
		}

		if (string.IsNullOrWhiteSpace(effective))
		{
			return;
		}

		// Best-effort for Plex. If unsupported upstream, it will be ignored.
		queryParts.Add($"audioLanguage={Uri.EscapeDataString(effective)}");
		logger.LogDebug("Applied casting audio language policy. Value={Value}", effective);
	}

	private static void ApplySubtitlePolicy(List<string> queryParts, string? preferred, string? fallback, ILogger logger)
	{
		var effective = NormalizePolicyValue(preferred);
		if (!IsRecognizedSubtitlePolicy(effective))
		{
			effective = NormalizePolicyValue(fallback);
		}

		var mapped = MapPlexSubtitlesValue(effective);
		if (mapped is null)
		{
			if (!string.IsNullOrWhiteSpace(effective))
			{
				logger.LogDebug("Unknown casting subtitle policy '{Value}'.", effective);
			}
			return;
		}

		queryParts.Add($"subtitles={Uri.EscapeDataString(mapped)}");
	}

	private static void ApplyAudioPolicy(List<string> queryParts, string? preferred, string? fallback, ILogger logger)
	{
		var effective = NormalizePolicyValue(preferred);
		if (!IsRecognizedAudioPolicy(effective))
		{
			effective = NormalizePolicyValue(fallback);
		}

		var lower = effective?.ToLowerInvariant();
		switch (lower)
		{
			case null:
			case "":
			case "auto":
				return;
			case "dts":
				queryParts.Add("audioCodec=dca");
				return;
			case "dd":
			case "dolbydigital":
				queryParts.Add("audioCodec=ac3");
				return;
			case "5.1":
			case "5_1":
			case "surround":
			case "surround5.1":
				queryParts.Add("maxAudioChannels=6");
				return;
			case "2ch":
			case "2.0":
			case "2_0":
			case "stereo":
			case "stereo2.0":
				queryParts.Add("maxAudioChannels=2");
				return;
			default:
				logger.LogDebug("Unknown casting audio policy '{Value}'.", effective);
				return;
		}
	}

	private static string? NormalizePolicyValue(string? value)
	{
		return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
	}

	private static string? NormalizeLanguageCode(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return null;
		}

		var trimmed = value.Trim();
		if (trimmed.Length > 16)
		{
			trimmed = trimmed.Substring(0, 16);
		}

		// Normalize common admin input to a BCP-47-ish form.
		return trimmed.Replace('_', '-').ToLowerInvariant();
	}

	private static string? ResolveAcceptLanguage(CastingSettingsRecord? settings)
	{
		if (settings is null)
		{
			return null;
		}

		var audio = NormalizeLanguageCode(settings.PreferredAudioLanguage) ?? NormalizeLanguageCode(settings.AudioLanguageFallback);
		var subs = NormalizeLanguageCode(settings.PreferredSubtitleLanguage) ?? NormalizeLanguageCode(settings.SubtitleLanguageFallback);

		if (!string.IsNullOrWhiteSpace(audio) && !string.IsNullOrWhiteSpace(subs) && !string.Equals(audio, subs, StringComparison.OrdinalIgnoreCase))
		{
			return $"{audio},{subs}";
		}

		return audio ?? subs;
	}

	private static bool IsRecognizedSubtitlePolicy(string? value)
	{
		var lower = value?.ToLowerInvariant();
		return lower is null or "" or "auto" or "none" or "burn";
	}

	private static bool IsRecognizedAudioPolicy(string? value)
	{
		var lower = value?.ToLowerInvariant();
		return lower is null or "" or "auto" or "dts" or "dd" or "dolbydigital" or "5.1" or "5_1" or "surround" or "surround5.1" or "2ch" or "2.0" or "2_0" or "stereo" or "stereo2.0";
	}

	private static string? MapPlexSubtitlesValue(string? value)
	{
		var lower = value?.ToLowerInvariant();
		return lower switch
		{
			null or "" or "auto" => null,
			"none" => "none",
			"burn" => "burn",
			_ => null
		};
	}

	private static string NormalizeBaseUrl(string baseUrl)
	{
		var trimmed = (baseUrl ?? string.Empty).Trim().TrimEnd('/');
		return string.IsNullOrWhiteSpace(trimmed) ? string.Empty : trimmed + "/";
	}

	// Intentionally no Plex metadata DTOs here: this provider streams via transcode endpoint.
}
