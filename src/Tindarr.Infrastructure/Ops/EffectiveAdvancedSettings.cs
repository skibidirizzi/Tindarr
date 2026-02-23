using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tindarr.Application.Abstractions.Ops;
using Tindarr.Application.Abstractions.Persistence;
using Tindarr.Application.Options;

namespace Tindarr.Infrastructure.Ops;

public sealed class EffectiveAdvancedSettings(
	IServiceScopeFactory scopeFactory,
	IOptions<ApiRateLimitOptions> apiRateLimitOptions,
	IOptions<CleanupOptions> cleanupOptions,
	IOptions<TmdbOptions> tmdbOptions,
	ILogger<EffectiveAdvancedSettings>? logger = null) : IEffectiveAdvancedSettings
{
	private readonly ApiRateLimitOptions _apiRateLimitConfig = apiRateLimitOptions.Value;
	private readonly CleanupOptions _cleanupConfig = cleanupOptions.Value;
	private readonly TmdbOptions _tmdbConfig = tmdbOptions.Value;

	private (ApiRateLimitOptions Api, CleanupOptions Cleanup, string? TmdbApiKey, string? TmdbReadAccessToken, string DateTimeDisplayMode, string TimeZoneId, string DateOrder)? _cache;
	private readonly object _lock = new();
	private const string DefaultDateTimeDisplayMode = "locale";
	private const string DefaultTimeZoneId = "Local";
	private const string DefaultDateOrder = "locale";

	public ApiRateLimitOptions GetApiRateLimitOptions()
	{
		EnsureLoaded();
		return _cache!.Value.Api;
	}

	public CleanupOptions GetCleanupOptions()
	{
		EnsureLoaded();
		return _cache!.Value.Cleanup;
	}

	public string GetEffectiveTmdbApiKey()
	{
		EnsureLoaded();
		var fromDb = _cache!.Value.TmdbApiKey;
		if (!string.IsNullOrWhiteSpace(fromDb))
		{
			return fromDb.Trim();
		}
		return _tmdbConfig.ApiKey ?? string.Empty;
	}

	public string GetEffectiveTmdbReadAccessToken()
	{
		EnsureLoaded();
		var fromDb = _cache!.Value.TmdbReadAccessToken;
		if (!string.IsNullOrWhiteSpace(fromDb))
		{
			return fromDb.Trim();
		}
		return _tmdbConfig.ReadAccessToken ?? string.Empty;
	}

	public bool HasEffectiveTmdbCredentials()
	{
		if (!string.IsNullOrWhiteSpace(GetEffectiveTmdbApiKey()))
		{
			return true;
		}
		return !string.IsNullOrWhiteSpace(GetEffectiveTmdbReadAccessToken());
	}

	public string GetDateTimeDisplayMode()
	{
		EnsureLoaded();
		return _cache!.Value.DateTimeDisplayMode;
	}

	public string GetTimeZoneId()
	{
		EnsureLoaded();
		return _cache!.Value.TimeZoneId;
	}

	public string GetDateOrder()
	{
		EnsureLoaded();
		return _cache!.Value.DateOrder;
	}

	/// <summary>Cache TTL so Workers (separate process) picks up credentials saved via API within a minute.</summary>
	private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

	/// <summary>Reload delay after SignalSettingsUpdated so next use sees the put.</summary>
	private static readonly TimeSpan ReloadDelayAfterSignal = TimeSpan.FromSeconds(1);

	private DateTimeOffset _cacheExpiresAt = DateTimeOffset.MinValue;
	private DateTimeOffset _cacheLoadedAt = DateTimeOffset.MinValue;

	public void Invalidate()
	{
		lock (_lock)
		{
			_cache = null;
			_cacheExpiresAt = DateTimeOffset.MinValue;
			_cacheLoadedAt = DateTimeOffset.MinValue;
		}
	}

	public void SignalSettingsUpdated(DateTimeOffset dbUpdatedAtUtc)
	{
		lock (_lock)
		{
			if (_cache is null)
			{
				return;
			}
			if (dbUpdatedAtUtc > _cacheLoadedAt)
			{
				_cache = null;
				_cacheExpiresAt = DateTimeOffset.UtcNow + ReloadDelayAfterSignal;
			}
		}
	}

	private void EnsureLoaded()
	{
		var now = DateTimeOffset.UtcNow;
		if (_cache is not null && now < _cacheExpiresAt)
		{
			return;
		}

		lock (_lock)
		{
			now = DateTimeOffset.UtcNow;
			if (_cache is not null && now < _cacheExpiresAt)
			{
				return;
			}

			AdvancedSettingsRecord? record = null;
			try
			{
				using var scope = scopeFactory.CreateScope();
				var repo = scope.ServiceProvider.GetRequiredService<IAdvancedSettingsRepository>();
				record = repo.GetAsync(CancellationToken.None).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				logger?.LogWarning(ex, "Advanced settings could not be loaded from the database (e.g. AdvancedSettings table missing). Using config defaults. Run migrations to create the table.");
			}

			var api = MergeApiRateLimit(record);
			var cleanup = MergeCleanup(record);
			var tmdbKey = record?.TmdbApiKey;
			var tmdbToken = record?.TmdbReadAccessToken;
			var displayMode = NormalizeDateTimeDisplayMode(record?.DateTimeDisplayMode);
			var timeZoneId = NormalizeTimeZoneId(record?.TimeZoneId);
			var dateOrder = NormalizeDateOrder(record?.DateOrder);
			_cache = (api, cleanup, string.IsNullOrWhiteSpace(tmdbKey) ? null : tmdbKey!.Trim(), string.IsNullOrWhiteSpace(tmdbToken) ? null : tmdbToken!.Trim(), displayMode, timeZoneId, dateOrder);
			_cacheExpiresAt = now + CacheTtl;
			_cacheLoadedAt = record?.UpdatedAtUtc ?? DateTimeOffset.MinValue;
		}
	}

	private static string NormalizeDateTimeDisplayMode(string? value)
	{
		var v = (value ?? string.Empty).Trim().ToLowerInvariant();
		return v is "12h" or "24h" or "relative" ? v : "locale";
	}

	private static string NormalizeTimeZoneId(string? value)
	{
		var v = (value ?? string.Empty).Trim();
		return string.IsNullOrEmpty(v) ? DefaultTimeZoneId : v;
	}

	private static string NormalizeDateOrder(string? value)
	{
		var v = (value ?? string.Empty).Trim().ToLowerInvariant();
		return v is "mdy" or "dmy" or "ymd" ? v : "locale";
	}

	private ApiRateLimitOptions MergeApiRateLimit(AdvancedSettingsRecord? record)
	{
		return new ApiRateLimitOptions
		{
			Enabled = record?.ApiRateLimitEnabled ?? _apiRateLimitConfig.Enabled,
			PermitLimit = record?.ApiRateLimitPermitLimit ?? _apiRateLimitConfig.PermitLimit,
			Window = record?.ApiRateLimitWindowMinutes is int m
				? TimeSpan.FromMinutes(m)
				: _apiRateLimitConfig.Window
		};
	}

	private CleanupOptions MergeCleanup(AdvancedSettingsRecord? record)
	{
		return new CleanupOptions
		{
			Enabled = record?.CleanupEnabled ?? _cleanupConfig.Enabled,
			Interval = record?.CleanupIntervalMinutes is int m
				? TimeSpan.FromMinutes(m)
				: _cleanupConfig.Interval,
			PurgeGuestUsers = record?.CleanupPurgeGuestUsers ?? _cleanupConfig.PurgeGuestUsers,
			GuestUserMaxAge = record?.CleanupGuestUserMaxAgeHours is int h
				? TimeSpan.FromHours(h)
				: _cleanupConfig.GuestUserMaxAge
		};
	}
}
