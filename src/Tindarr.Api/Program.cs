using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;
using Tindarr.Api.Auth;
using Tindarr.Api.Hosting.WindowsService;
using Tindarr.Api.Middleware;
using Tindarr.Application.Abstractions.Domain;
using Tindarr.Application.Abstractions.Caching;
using Tindarr.Application.Abstractions.Integrations;
using Tindarr.Application.Abstractions.Persistence;
using Tindarr.Application.Abstractions.Security;
using Tindarr.Application.Features.Radarr;
using Tindarr.Application.Features.Plex;
using Tindarr.Application.Features.Jellyfin;
using Tindarr.Application.Features.Emby;
using Tindarr.Application.Features.AcceptedMovies;
using Tindarr.Application.Features.Interactions;
using Tindarr.Application.Interfaces.Interactions;
using Tindarr.Application.Interfaces.Auth;
using Tindarr.Application.Interfaces.AcceptedMovies;
using Tindarr.Application.Interfaces.Ops;
using Tindarr.Application.Interfaces.Preferences;
using Tindarr.Application.Interfaces.Integrations;
using Tindarr.Application.Options;
using Tindarr.Application.Services;
using Tindarr.Infrastructure.Caching;
using Tindarr.Infrastructure.Integrations.Jellyfin;
using Tindarr.Infrastructure.Integrations.Emby;
using Tindarr.Infrastructure.Integrations.Radarr;
using Tindarr.Infrastructure.Integrations.Plex;
using Tindarr.Infrastructure.Integrations.Tmdb;
using Tindarr.Infrastructure.Integrations.Tmdb.Http;
using Tindarr.Infrastructure.Interactions;
using Tindarr.Infrastructure.PlexCache;
using Tindarr.Infrastructure.Persistence;
using Tindarr.Infrastructure.Persistence.Repositories;
using Tindarr.Infrastructure.Security;
using Tindarr.Application.Features.Auth;
using Tindarr.Application.Features.Preferences;
using Tindarr.Api.Services;

var isWindowsService = WindowsServiceHostSetup.IsRunningAsWindowsService();
var contentRoot = isWindowsService ? AppContext.BaseDirectory : null;
string? dataDirOverride = null;

// WebRoot must be configured at builder creation time (cannot be changed later).
string? webRoot = null;
if (isWindowsService)
{
	var preConfig = new ConfigurationBuilder()
		.SetBasePath(AppContext.BaseDirectory)
		.AddJsonFile("appsettings.json", optional: true)
		.AddJsonFile("appsettings.Development.json", optional: true)
		.AddEnvironmentVariables()
		.AddCommandLine(args)
		.Build();

	var wsOpts = preConfig.GetSection(WindowsServiceOptions.SectionName).Get<WindowsServiceOptions>() ?? new WindowsServiceOptions();
	var dataDir = WindowsServiceHostSetup.ResolveDataDir(wsOpts);
	Directory.CreateDirectory(dataDir);
	dataDirOverride = dataDir;

	if (wsOpts.UseDataDirWebRoot)
	{
		webRoot = WindowsServiceHostSetup.ResolveTargetWebRoot(dataDir, wsOpts);
		Directory.CreateDirectory(webRoot);
	}
}

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
	Args = args,
	ContentRootPath = contentRoot,
	WebRootPath = webRoot
});

// Dev + service convenience: allow TMDB_API_KEY (flat env var) to populate Tmdb:ApiKey.
// ASP.NET Core's default env var mapping expects "Tmdb__ApiKey".
var tmdbApiKeyEnv = Environment.GetEnvironmentVariable("TMDB_API_KEY");
if (!string.IsNullOrWhiteSpace(tmdbApiKeyEnv) && string.IsNullOrWhiteSpace(builder.Configuration["Tmdb:ApiKey"]))
{
	builder.Configuration["Tmdb:ApiKey"] = tmdbApiKeyEnv;
}

if (OperatingSystem.IsWindows())
{
	// Safe to call in both console and service mode; it only activates when appropriate.
	builder.Host.UseWindowsService();
}

builder.Services.AddControllers();
builder.Services.Configure<Microsoft.AspNetCore.Mvc.JsonOptions>(options =>
{
	// UI sends enums as strings (e.g. "Like"); keep contracts stable.
	options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddOptions<BaseUrlOptions>()
	.BindConfiguration(BaseUrlOptions.SectionName)
	.Validate(o => o.IsValid(), "Invalid BaseUrl configuration.")
	.ValidateOnStart();

builder.Services.AddOptions<DatabaseOptions>()
	.BindConfiguration(DatabaseOptions.SectionName)
	.Validate(o => o.IsValid(), "Invalid Database configuration.")
	.ValidateOnStart();

builder.Services.AddOptions<JwtOptions>()
	.BindConfiguration(JwtOptions.SectionName)
	.Validate(o => o.IsValid(), "Invalid Jwt configuration.")
	.ValidateOnStart();

builder.Services.AddOptions<RegistrationOptions>()
	.BindConfiguration(RegistrationOptions.SectionName)
	.Validate(o => o.IsValid(), "Invalid Registration configuration.")
	.ValidateOnStart();

builder.Services.AddOptions<LoggingOptions>()
	.BindConfiguration(LoggingOptions.SectionName)
	.Validate(o => o.IsValid(), "Invalid Logging configuration.")
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

builder.Services.AddOptions<WindowsServiceOptions>()
	.BindConfiguration(WindowsServiceOptions.SectionName);

builder.Services.AddSingleton<IBaseUrlResolver>(sp =>
{
	var options = sp.GetRequiredService<IOptions<BaseUrlOptions>>().Value;
	return new BaseUrlResolver(options);
});

builder.Services.AddSingleton<ILanAddressResolver, LanAddressResolver>();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpContextCurrentUser>();

builder.Services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
builder.Services.AddSingleton<ITokenSigningKeyStore, DbOrFileTokenSigningKeyStore>();
builder.Services.AddSingleton<ITokenService, JwtTokenService>();

builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IUserPreferencesRepository, UserPreferencesRepository>();
builder.Services.AddScoped<IAcceptedMovieRepository, AcceptedMovieRepository>();
builder.Services.AddScoped<IServiceSettingsRepository, ServiceSettingsRepository>();
builder.Services.AddScoped<ILibraryCacheRepository, LibraryCacheRepository>();
builder.Services.AddScoped<IJoinAddressSettingsRepository, JoinAddressSettingsRepository>();

builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IUserPreferencesService, UserPreferencesService>();
builder.Services.AddScoped<IAcceptedMoviesService, AcceptedMoviesService>();
builder.Services.AddScoped<IRadarrService, RadarrService>();
builder.Services.AddScoped<IPlexService, PlexService>();
builder.Services.AddScoped<IJellyfinService, JellyfinService>();
builder.Services.AddScoped<IEmbyService, EmbyService>();

builder.Services.AddSingleton<IConfigureOptions<JwtBearerOptions>, ConfigureJwtBearerOptions>();

builder.Services.AddAuthentication(options =>
	{
		options.DefaultScheme = "tindarr";
		options.DefaultChallengeScheme = "tindarr";
	})
	.AddPolicyScheme("tindarr", "tindarr", options =>
	{
		options.ForwardDefaultSelector = context =>
		{
			var jwt = context.RequestServices.GetRequiredService<IOptions<JwtOptions>>().Value;
			var env = context.RequestServices.GetRequiredService<IHostEnvironment>();

			var authHeader = context.Request.Headers.Authorization.ToString();
			if (!string.IsNullOrWhiteSpace(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
			{
				return JwtBearerDefaults.AuthenticationScheme;
			}

			// DEV fallback for existing UI: allow header-based auth only when explicitly enabled.
			if (env.IsDevelopment() && jwt.AllowDevHeaderFallback
				&& context.Request.Headers.ContainsKey(DevHeaderAuthenticationDefaults.UserIdHeader))
			{
				return DevHeaderAuthenticationDefaults.Scheme;
			}

			return JwtBearerDefaults.AuthenticationScheme;
		};
	})
	.AddJwtBearer(JwtBearerDefaults.AuthenticationScheme)
	.AddScheme<AuthenticationSchemeOptions, DevHeaderAuthenticationHandler>(DevHeaderAuthenticationDefaults.Scheme, _ => { });

builder.Services.AddAuthorization(options =>
{
	options.AddPolicy(Policies.AdminOnly, policy => policy.RequireRole(Policies.AdminRole));
	options.AddPolicy(Policies.CuratorOnly, policy => policy.RequireRole(Policies.CuratorRole));
	options.AddPolicy(Policies.ContributorOrCurator, policy => policy.RequireRole(Policies.ContributorRole, Policies.CuratorRole, Policies.AdminRole));
});

builder.Services.AddCors(options =>
{
	options.AddPolicy("devclient", policy =>
	{
		policy.AllowAnyHeader()
			.AllowAnyMethod()
			.AllowCredentials()
			.SetIsOriginAllowed(_ => true);
	});
});

builder.Services.AddScoped<EfCoreInteractionStore>();
builder.Services.AddSingleton<Tindarr.Infrastructure.Interactions.InMemoryInteractionStore>();
builder.Services.AddScoped<IInteractionStore, Tindarr.Infrastructure.Interactions.RoutingInteractionStore>();
builder.Services.AddMemoryCache();

// Persist TMDB metadata separately from the main tindarr.db.
// This avoids repeated upstream TMDB calls across restarts.
var tmdbMetadataDbPath = Path.Combine(dataDirOverride ?? builder.Environment.ContentRootPath, "tmdbmetadata.db");
builder.Services.AddSingleton<ITmdbCache>(sp =>
	new MemoryOrDbTmdbCache(sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>(), tmdbMetadataDbPath));
builder.Services.AddSingleton<ITmdbCacheAdmin>(sp => (ITmdbCacheAdmin)sp.GetRequiredService<ITmdbCache>());

builder.Services.AddSingleton<ITmdbMetadataStore>(_ => new TmdbMetadataStore(tmdbMetadataDbPath));

builder.Services.AddHttpClient("tmdb-images");
builder.Services.AddSingleton<ITmdbImageCache>(sp =>
{
	var client = sp.GetRequiredService<IHttpClientFactory>().CreateClient("tmdb-images");
	return new TmdbImageCache(client, sp.GetRequiredService<IOptions<TmdbOptions>>(), tmdbMetadataDbPath);
});

builder.Services.AddSingleton<ITmdbBuildJob, TmdbBuildJob>();

builder.Services.AddSingleton<ITmdbRateLimiter, TokenBucketRateLimiter>();
builder.Services.AddTransient<TmdbCachingHandler>();
builder.Services.AddTransient<TmdbRateLimitingHandler>();

builder.Services.AddHttpClient<ITmdbClient, TmdbClient>((sp, client) =>
{
	var tmdb = sp.GetRequiredService<IOptions<TmdbOptions>>().Value;
	client.BaseAddress = new Uri(tmdb.BaseUrl);
	client.DefaultRequestHeaders.Accept.ParseAdd("application/json");

	// Prefer Bearer token auth when provided (recommended by TMDB; avoids query-string secrets).
	if (!string.IsNullOrWhiteSpace(tmdb.ReadAccessToken))
	{
		client.DefaultRequestHeaders.Authorization =
			new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tmdb.ReadAccessToken);
	}
})
.AddHttpMessageHandler<TmdbCachingHandler>()
.AddHttpMessageHandler<TmdbRateLimitingHandler>()
.AddHttpMessageHandler(() => new TmdbRetryHandler(maxRetries: 3));

builder.Services.AddHttpClient<IRadarrClient, RadarrClient>();
builder.Services.AddHttpClient<IPlexAuthClient, PlexAuthClient>();
builder.Services.AddHttpClient<IPlexLibraryClient, PlexLibraryClient>();
builder.Services.AddHttpClient<IJellyfinClient, JellyfinClient>();
builder.Services.AddHttpClient<IEmbyClient, EmbyClient>();

builder.Services.AddScoped<TmdbSwipeDeckSource>();
builder.Services.AddScoped<Tindarr.Infrastructure.Integrations.Plex.PlexSwipeDeckSource>();
builder.Services.AddScoped<Tindarr.Infrastructure.Integrations.Jellyfin.JellyfinSwipeDeckSource>();
builder.Services.AddScoped<Tindarr.Infrastructure.Integrations.Emby.EmbySwipeDeckSource>();
builder.Services.AddScoped<ISwipeDeckSource, Tindarr.Infrastructure.Integrations.Interactions.CompositeSwipeDeckSource>();
builder.Services.AddScoped<IInteractionService, InteractionService>();
builder.Services.AddScoped<ISwipeDeckService, SwipeDeckService>();
builder.Services.AddScoped<IMatchingEngine, MatchingEngine>();

builder.Services.AddSingleton<Tindarr.Application.Interfaces.Rooms.IRoomStore, Tindarr.Infrastructure.Rooms.InMemoryRoomStore>();
builder.Services.AddSingleton<Tindarr.Application.Interfaces.Rooms.IRoomInteractionStore, Tindarr.Infrastructure.Rooms.InMemoryRoomInteractionStore>();
builder.Services.AddScoped<Tindarr.Application.Interfaces.Rooms.IRoomService, Tindarr.Application.Features.Rooms.RoomService>();

// Keep DB location stable across Debug/Release so migrations and runtime match.
builder.Services.AddTindarrPersistence(builder.Configuration, overrideDataDir: dataDirOverride ?? builder.Environment.ContentRootPath);
builder.Services.AddPlexCache(builder.Configuration, overrideDataDir: dataDirOverride ?? builder.Environment.ContentRootPath);

var app = builder.Build();

// Ensure the main app database schema exists on startup.
// Without this, a fresh `tindarr.db` will cause runtime 500s (e.g., login) due to missing tables.
using (var scope = app.Services.CreateScope())
{
	var env = scope.ServiceProvider.GetRequiredService<IHostEnvironment>();
	var db = scope.ServiceProvider.GetRequiredService<TindarrDbContext>();
	var plexCacheDb = scope.ServiceProvider.GetRequiredService<Tindarr.Infrastructure.PlexCache.PlexCacheDbContext>();

	// Safe to run repeatedly; in prod environments you may choose to disable if desired.
	if (env.IsDevelopment() || isWindowsService)
	{
		db.Database.Migrate();
		plexCacheDb.Database.EnsureCreated();
	}
}

if (isWindowsService)
{
	var wsOpts = app.Services.GetRequiredService<IOptions<WindowsServiceOptions>>().Value;
	var dataDir = WindowsServiceHostSetup.ResolveDataDir(wsOpts);
	var sourceWebRoot = WindowsServiceHostSetup.ResolveSourceWebRoot(app.Environment.ContentRootPath, wsOpts);
	var targetWebRoot = app.Environment.WebRootPath;

	if (wsOpts.MirrorWebRootOnStart && wsOpts.UseDataDirWebRoot)
	{
		WebRootMirror.MirrorDirectory(sourceWebRoot, targetWebRoot, app.Logger);
	}
}

app.UseCors("devclient");

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// SPA fallback for non-API routes only.
app.MapFallback(async context =>
{
	if (context.Request.Path.StartsWithSegments("/api"))
	{
		context.Response.StatusCode = StatusCodes.Status404NotFound;
		return;
	}

	var indexPath = Path.Combine(app.Environment.WebRootPath, "index.html");
	if (!File.Exists(indexPath))
	{
		// Avoid throwing (and producing a 500) when the SPA hasn't been built/copied yet.
		context.Response.StatusCode = StatusCodes.Status404NotFound;
		await context.Response.WriteAsync("UI not installed (missing wwwroot/index.html).");
		return;
	}

	await context.Response.SendFileAsync(indexPath);
});

app.Run();
