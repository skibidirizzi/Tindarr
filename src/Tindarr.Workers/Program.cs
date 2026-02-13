using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting.WindowsServices;
using Tindarr.Application.Abstractions.Caching;
using Tindarr.Application.Abstractions.Integrations;
using Tindarr.Application.Abstractions.Persistence;
using Tindarr.Application.Features.Plex;
using Tindarr.Application.Features.Radarr;
using Tindarr.Application.Features.Jellyfin;
using Tindarr.Application.Features.Emby;
using Tindarr.Application.Interfaces.Integrations;
using Tindarr.Application.Interfaces.Ops;
using Tindarr.Application.Interfaces.Preferences;
using Tindarr.Application.Features.Preferences;
using Tindarr.Application.Options;
using Tindarr.Application.Services;
using Tindarr.Infrastructure.Caching;
using Tindarr.Infrastructure.Integrations.Jellyfin;
using Tindarr.Infrastructure.Integrations.Emby;
using Tindarr.Infrastructure.Integrations.Plex;
using Tindarr.Infrastructure.Integrations.Radarr;
using Tindarr.Infrastructure.Integrations.Tmdb;
using Tindarr.Infrastructure.Integrations.Tmdb.Http;
using Tindarr.Infrastructure.Persistence;
using Tindarr.Infrastructure.Persistence.Repositories;
using Tindarr.Workers.Jobs;

var isWindowsService = OperatingSystem.IsWindows() && !Environment.UserInteractive;

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
	Args = args,
	ContentRootPath = isWindowsService ? AppContext.BaseDirectory : null
});

var tmdbApiKeyEnv = Environment.GetEnvironmentVariable("TMDB_API_KEY");
if (!string.IsNullOrWhiteSpace(tmdbApiKeyEnv) && string.IsNullOrWhiteSpace(builder.Configuration["Tmdb:ApiKey"]))
{
	builder.Configuration["Tmdb:ApiKey"] = tmdbApiKeyEnv;
}

if (isWindowsService)
{
	// Enable Windows Service lifetime for worker host.
	builder.Services.AddWindowsService();
}

builder.Services.AddOptions<BaseUrlOptions>()
	.BindConfiguration(BaseUrlOptions.SectionName)
	.Validate(o => o.IsValid(), "Invalid BaseUrl configuration.")
	.ValidateOnStart();

builder.Services.AddOptions<DatabaseOptions>()
	.BindConfiguration(DatabaseOptions.SectionName)
	.Validate(o => o.IsValid(), "Invalid Database configuration.")
	.ValidateOnStart();

builder.Services.AddOptions<TmdbOptions>()
	.BindConfiguration(TmdbOptions.SectionName)
	.Validate(o => o.IsValid(), "Invalid Tmdb configuration.")
	.ValidateOnStart();

builder.Services.AddOptions<RadarrOptions>()
	.BindConfiguration(RadarrOptions.SectionName)
	.Validate(o => o.IsValid(), "Invalid Radarr configuration.")
	.ValidateOnStart();

builder.Services.AddOptions<PlexOptions>()
	.BindConfiguration(PlexOptions.SectionName)
	.Validate(o => o.IsValid(), "Invalid Plex configuration.")
	.ValidateOnStart();

builder.Services.AddOptions<JellyfinOptions>()
	.BindConfiguration(JellyfinOptions.SectionName)
	.Validate(o => o.IsValid(), "Invalid Jellyfin configuration.")
	.ValidateOnStart();

builder.Services.AddOptions<EmbyOptions>()
	.BindConfiguration(EmbyOptions.SectionName)
	.Validate(o => o.IsValid(), "Invalid Emby configuration.")
	.ValidateOnStart();

builder.Services.AddSingleton<IBaseUrlResolver>(sp =>
{
	var options = sp.GetRequiredService<IOptions<BaseUrlOptions>>().Value;
	return new BaseUrlResolver(options);
});

builder.Services.AddSingleton<ILanAddressResolver, LanAddressResolver>();

builder.Services.AddTindarrPersistence(builder.Configuration);

builder.Services.AddScoped<IAcceptedMovieRepository, AcceptedMovieRepository>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IUserPreferencesRepository, UserPreferencesRepository>();
builder.Services.AddScoped<IServiceSettingsRepository, ServiceSettingsRepository>();
builder.Services.AddScoped<ILibraryCacheRepository, LibraryCacheRepository>();
builder.Services.AddScoped<IUserPreferencesService, UserPreferencesService>();
builder.Services.AddScoped<IRadarrService, RadarrService>();
builder.Services.AddScoped<IPlexService, PlexService>();
builder.Services.AddScoped<IJellyfinService, JellyfinService>();
builder.Services.AddScoped<IEmbyService, EmbyService>();

builder.Services.AddHttpClient<IRadarrClient, RadarrClient>();
builder.Services.AddHttpClient<IPlexAuthClient, PlexAuthClient>();
builder.Services.AddHttpClient<IPlexLibraryClient, PlexLibraryClient>();
builder.Services.AddHttpClient<IJellyfinClient, JellyfinClient>();
builder.Services.AddHttpClient<IEmbyClient, EmbyClient>();

builder.Services.AddMemoryCache();
builder.Services.AddSingleton<ITmdbCache>(sp =>
{
	var tmdbMetadataDbPath = Path.Combine(builder.Environment.ContentRootPath, "tmdbmetadata.db");
	return new MemoryOrDbTmdbCache(sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>(), tmdbMetadataDbPath);
});
builder.Services.AddSingleton<ITmdbCacheAdmin>(sp => (ITmdbCacheAdmin)sp.GetRequiredService<ITmdbCache>());

builder.Services.AddSingleton<ITmdbMetadataStore>(sp =>
{
	var tmdbMetadataDbPath = Path.Combine(builder.Environment.ContentRootPath, "tmdbmetadata.db");
	return new TmdbMetadataStore(tmdbMetadataDbPath);
});

builder.Services.AddHttpClient("tmdb-images");
builder.Services.AddSingleton<ITmdbImageCache>(sp =>
{
	var tmdbMetadataDbPath = Path.Combine(builder.Environment.ContentRootPath, "tmdbmetadata.db");
	var client = sp.GetRequiredService<IHttpClientFactory>().CreateClient("tmdb-images");
	return new TmdbImageCache(client, sp.GetRequiredService<IOptions<TmdbOptions>>(), tmdbMetadataDbPath);
});

builder.Services.AddSingleton<ITmdbRateLimiter, TokenBucketRateLimiter>();
builder.Services.AddTransient<TmdbCachingHandler>();
builder.Services.AddTransient<TmdbRateLimitingHandler>();

builder.Services.AddHttpClient<ITmdbClient, TmdbClient>((sp, client) =>
{
	var tmdb = sp.GetRequiredService<IOptions<TmdbOptions>>().Value;
	client.BaseAddress = new Uri(tmdb.BaseUrl);
	client.DefaultRequestHeaders.Accept.ParseAdd("application/json");

	if (!string.IsNullOrWhiteSpace(tmdb.ReadAccessToken))
	{
		client.DefaultRequestHeaders.Authorization =
			new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tmdb.ReadAccessToken);
	}
})
.AddHttpMessageHandler<TmdbCachingHandler>()
.AddHttpMessageHandler<TmdbRateLimitingHandler>()
.AddHttpMessageHandler(() => new TmdbRetryHandler(maxRetries: 3));

// Core-only worker stubs (periodic background services).
builder.Services.AddHostedService<OutboxWorker>();
builder.Services.AddHostedService<TmdbMetadataWorker>();
builder.Services.AddHostedService<TmdbDiscoverPrewarmWorker>();
builder.Services.AddHostedService<TmdbDetailsBackfillWorker>();
builder.Services.AddHostedService<MatchComputationWorker>();
builder.Services.AddHostedService<MediaServerSyncWorker>();
builder.Services.AddHostedService<RadarrAutoAddWorker>();
builder.Services.AddHostedService<PlexLibrarySyncWorker>();
builder.Services.AddHostedService<CleanupWorker>();
builder.Services.AddHostedService<HealthHeartbeatWorker>();
builder.Services.AddHostedService<RecommendationWorker>();
builder.Services.AddHostedService<ImageCacheWorker>();
builder.Services.AddHostedService<CastSessionWorker>();

await builder.Build().RunAsync();
