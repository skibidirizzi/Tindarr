using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tindarr.Application.Abstractions.Integrations;
using Tindarr.Application.Abstractions.Persistence;
using Tindarr.Application.Interfaces.Integrations;
using Tindarr.Application.Options;
using Tindarr.Contracts.Movies;
using Tindarr.Domain.Common;

namespace Tindarr.Application.Features.Plex;

public sealed class PlexService(
	IPlexAuthClient authClient,
	IPlexLibraryClient libraryClient,
	IServiceSettingsRepository settingsRepo,
	ILibraryCacheRepository libraryCache,
	ITmdbClient tmdbClient,
	IOptions<PlexOptions> options,
	ILogger<PlexService> logger) : IPlexService
{
	private static readonly IReadOnlyDictionary<int, string> TmdbGenreMap = new Dictionary<int, string>
	{
		[28] = "Action",
		[12] = "Adventure",
		[16] = "Animation",
		[35] = "Comedy",
		[80] = "Crime",
		[99] = "Documentary",
		[18] = "Drama",
		[10751] = "Family",
		[14] = "Fantasy",
		[36] = "History",
		[27] = "Horror",
		[10402] = "Music",
		[9648] = "Mystery",
		[10749] = "Romance",
		[878] = "Science Fiction",
		[10770] = "TV Movie",
		[53] = "Thriller",
		[10752] = "War",
		[37] = "Western"
	};

	private readonly PlexOptions _options = options.Value;

	public async Task<PlexPinCreateResult> CreatePinAsync(CancellationToken cancellationToken)
	{
		var account = await EnsureAccountAsync(cancellationToken);
		var pin = await authClient.CreatePinAsync(account.PlexClientIdentifier!, cancellationToken);
		var authUrl = BuildAuthUrl(account.PlexClientIdentifier!, pin.Code);

		return new PlexPinCreateResult(pin.Id, pin.Code, pin.ExpiresAtUtc, authUrl);
	}

	public async Task<PlexPinStatusResult> VerifyPinAsync(long pinId, CancellationToken cancellationToken)
	{
		var account = await EnsureAccountAsync(cancellationToken);
		var pin = await authClient.GetPinAsync(account.PlexClientIdentifier!, pinId, cancellationToken);
		if (pin is null)
		{
			return new PlexPinStatusResult(pinId, string.Empty, null, false);
		}

		var authorized = !string.IsNullOrWhiteSpace(pin.AuthToken);
		if (authorized)
		{
			var validation = await authClient.ValidateTokenAsync(account.PlexClientIdentifier!, pin.AuthToken!, cancellationToken);
			if (!validation.Ok)
			{
				authorized = false;
			}
			else
			{
				await UpdateAccountAsync(account, plexAuthToken: pin.AuthToken, cancellationToken: cancellationToken);
			}
		}

		return new PlexPinStatusResult(pin.Id, pin.Code, pin.ExpiresAtUtc, authorized);
	}

	public async Task<PlexAuthStatusResult> GetAuthStatusAsync(CancellationToken cancellationToken)
	{
		var account = await settingsRepo.GetAsync(new ServiceScope(ServiceType.Plex, PlexConstants.AccountServerId), cancellationToken);
		return new PlexAuthStatusResult(
			HasClientIdentifier: !string.IsNullOrWhiteSpace(account?.PlexClientIdentifier),
			HasAuthToken: !string.IsNullOrWhiteSpace(account?.PlexAuthToken));
	}

	public async Task<IReadOnlyList<PlexServerRecord>> RefreshServersAsync(CancellationToken cancellationToken)
	{
		var account = await RequireAuthenticatedAsync(cancellationToken);
		var servers = await authClient.GetServersAsync(account.PlexClientIdentifier!, account.PlexAuthToken!, cancellationToken);

		var results = new List<PlexServerRecord>();
		foreach (var server in servers)
		{
			var record = await UpsertServerAsync(server, account, cancellationToken);
			results.Add(MapServer(record));
		}

		return results;
	}

	public async Task<IReadOnlyList<PlexServerRecord>> ListServersAsync(CancellationToken cancellationToken)
	{
		var rows = await settingsRepo.ListAsync(ServiceType.Plex, cancellationToken);
		return rows
			.Where(x => !string.Equals(x.ServerId, PlexConstants.AccountServerId, StringComparison.OrdinalIgnoreCase))
			.Select(MapServer)
			.ToList();
	}

	public async Task<PlexLibrarySyncResult> SyncLibraryAsync(ServiceScope scope, CancellationToken cancellationToken)
	{
		var settings = await RequireServerAsync(scope, cancellationToken);
		var account = await EnsureAccountAsync(cancellationToken);
		var accessToken = ResolveAccessToken(settings, account);

		var connection = new PlexServerConnectionInfo(
			settings.PlexServerUri!,
			accessToken,
			account.PlexClientIdentifier!);

		var sections = await libraryClient.GetMovieSectionsAsync(connection, cancellationToken);
		var tmdbIds = new HashSet<int>();
		foreach (var section in sections)
		{
			var items = await libraryClient.GetLibraryItemsAsync(connection, section.Key, cancellationToken);
			foreach (var item in items)
			{
				if (item.TmdbId > 0)
				{
					tmdbIds.Add(item.TmdbId);
				}
			}
		}

		var now = DateTimeOffset.UtcNow;
		await libraryCache.ReplaceTmdbIdsAsync(scope, tmdbIds.ToList(), now, cancellationToken);
		await UpdateServerAsync(settings, plexLastLibrarySyncUtc: now, cancellationToken: cancellationToken);

		await EnrichTmdbAsync(tmdbIds, cancellationToken);

		return new PlexLibrarySyncResult(tmdbIds.Count, now);
	}

	public async Task<PlexLibrarySyncResult?> EnsureLibrarySyncAsync(ServiceScope scope, CancellationToken cancellationToken)
	{
		var settings = await settingsRepo.GetAsync(scope, cancellationToken);
		if (settings is null || string.IsNullOrWhiteSpace(settings.PlexServerUri))
		{
			return null;
		}

		var lastSync = settings.PlexLastLibrarySyncUtc;
		var maxAge = TimeSpan.FromMinutes(Math.Clamp(_options.LibrarySyncMinutes, 1, 1440));
		if (lastSync is null || DateTimeOffset.UtcNow - lastSync.Value >= maxAge)
		{
			return await SyncLibraryAsync(scope, cancellationToken);
		}

		return null;
	}

	public async Task<IReadOnlyList<MovieDetailsDto>> GetLibraryAsync(
		ServiceScope scope,
		UserPreferencesRecord preferences,
		int limit,
		CancellationToken cancellationToken)
	{
		limit = Math.Clamp(limit, 1, 200);
		await EnsureLibrarySyncAsync(scope, cancellationToken);

		var ids = await libraryCache.GetTmdbIdsAsync(scope, cancellationToken);
		if (ids.Count == 0)
		{
			return [];
		}

		var results = new List<MovieDetailsDto>(limit);
		foreach (var tmdbId in ids)
		{
			var details = await tmdbClient.GetMovieDetailsAsync(tmdbId, cancellationToken);
			if (details is null)
			{
				continue;
			}

			if (!MatchesPreferences(details, preferences))
			{
				continue;
			}

			results.Add(details);
			if (results.Count >= limit)
			{
				break;
			}
		}

		return results;
	}

	private async Task<ServiceSettingsRecord> EnsureAccountAsync(CancellationToken cancellationToken)
	{
		var scope = new ServiceScope(ServiceType.Plex, PlexConstants.AccountServerId);
		var existing = await settingsRepo.GetAsync(scope, cancellationToken);
		if (existing is not null && !string.IsNullOrWhiteSpace(existing.PlexClientIdentifier))
		{
			return existing;
		}

		var clientId = existing?.PlexClientIdentifier;
		if (string.IsNullOrWhiteSpace(clientId))
		{
			clientId = Guid.NewGuid().ToString("N");
		}

		var upsert = BuildUpsert(existing,
			plexClientIdentifier: clientId,
			plexAuthToken: existing?.PlexAuthToken);

		await settingsRepo.UpsertAsync(scope, upsert, cancellationToken);
		return await settingsRepo.GetAsync(scope, cancellationToken)
			?? throw new InvalidOperationException("Plex account settings missing after upsert.");
	}

	private async Task<ServiceSettingsRecord> RequireAuthenticatedAsync(CancellationToken cancellationToken)
	{
		var account = await EnsureAccountAsync(cancellationToken);
		if (string.IsNullOrWhiteSpace(account.PlexAuthToken))
		{
			throw new InvalidOperationException("Plex is not authenticated.");
		}

		return account;
	}

	private async Task<ServiceSettingsRecord> RequireServerAsync(ServiceScope scope, CancellationToken cancellationToken)
	{
		var settings = await settingsRepo.GetAsync(scope, cancellationToken);
		if (settings is null || string.IsNullOrWhiteSpace(settings.PlexServerUri))
		{
			throw new InvalidOperationException("Plex server is not configured.");
		}

		return settings;
	}

	private async Task<ServiceSettingsRecord> UpsertServerAsync(PlexServerResource resource, ServiceSettingsRecord account, CancellationToken cancellationToken)
	{
		var scope = new ServiceScope(ServiceType.Plex, resource.MachineIdentifier);
		var existing = await settingsRepo.GetAsync(scope, cancellationToken);

		var selectedUri = SelectBestUri(resource);
		var accessToken = string.IsNullOrWhiteSpace(resource.AccessToken) ? account.PlexAuthToken : resource.AccessToken;

		var upsert = BuildUpsert(existing,
			plexServerName: resource.Name,
			plexServerUri: selectedUri,
			plexServerVersion: resource.Version,
			plexServerPlatform: resource.Platform,
			plexServerOwned: resource.Owned,
			plexServerOnline: resource.Online,
			plexServerAccessToken: accessToken);

		await settingsRepo.UpsertAsync(scope, upsert, cancellationToken);
		return await settingsRepo.GetAsync(scope, cancellationToken)
			?? throw new InvalidOperationException("Plex server settings missing after upsert.");
	}

	private Task UpdateAccountAsync(
		ServiceSettingsRecord settings,
		string? plexAuthToken = null,
		CancellationToken cancellationToken = default)
	{
		var upsert = BuildUpsert(settings,
			plexClientIdentifier: settings.PlexClientIdentifier,
			plexAuthToken: plexAuthToken ?? settings.PlexAuthToken);

		return settingsRepo.UpsertAsync(new ServiceScope(settings.ServiceType, settings.ServerId), upsert, cancellationToken);
	}

	private Task UpdateServerAsync(
		ServiceSettingsRecord settings,
		DateTimeOffset? plexLastLibrarySyncUtc = null,
		CancellationToken cancellationToken = default)
	{
		var upsert = BuildUpsert(settings,
			plexServerName: settings.PlexServerName,
			plexServerUri: settings.PlexServerUri,
			plexServerVersion: settings.PlexServerVersion,
			plexServerPlatform: settings.PlexServerPlatform,
			plexServerOwned: settings.PlexServerOwned,
			plexServerOnline: settings.PlexServerOnline,
			plexServerAccessToken: settings.PlexServerAccessToken,
			plexLastLibrarySyncUtc: plexLastLibrarySyncUtc ?? settings.PlexLastLibrarySyncUtc);

		return settingsRepo.UpsertAsync(new ServiceScope(settings.ServiceType, settings.ServerId), upsert, cancellationToken);
	}

	private static ServiceSettingsUpsert BuildUpsert(
		ServiceSettingsRecord? existing,
		string? plexClientIdentifier = null,
		string? plexAuthToken = null,
		string? plexServerName = null,
		string? plexServerUri = null,
		string? plexServerVersion = null,
		string? plexServerPlatform = null,
		bool? plexServerOwned = null,
		bool? plexServerOnline = null,
		string? plexServerAccessToken = null,
		DateTimeOffset? plexLastLibrarySyncUtc = null)
	{
		return new ServiceSettingsUpsert(
			RadarrBaseUrl: existing?.RadarrBaseUrl ?? string.Empty,
			RadarrApiKey: existing?.RadarrApiKey ?? string.Empty,
			RadarrQualityProfileId: existing?.RadarrQualityProfileId,
			RadarrRootFolderPath: existing?.RadarrRootFolderPath,
			RadarrTagLabel: existing?.RadarrTagLabel,
			RadarrTagId: existing?.RadarrTagId,
			RadarrAutoAddEnabled: existing?.RadarrAutoAddEnabled ?? false,
			RadarrLastAutoAddAcceptedId: existing?.RadarrLastAutoAddAcceptedId,
			RadarrLastLibrarySyncUtc: existing?.RadarrLastLibrarySyncUtc,
			PlexClientIdentifier: plexClientIdentifier ?? existing?.PlexClientIdentifier,
			PlexAuthToken: plexAuthToken ?? existing?.PlexAuthToken,
			PlexServerName: plexServerName ?? existing?.PlexServerName,
			PlexServerUri: plexServerUri ?? existing?.PlexServerUri,
			PlexServerVersion: plexServerVersion ?? existing?.PlexServerVersion,
			PlexServerPlatform: plexServerPlatform ?? existing?.PlexServerPlatform,
			PlexServerOwned: plexServerOwned ?? existing?.PlexServerOwned,
			PlexServerOnline: plexServerOnline ?? existing?.PlexServerOnline,
			PlexServerAccessToken: plexServerAccessToken ?? existing?.PlexServerAccessToken,
			PlexLastLibrarySyncUtc: plexLastLibrarySyncUtc ?? existing?.PlexLastLibrarySyncUtc);
	}

	private string ResolveAccessToken(ServiceSettingsRecord server, ServiceSettingsRecord account)
	{
		if (!string.IsNullOrWhiteSpace(server.PlexServerAccessToken))
		{
			return server.PlexServerAccessToken!;
		}

		if (string.IsNullOrWhiteSpace(account.PlexAuthToken))
		{
			throw new InvalidOperationException("Plex token is not available.");
		}

		return account.PlexAuthToken!;
	}

	private static string BuildAuthUrl(string clientIdentifier, string code)
	{
		var encodedClient = Uri.EscapeDataString(clientIdentifier);
		var encodedCode = Uri.EscapeDataString(code);
		return $"https://app.plex.tv/auth#?clientID={encodedClient}&code={encodedCode}";
	}

	private static string? SelectBestUri(PlexServerResource resource)
	{
		if (resource.Connections.Count == 0)
		{
			return null;
		}

		var ordered = resource.Connections
			.OrderByDescending(c => c.Local)
			.ThenByDescending(c => string.Equals(c.Protocol, "https", StringComparison.OrdinalIgnoreCase))
			.ThenBy(c => c.Relay);

		return ordered.FirstOrDefault()?.Uri;
	}

	private async Task EnrichTmdbAsync(IReadOnlyCollection<int> tmdbIds, CancellationToken cancellationToken)
	{
		if (tmdbIds.Count == 0)
		{
			return;
		}

		var maxConcurrency = Math.Clamp(_options.EnrichmentConcurrency, 1, 32);
		using var gate = new SemaphoreSlim(maxConcurrency, maxConcurrency);
		var tasks = tmdbIds.Select(async id =>
		{
			await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
			try
			{
				_ = await tmdbClient.GetMovieDetailsAsync(id, cancellationToken).ConfigureAwait(false);
			}
			catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
			{
				logger.LogWarning(ex, "tmdb enrichment failed. TmdbId={TmdbId}", id);
			}
			finally
			{
				gate.Release();
			}
		}).ToList();

		await Task.WhenAll(tasks).ConfigureAwait(false);
	}

	private static bool MatchesPreferences(MovieDetailsDto details, UserPreferencesRecord preferences)
	{
		if (preferences.MinReleaseYear is { } minYear)
		{
			if (details.ReleaseYear is null || details.ReleaseYear.Value < minYear)
			{
				return false;
			}
		}

		if (preferences.MaxReleaseYear is { } maxYear)
		{
			if (details.ReleaseYear is null || details.ReleaseYear.Value > maxYear)
			{
				return false;
			}
		}

		if (preferences.MinRating is { } minRating)
		{
			if (details.Rating is null || details.Rating.Value < minRating)
			{
				return false;
			}
		}

		if (preferences.MaxRating is { } maxRating)
		{
			if (details.Rating is null || details.Rating.Value > maxRating)
			{
				return false;
			}
		}

		if (!preferences.IncludeAdult && details.MpaaRating is not null && IsAdultRating(details.MpaaRating))
		{
			return false;
		}

		if (preferences.PreferredGenres is { Count: > 0 })
		{
			var preferred = MapGenreNames(preferences.PreferredGenres);
			if (preferred.Count > 0 && !details.Genres.Any(g => preferred.Contains(g)))
			{
				return false;
			}
		}

		if (preferences.ExcludedGenres is { Count: > 0 })
		{
			var excluded = MapGenreNames(preferences.ExcludedGenres);
			if (excluded.Count > 0 && details.Genres.Any(g => excluded.Contains(g)))
			{
				return false;
			}
		}

		if (preferences.PreferredRegions is { Count: > 0 })
		{
			if (!MatchesRegions(details.Regions, preferences.PreferredRegions))
			{
				return false;
			}
		}

		if (preferences.PreferredOriginalLanguages is { Count: > 0 })
		{
			if (string.IsNullOrWhiteSpace(details.OriginalLanguage))
			{
				return false;
			}

			if (!preferences.PreferredOriginalLanguages.Contains(details.OriginalLanguage, StringComparer.OrdinalIgnoreCase))
			{
				return false;
			}
		}

		return true;
	}

	private static bool MatchesRegions(IReadOnlyList<string> regions, IReadOnlyList<string> preferredRegions)
	{
		if (preferredRegions.Count == 0)
		{
			return true;
		}

		if (regions.Count == 0)
		{
			return false;
		}

		var preferred = new HashSet<string>(preferredRegions, StringComparer.OrdinalIgnoreCase);
		return regions.Any(r => preferred.Contains(r));
	}

	private static HashSet<string> MapGenreNames(IReadOnlyList<int> genreIds)
	{
		var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var id in genreIds)
		{
			if (TmdbGenreMap.TryGetValue(id, out var name))
			{
				names.Add(name);
			}
		}

		return names;
	}

	private static bool IsAdultRating(string rating)
	{
		var normalized = rating.Trim().ToUpperInvariant();
		return normalized is "NC-17" or "X" or "XXX" or "AO" or "ADULT";
	}

	private static PlexServerRecord MapServer(ServiceSettingsRecord settings)
	{
		return new PlexServerRecord(
			settings.ServerId,
			settings.PlexServerName ?? settings.ServerId,
			settings.PlexServerUri,
			settings.PlexServerVersion,
			settings.PlexServerPlatform,
			settings.PlexServerOwned,
			settings.PlexServerOnline,
			settings.PlexLastLibrarySyncUtc,
			settings.UpdatedAtUtc);
	}
}
