using Microsoft.Extensions.Options;
using Tindarr.Application.Abstractions.Persistence;
using Tindarr.Application.Interfaces.Integrations;
using Tindarr.Application.Options;
using Tindarr.Domain.Common;

namespace Tindarr.Application.Features.Jellyfin;

public sealed class JellyfinService(
	Tindarr.Application.Abstractions.Integrations.IJellyfinClient client,
	IServiceSettingsRepository settingsRepo,
	ILibraryCacheRepository libraryCache,
	IOptions<JellyfinOptions> options) : IJellyfinService
{
	private readonly JellyfinOptions _options = options.Value;

	public async Task<IReadOnlyList<JellyfinServerRecord>> ListServersAsync(CancellationToken cancellationToken)
	{
		var rows = await settingsRepo.ListAsync(ServiceType.Jellyfin, cancellationToken);
		return rows
			.Select(MapServer)
			.ToList();
	}

	public Task<ServiceSettingsRecord?> GetSettingsAsync(ServiceScope scope, CancellationToken cancellationToken)
	{
		return settingsRepo.GetAsync(scope, cancellationToken);
	}

	public async Task<ServiceSettingsRecord> UpsertSettingsAsync(JellyfinSettingsUpsert upsert, bool confirmNewInstance, CancellationToken cancellationToken)
	{
		var baseUrl = (upsert.BaseUrl ?? string.Empty).Trim();
		if (string.IsNullOrWhiteSpace(baseUrl))
		{
			throw new ArgumentException("BaseUrl is required.", nameof(upsert.BaseUrl));
		}

		if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out _))
		{
			throw new ArgumentException("BaseUrl must be an absolute URL.", nameof(upsert.BaseUrl));
		}

		var apiKey = (upsert.ApiKey ?? string.Empty).Trim();
		if (string.IsNullOrWhiteSpace(apiKey))
		{
			throw new ArgumentException("ApiKey is required.", nameof(upsert.ApiKey));
		}

		var connection = new Tindarr.Application.Abstractions.Integrations.JellyfinConnection(baseUrl, apiKey);
		var info = await client.GetSystemInfoAsync(connection, cancellationToken);

		var scope = new ServiceScope(ServiceType.Jellyfin, info.Id);
		var existing = await settingsRepo.GetAsync(scope, cancellationToken);

		if (existing is null)
		{
			var configured = await settingsRepo.ListAsync(ServiceType.Jellyfin, cancellationToken);
			if (configured.Count > 0 && !confirmNewInstance)
			{
				throw new ArgumentException(
					"Jellyfin is already configured. Set confirmNewInstance=true to add another server.",
					nameof(confirmNewInstance));
			}
		}

		var baseUrlChanged = existing is not null && !string.Equals(existing.JellyfinBaseUrl, baseUrl, StringComparison.OrdinalIgnoreCase);
		var apiKeyChanged = existing is not null && !string.Equals(existing.JellyfinApiKey, apiKey, StringComparison.Ordinal);

		var lastLibrarySyncUtc = existing?.JellyfinLastLibrarySyncUtc;
		if (baseUrlChanged || apiKeyChanged)
		{
			lastLibrarySyncUtc = null;
			await libraryCache.ReplaceTmdbIdsAsync(scope, [], DateTimeOffset.UtcNow, cancellationToken);
		}

		var upsertRecord = BuildUpsert(existing,
			jellyfinBaseUrl: baseUrl,
			jellyfinApiKey: apiKey,
			jellyfinServerName: string.IsNullOrWhiteSpace(info.ServerName) ? existing?.JellyfinServerName : info.ServerName,
			jellyfinServerVersion: string.IsNullOrWhiteSpace(info.Version) ? existing?.JellyfinServerVersion : info.Version,
			jellyfinLastLibrarySyncUtc: lastLibrarySyncUtc);

		await settingsRepo.UpsertAsync(scope, upsertRecord, cancellationToken);
		return await settingsRepo.GetAsync(scope, cancellationToken)
			?? throw new InvalidOperationException("Jellyfin settings missing after upsert.");
	}

	public async Task<JellyfinConnectionTestResult> TestConnectionAsync(ServiceScope scope, CancellationToken cancellationToken)
	{
		var settings = await settingsRepo.GetAsync(scope, cancellationToken);
		if (!IsConfigured(settings))
		{
			return new Tindarr.Application.Interfaces.Integrations.JellyfinConnectionTestResult(false, "Jellyfin is not configured.");
		}

		var result = await client.TestConnectionAsync(BuildConnection(settings!), cancellationToken);
		return new Tindarr.Application.Interfaces.Integrations.JellyfinConnectionTestResult(result.Ok, result.Message);
	}

	public async Task<JellyfinLibrarySyncResult> SyncLibraryAsync(ServiceScope scope, CancellationToken cancellationToken)
	{
		var settings = await RequireConfiguredAsync(scope, cancellationToken);
		var connection = BuildConnection(settings);

		var tmdbIds = await client.GetLibraryTmdbIdsAsync(connection, cancellationToken);

		var now = DateTimeOffset.UtcNow;
		await libraryCache.ReplaceTmdbIdsAsync(scope, tmdbIds, now, cancellationToken);
		await UpdateSettingsAsync(settings, jellyfinLastLibrarySyncUtc: now, cancellationToken: cancellationToken);

		return new JellyfinLibrarySyncResult(tmdbIds.Count, now);
	}

	public async Task<JellyfinLibrarySyncResult?> EnsureLibrarySyncAsync(ServiceScope scope, CancellationToken cancellationToken)
	{
		var settings = await settingsRepo.GetAsync(scope, cancellationToken);
		if (!IsConfigured(settings))
		{
			return null;
		}

		var lastSync = settings!.JellyfinLastLibrarySyncUtc;
		var maxAge = TimeSpan.FromMinutes(Math.Clamp(_options.LibrarySyncMinutes, 1, 1440));
		if (lastSync is null || DateTimeOffset.UtcNow - lastSync.Value >= maxAge)
		{
			return await SyncLibraryAsync(scope, cancellationToken);
		}

		return null;
	}

	private async Task<ServiceSettingsRecord> RequireConfiguredAsync(ServiceScope scope, CancellationToken cancellationToken)
	{
		var settings = await settingsRepo.GetAsync(scope, cancellationToken);
		if (!IsConfigured(settings))
		{
			throw new InvalidOperationException("Jellyfin is not configured.");
		}

		return settings!;
	}

	private static bool IsConfigured(ServiceSettingsRecord? settings)
	{
		return settings is not null
			&& !string.IsNullOrWhiteSpace(settings.JellyfinBaseUrl)
			&& !string.IsNullOrWhiteSpace(settings.JellyfinApiKey);
	}

	private static Tindarr.Application.Abstractions.Integrations.JellyfinConnection BuildConnection(ServiceSettingsRecord settings)
	{
		return new Tindarr.Application.Abstractions.Integrations.JellyfinConnection(settings.JellyfinBaseUrl!, settings.JellyfinApiKey!);
	}

	private Task UpdateSettingsAsync(
		ServiceSettingsRecord settings,
		DateTimeOffset? jellyfinLastLibrarySyncUtc = null,
		CancellationToken cancellationToken = default)
	{
		var upsert = BuildUpsert(settings,
			jellyfinBaseUrl: settings.JellyfinBaseUrl,
			jellyfinApiKey: settings.JellyfinApiKey,
			jellyfinServerName: settings.JellyfinServerName,
			jellyfinServerVersion: settings.JellyfinServerVersion,
			jellyfinLastLibrarySyncUtc: jellyfinLastLibrarySyncUtc ?? settings.JellyfinLastLibrarySyncUtc);

		return settingsRepo.UpsertAsync(new ServiceScope(settings.ServiceType, settings.ServerId), upsert, cancellationToken);
	}

	private static ServiceSettingsUpsert BuildUpsert(
		ServiceSettingsRecord? existing,
		string? jellyfinBaseUrl = null,
		string? jellyfinApiKey = null,
		string? jellyfinServerName = null,
		string? jellyfinServerVersion = null,
		DateTimeOffset? jellyfinLastLibrarySyncUtc = null)
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
			JellyfinBaseUrl: jellyfinBaseUrl ?? existing?.JellyfinBaseUrl,
			JellyfinApiKey: jellyfinApiKey ?? existing?.JellyfinApiKey,
			JellyfinServerName: jellyfinServerName ?? existing?.JellyfinServerName,
			JellyfinServerVersion: jellyfinServerVersion ?? existing?.JellyfinServerVersion,
			JellyfinLastLibrarySyncUtc: jellyfinLastLibrarySyncUtc ?? existing?.JellyfinLastLibrarySyncUtc,
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
	}

	private static JellyfinServerRecord MapServer(ServiceSettingsRecord record)
	{
		var name = string.IsNullOrWhiteSpace(record.JellyfinServerName) ? record.ServerId : record.JellyfinServerName;
		return new JellyfinServerRecord(
			record.ServerId,
			name,
			record.JellyfinBaseUrl,
			record.JellyfinServerVersion,
			record.JellyfinLastLibrarySyncUtc,
			record.UpdatedAtUtc);
	}
}
