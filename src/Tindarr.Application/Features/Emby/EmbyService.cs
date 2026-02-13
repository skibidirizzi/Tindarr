using Microsoft.Extensions.Options;
using Tindarr.Application.Abstractions.Persistence;
using Tindarr.Application.Interfaces.Integrations;
using Tindarr.Application.Options;
using Tindarr.Domain.Common;

namespace Tindarr.Application.Features.Emby;

public sealed class EmbyService(
	Tindarr.Application.Abstractions.Integrations.IEmbyClient client,
	IServiceSettingsRepository settingsRepo,
	ILibraryCacheRepository libraryCache,
	IOptions<EmbyOptions> options) : IEmbyService
{
	private readonly EmbyOptions _options = options.Value;

	public async Task<IReadOnlyList<EmbyServerRecord>> ListServersAsync(CancellationToken cancellationToken)
	{
		var rows = await settingsRepo.ListAsync(ServiceType.Emby, cancellationToken);
		return rows
			.Select(MapServer)
			.ToList();
	}

	public Task<ServiceSettingsRecord?> GetSettingsAsync(ServiceScope scope, CancellationToken cancellationToken)
	{
		return settingsRepo.GetAsync(scope, cancellationToken);
	}

	public async Task<ServiceSettingsRecord> UpsertSettingsAsync(EmbySettingsUpsert upsert, bool confirmNewInstance, CancellationToken cancellationToken)
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

		var connection = new Tindarr.Application.Abstractions.Integrations.EmbyConnection(baseUrl, apiKey);
		var info = await client.GetSystemInfoAsync(connection, cancellationToken);

		var scope = new ServiceScope(ServiceType.Emby, info.Id);
		var existing = await settingsRepo.GetAsync(scope, cancellationToken);

		if (existing is null)
		{
			var configured = await settingsRepo.ListAsync(ServiceType.Emby, cancellationToken);
			if (configured.Count > 0 && !confirmNewInstance)
			{
				throw new ArgumentException(
					"Emby is already configured. Set confirmNewInstance=true to add another server.",
					nameof(confirmNewInstance));
			}
		}

		var baseUrlChanged = existing is not null && !string.Equals(existing.EmbyBaseUrl, baseUrl, StringComparison.OrdinalIgnoreCase);
		var apiKeyChanged = existing is not null && !string.Equals(existing.EmbyApiKey, apiKey, StringComparison.Ordinal);

		var lastLibrarySyncUtc = existing?.EmbyLastLibrarySyncUtc;
		if (baseUrlChanged || apiKeyChanged)
		{
			lastLibrarySyncUtc = null;
			await libraryCache.ReplaceTmdbIdsAsync(scope, [], DateTimeOffset.UtcNow, cancellationToken);
		}

		var upsertRecord = BuildUpsert(existing,
			embyBaseUrl: baseUrl,
			embyApiKey: apiKey,
			embyServerName: string.IsNullOrWhiteSpace(info.ServerName) ? existing?.EmbyServerName : info.ServerName,
			embyServerVersion: string.IsNullOrWhiteSpace(info.Version) ? existing?.EmbyServerVersion : info.Version,
			embyLastLibrarySyncUtc: lastLibrarySyncUtc);

		await settingsRepo.UpsertAsync(scope, upsertRecord, cancellationToken);
		return await settingsRepo.GetAsync(scope, cancellationToken)
			?? throw new InvalidOperationException("Emby settings missing after upsert.");
	}

	public async Task<EmbyConnectionTestResult> TestConnectionAsync(ServiceScope scope, CancellationToken cancellationToken)
	{
		var settings = await settingsRepo.GetAsync(scope, cancellationToken);
		if (!IsConfigured(settings))
		{
			return new Tindarr.Application.Interfaces.Integrations.EmbyConnectionTestResult(false, "Emby is not configured.");
		}

		var result = await client.TestConnectionAsync(BuildConnection(settings!), cancellationToken);
		return new Tindarr.Application.Interfaces.Integrations.EmbyConnectionTestResult(result.Ok, result.Message);
	}

	public async Task<EmbyLibrarySyncResult> SyncLibraryAsync(ServiceScope scope, CancellationToken cancellationToken)
	{
		var settings = await RequireConfiguredAsync(scope, cancellationToken);
		var connection = BuildConnection(settings);

		var tmdbIds = await client.GetLibraryTmdbIdsAsync(connection, cancellationToken);

		var now = DateTimeOffset.UtcNow;
		await libraryCache.ReplaceTmdbIdsAsync(scope, tmdbIds, now, cancellationToken);
		await UpdateSettingsAsync(settings, embyLastLibrarySyncUtc: now, cancellationToken: cancellationToken);

		return new EmbyLibrarySyncResult(tmdbIds.Count, now);
	}

	public async Task<EmbyLibrarySyncResult?> EnsureLibrarySyncAsync(ServiceScope scope, CancellationToken cancellationToken)
	{
		var settings = await settingsRepo.GetAsync(scope, cancellationToken);
		if (!IsConfigured(settings))
		{
			return null;
		}

		var lastSync = settings!.EmbyLastLibrarySyncUtc;
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
			throw new InvalidOperationException("Emby is not configured.");
		}

		return settings!;
	}

	private static bool IsConfigured(ServiceSettingsRecord? settings)
	{
		return settings is not null
			&& !string.IsNullOrWhiteSpace(settings.EmbyBaseUrl)
			&& !string.IsNullOrWhiteSpace(settings.EmbyApiKey);
	}

	private static Tindarr.Application.Abstractions.Integrations.EmbyConnection BuildConnection(ServiceSettingsRecord settings)
	{
		return new Tindarr.Application.Abstractions.Integrations.EmbyConnection(settings.EmbyBaseUrl!, settings.EmbyApiKey!);
	}

	private Task UpdateSettingsAsync(
		ServiceSettingsRecord settings,
		DateTimeOffset? embyLastLibrarySyncUtc = null,
		CancellationToken cancellationToken = default)
	{
		var upsert = BuildUpsert(settings,
			embyBaseUrl: settings.EmbyBaseUrl,
			embyApiKey: settings.EmbyApiKey,
			embyServerName: settings.EmbyServerName,
			embyServerVersion: settings.EmbyServerVersion,
			embyLastLibrarySyncUtc: embyLastLibrarySyncUtc ?? settings.EmbyLastLibrarySyncUtc);

		return settingsRepo.UpsertAsync(new ServiceScope(settings.ServiceType, settings.ServerId), upsert, cancellationToken);
	}

	private static ServiceSettingsUpsert BuildUpsert(
		ServiceSettingsRecord? existing,
		string? embyBaseUrl = null,
		string? embyApiKey = null,
		string? embyServerName = null,
		string? embyServerVersion = null,
		DateTimeOffset? embyLastLibrarySyncUtc = null)
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
			JellyfinBaseUrl: existing?.JellyfinBaseUrl,
			JellyfinApiKey: existing?.JellyfinApiKey,
			JellyfinServerName: existing?.JellyfinServerName,
			JellyfinServerVersion: existing?.JellyfinServerVersion,
			JellyfinLastLibrarySyncUtc: existing?.JellyfinLastLibrarySyncUtc,
			EmbyBaseUrl: embyBaseUrl ?? existing?.EmbyBaseUrl,
			EmbyApiKey: embyApiKey ?? existing?.EmbyApiKey,
			EmbyServerName: embyServerName ?? existing?.EmbyServerName,
			EmbyServerVersion: embyServerVersion ?? existing?.EmbyServerVersion,
			EmbyLastLibrarySyncUtc: embyLastLibrarySyncUtc ?? existing?.EmbyLastLibrarySyncUtc,
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

	private static EmbyServerRecord MapServer(ServiceSettingsRecord record)
	{
		var name = string.IsNullOrWhiteSpace(record.EmbyServerName) ? record.ServerId : record.EmbyServerName;
		return new EmbyServerRecord(
			record.ServerId,
			name,
			record.EmbyBaseUrl,
			record.EmbyServerVersion,
			record.EmbyLastLibrarySyncUtc,
			record.UpdatedAtUtc);
	}
}
