using Microsoft.Extensions.Options;
using Tindarr.Application.Abstractions.Integrations;
using Tindarr.Application.Abstractions.Persistence;
using Tindarr.Application.Interfaces.Integrations;
using Tindarr.Application.Options;
using Tindarr.Domain.Common;

namespace Tindarr.Application.Features.Radarr;

public sealed class RadarrService(
	IRadarrClient client,
	IServiceSettingsRepository settingsRepo,
	ILibraryCacheRepository libraryCache,
	IAcceptedMovieRepository acceptedMovies,
	IOptions<RadarrOptions> options) : IRadarrService
{
	private readonly RadarrOptions _options = options.Value;

	public Task<ServiceSettingsRecord?> GetSettingsAsync(ServiceScope scope, CancellationToken cancellationToken)
	{
		return settingsRepo.GetAsync(scope, cancellationToken);
	}

	public async Task<ServiceSettingsRecord> UpsertSettingsAsync(ServiceScope scope, RadarrSettingsUpsert upsert, CancellationToken cancellationToken)
	{
		var existing = await settingsRepo.GetAsync(scope, cancellationToken);

		var baseUrl = string.IsNullOrWhiteSpace(upsert.BaseUrl)
			? existing?.RadarrBaseUrl ?? string.Empty
			: upsert.BaseUrl.Trim();

		if (string.IsNullOrWhiteSpace(baseUrl))
		{
			throw new ArgumentException("BaseUrl is required.", nameof(upsert.BaseUrl));
		}

		if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out _))
		{
			throw new ArgumentException("BaseUrl must be an absolute URL.", nameof(upsert.BaseUrl));
		}

		var apiKey = upsert.ApiKey is null
			? existing?.RadarrApiKey ?? string.Empty
			: upsert.ApiKey.Trim();

		var tagLabel = string.IsNullOrWhiteSpace(upsert.TagLabel)
			? existing?.RadarrTagLabel ?? _options.DefaultTagLabel
			: upsert.TagLabel.Trim();

		var qualityProfileId = upsert.QualityProfileId ?? existing?.RadarrQualityProfileId;
		var rootFolderPath = string.IsNullOrWhiteSpace(upsert.RootFolderPath)
			? existing?.RadarrRootFolderPath
			: upsert.RootFolderPath.Trim();

		var baseUrlChanged = existing is not null && !string.Equals(existing.RadarrBaseUrl, baseUrl, StringComparison.OrdinalIgnoreCase);
		var apiKeyChanged = existing is not null && !string.Equals(existing.RadarrApiKey, apiKey, StringComparison.Ordinal);
		var tagLabelChanged = existing is not null && !string.Equals(existing.RadarrTagLabel, tagLabel, StringComparison.OrdinalIgnoreCase);

		var tagId = existing?.RadarrTagId;
		if (baseUrlChanged || apiKeyChanged || tagLabelChanged)
		{
			tagId = null;
		}

		var lastLibrarySyncUtc = existing?.RadarrLastLibrarySyncUtc;
		if (baseUrlChanged)
		{
			lastLibrarySyncUtc = null;
			await libraryCache.ReplaceTmdbIdsAsync(scope, [], DateTimeOffset.UtcNow, cancellationToken);
		}

		var upsertRecord = new ServiceSettingsUpsert(
			RadarrBaseUrl: baseUrl,
			RadarrApiKey: apiKey,
			RadarrQualityProfileId: qualityProfileId,
			RadarrRootFolderPath: rootFolderPath,
			RadarrTagLabel: tagLabel,
			RadarrTagId: tagId,
			RadarrAutoAddEnabled: upsert.AutoAddEnabled,
			RadarrLastAutoAddAcceptedId: existing?.RadarrLastAutoAddAcceptedId,
			RadarrLastLibrarySyncUtc: lastLibrarySyncUtc,
			JellyfinBaseUrl: existing?.JellyfinBaseUrl,
			JellyfinApiKey: existing?.JellyfinApiKey,
			JellyfinServerName: existing?.JellyfinServerName,
			JellyfinServerVersion: existing?.JellyfinServerVersion,
			JellyfinLastLibrarySyncUtc: existing?.JellyfinLastLibrarySyncUtc,
			EmbyBaseUrl: existing?.EmbyBaseUrl,
			EmbyApiKey: existing?.EmbyApiKey,
			EmbyServerName: existing?.EmbyServerName,
			EmbyServerVersion: existing?.EmbyServerVersion,
			EmbyLastLibrarySyncUtc: existing?.EmbyLastLibrarySyncUtc,
			PlexClientIdentifier: existing?.PlexClientIdentifier,
			PlexAuthToken: existing?.PlexAuthToken,
			PlexServerName: existing?.PlexServerName,
			PlexServerUri: existing?.PlexServerUri,
			PlexServerVersion: existing?.PlexServerVersion,
			PlexServerPlatform: existing?.PlexServerPlatform,
			PlexServerOwned: existing?.PlexServerOwned,
			PlexServerOnline: existing?.PlexServerOnline,
			PlexServerAccessToken: existing?.PlexServerAccessToken,
			PlexLastLibrarySyncUtc: existing?.PlexLastLibrarySyncUtc);

		await settingsRepo.UpsertAsync(scope, upsertRecord, cancellationToken);
		return await settingsRepo.GetAsync(scope, cancellationToken) ?? throw new InvalidOperationException("Radarr settings missing after upsert.");
	}

	public async Task<RadarrConnectionTestResult> TestConnectionAsync(ServiceScope scope, CancellationToken cancellationToken)
	{
		var settings = await settingsRepo.GetAsync(scope, cancellationToken);
		if (!IsConfigured(settings))
		{
			return new RadarrConnectionTestResult(false, "Radarr is not configured.");
		}

		return await client.TestConnectionAsync(BuildConnection(settings!), cancellationToken);
	}

	public async Task<IReadOnlyList<RadarrQualityProfile>> GetQualityProfilesAsync(ServiceScope scope, CancellationToken cancellationToken)
	{
		var settings = await RequireConfiguredAsync(scope, cancellationToken);
		return await client.GetQualityProfilesAsync(BuildConnection(settings), cancellationToken);
	}

	public async Task<IReadOnlyList<RadarrRootFolder>> GetRootFoldersAsync(ServiceScope scope, CancellationToken cancellationToken)
	{
		var settings = await RequireConfiguredAsync(scope, cancellationToken);
		return await client.GetRootFoldersAsync(BuildConnection(settings), cancellationToken);
	}

	public async Task<RadarrLibrarySyncResult> SyncLibraryAsync(ServiceScope scope, CancellationToken cancellationToken)
	{
		var settings = await RequireConfiguredAsync(scope, cancellationToken);
		var connection = BuildConnection(settings);

		var movies = await client.GetLibraryAsync(connection, cancellationToken);
		var tmdbIds = movies.Select(m => m.TmdbId).Where(id => id > 0).Distinct().ToList();

		var now = DateTimeOffset.UtcNow;
		await libraryCache.ReplaceTmdbIdsAsync(scope, tmdbIds, now, cancellationToken);
		await UpdateSettingsAsync(settings, radarrLastLibrarySyncUtc: now, cancellationToken: cancellationToken);

		return new RadarrLibrarySyncResult(tmdbIds.Count, now);
	}

	public async Task<RadarrLibrarySyncResult?> EnsureLibrarySyncAsync(ServiceScope scope, CancellationToken cancellationToken)
	{
		var settings = await settingsRepo.GetAsync(scope, cancellationToken);
		if (!IsConfigured(settings))
		{
			return null;
		}

		var lastSync = settings!.RadarrLastLibrarySyncUtc;
		var maxAge = TimeSpan.FromMinutes(Math.Clamp(_options.LibrarySyncMinutes, 1, 1440));
		if (lastSync is null || DateTimeOffset.UtcNow - lastSync.Value >= maxAge)
		{
			return await SyncLibraryAsync(scope, cancellationToken);
		}

		return null;
	}

	public async Task<RadarrAutoAddResult> AutoAddAcceptedMoviesAsync(ServiceScope scope, CancellationToken cancellationToken)
	{
		var settings = await settingsRepo.GetAsync(scope, cancellationToken);
		if (!IsConfigured(settings))
		{
			return new RadarrAutoAddResult(0, 0, 0, 0, settings?.RadarrLastAutoAddAcceptedId, "Radarr is not configured.");
		}

		await EnsureLibrarySyncAsync(scope, cancellationToken);

		if (!settings!.RadarrAutoAddEnabled)
		{
			return new RadarrAutoAddResult(0, 0, 0, 0, settings.RadarrLastAutoAddAcceptedId, "Auto-add disabled.");
		}

		if (settings.RadarrQualityProfileId is null || string.IsNullOrWhiteSpace(settings.RadarrRootFolderPath))
		{
			return new RadarrAutoAddResult(0, 0, 0, 0, settings.RadarrLastAutoAddAcceptedId, "Radarr default profile/root folder not set.");
		}

		var batchSize = Math.Clamp(_options.AutoAddBatchSize, 1, 500);
		var accepted = await acceptedMovies.ListSinceIdAsync(scope, settings.RadarrLastAutoAddAcceptedId, batchSize, cancellationToken);
		if (accepted.Count == 0)
		{
			return new RadarrAutoAddResult(0, 0, 0, 0, settings.RadarrLastAutoAddAcceptedId, "No accepted movies.");
		}

		var libraryIds = await libraryCache.GetTmdbIdsAsync(scope, cancellationToken);
		var librarySet = new HashSet<int>(libraryIds);
		var connection = BuildConnection(settings);

		var attempted = 0;
		var added = 0;
		var skippedExisting = 0;
		var failed = 0;
		long? lastAcceptedId = settings.RadarrLastAutoAddAcceptedId;
		var addedIds = new List<int>();

		foreach (var movie in accepted)
		{
			attempted++;
			if (librarySet.Contains(movie.TmdbId))
			{
				skippedExisting++;
				lastAcceptedId = movie.Id;
				continue;
			}

			var result = await AddMovieInternalAsync(settings, connection, movie.TmdbId, cancellationToken);
			if (result.Added)
			{
				added++;
				librarySet.Add(movie.TmdbId);
				addedIds.Add(movie.TmdbId);
				lastAcceptedId = movie.Id;
				continue;
			}

			if (result.AlreadyExists)
			{
				skippedExisting++;
				librarySet.Add(movie.TmdbId);
				addedIds.Add(movie.TmdbId);
				lastAcceptedId = movie.Id;
				continue;
			}

			failed++;
		}

		if (lastAcceptedId != settings.RadarrLastAutoAddAcceptedId)
		{
			await UpdateSettingsAsync(settings, radarrLastAutoAddAcceptedId: lastAcceptedId, cancellationToken: cancellationToken);
		}

		if (addedIds.Count > 0)
		{
			await libraryCache.AddTmdbIdsAsync(scope, addedIds, DateTimeOffset.UtcNow, cancellationToken);
		}

		return new RadarrAutoAddResult(attempted, added, skippedExisting, failed, lastAcceptedId, null);
	}

	public async Task<RadarrAddMovieResult> AddMovieAsync(ServiceScope scope, int tmdbId, CancellationToken cancellationToken)
	{
		var settings = await RequireConfiguredAsync(scope, cancellationToken);
		if (settings.RadarrQualityProfileId is null || string.IsNullOrWhiteSpace(settings.RadarrRootFolderPath))
		{
			return new RadarrAddMovieResult(false, false, "Radarr default profile/root folder not set.");
		}

		return await AddMovieInternalAsync(settings, BuildConnection(settings), tmdbId, cancellationToken);
	}

	private async Task<RadarrAddMovieResult> AddMovieInternalAsync(ServiceSettingsRecord settings, RadarrConnection connection, int tmdbId, CancellationToken cancellationToken)
	{
		var lookup = await client.LookupMovieAsync(connection, tmdbId, cancellationToken);
		if (lookup is null)
		{
			return new RadarrAddMovieResult(false, false, "Movie lookup failed.");
		}

		var tagLabel = string.IsNullOrWhiteSpace(settings.RadarrTagLabel) ? _options.DefaultTagLabel : settings.RadarrTagLabel.Trim();
		var tagId = settings.RadarrTagId;

		if (tagId is null && !string.IsNullOrWhiteSpace(tagLabel))
		{
			tagId = await client.EnsureTagAsync(connection, tagLabel, cancellationToken);
			if (tagId is null)
			{
				return new RadarrAddMovieResult(false, false, "Unable to ensure Radarr tag.");
			}

			await UpdateSettingsAsync(settings, radarrTagId: tagId, cancellationToken: cancellationToken);
		}

		var request = new RadarrAddMovieRequest(
			Lookup: lookup,
			QualityProfileId: settings.RadarrQualityProfileId!.Value,
			RootFolderPath: settings.RadarrRootFolderPath!,
			TagIds: tagId is null ? [] : [tagId.Value],
			SearchForMovie: true);

		return await client.AddMovieAsync(connection, request, cancellationToken);
	}

	private static bool IsConfigured(ServiceSettingsRecord? settings)
	{
		return settings is not null
			&& !string.IsNullOrWhiteSpace(settings.RadarrBaseUrl)
			&& !string.IsNullOrWhiteSpace(settings.RadarrApiKey);
	}

	private static RadarrConnection BuildConnection(ServiceSettingsRecord settings)
	{
		return new RadarrConnection(settings.RadarrBaseUrl, settings.RadarrApiKey);
	}

	private async Task<ServiceSettingsRecord> RequireConfiguredAsync(ServiceScope scope, CancellationToken cancellationToken)
	{
		var settings = await settingsRepo.GetAsync(scope, cancellationToken);
		if (!IsConfigured(settings))
		{
			throw new InvalidOperationException("Radarr is not configured.");
		}

		return settings!;
	}

	private Task UpdateSettingsAsync(
		ServiceSettingsRecord settings,
		long? radarrLastAutoAddAcceptedId = null,
		DateTimeOffset? radarrLastLibrarySyncUtc = null,
		int? radarrTagId = null,
		CancellationToken cancellationToken = default)
	{
		var upsert = new ServiceSettingsUpsert(
			settings.RadarrBaseUrl,
			settings.RadarrApiKey,
			settings.RadarrQualityProfileId,
			settings.RadarrRootFolderPath,
			settings.RadarrTagLabel,
			radarrTagId ?? settings.RadarrTagId,
			settings.RadarrAutoAddEnabled,
			radarrLastAutoAddAcceptedId ?? settings.RadarrLastAutoAddAcceptedId,
			radarrLastLibrarySyncUtc ?? settings.RadarrLastLibrarySyncUtc,
			settings.JellyfinBaseUrl,
			settings.JellyfinApiKey,
			settings.JellyfinServerName,
			settings.JellyfinServerVersion,
			settings.JellyfinLastLibrarySyncUtc,
			settings.EmbyBaseUrl,
			settings.EmbyApiKey,
			settings.EmbyServerName,
			settings.EmbyServerVersion,
			settings.EmbyLastLibrarySyncUtc,
			settings.PlexClientIdentifier,
			settings.PlexAuthToken,
			settings.PlexServerName,
			settings.PlexServerUri,
			settings.PlexServerVersion,
			settings.PlexServerPlatform,
			settings.PlexServerOwned,
			settings.PlexServerOnline,
			settings.PlexServerAccessToken,
			settings.PlexLastLibrarySyncUtc);

		return settingsRepo.UpsertAsync(new ServiceScope(settings.ServiceType, settings.ServerId), upsert, cancellationToken);
	}
}
