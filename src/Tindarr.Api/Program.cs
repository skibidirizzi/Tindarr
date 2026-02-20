using System.Security.Claims;
using System.Threading.RateLimiting;
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
using Tindarr.Infrastructure.Casting;
using Tindarr.Infrastructure.Integrations.Jellyfin;
using Tindarr.Infrastructure.Integrations.Emby;
using Tindarr.Infrastructure.Integrations.Radarr;
using Tindarr.Infrastructure.Integrations.Plex;
using Tindarr.Infrastructure.Integrations.Tmdb;
using Tindarr.Infrastructure.Integrations.Tmdb.Http;
using Tindarr.Infrastructure.Interactions;
using Tindarr.Infrastructure.Playback.Providers;
using Tindarr.Infrastructure.EmbyCache;
using Tindarr.Infrastructure.JellyfinCache;
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

// When running as a console app (not a service) outside Development, use a writable data dir
// so the app works when installed under Program Files. Service and Development keep existing behavior.
var effectiveDataDir = dataDirOverride;
if (effectiveDataDir is null && !isWindowsService && !builder.Environment.IsDevelopment()
	&& string.IsNullOrWhiteSpace(builder.Configuration["Database:DataDir"]))
{
	effectiveDataDir = Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
		"Tindarr");
	Directory.CreateDirectory(effectiveDataDir);
}

static string ResolveTmdbMetadataDbPath(IConfiguration config, string? dataDirOverride, IHostEnvironment env)
{
	var configuredDbPath = config["Tmdb:MetadataDbPath"];
	if (!string.IsNullOrWhiteSpace(configuredDbPath))
	{
		return Path.IsPathRooted(configuredDbPath)
			? configuredDbPath
			: Path.GetFullPath(Path.Combine(dataDirOverride ?? config["Database:DataDir"] ?? env.ContentRootPath, configuredDbPath));
	}

	var dataRoot = dataDirOverride ?? config["Database:DataDir"] ?? env.ContentRootPath;
	return Path.Combine(dataRoot, "tmdbmetadata.db");
}

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

builder.Services.AddHealthChecks();

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

builder.Services.AddOptions<PlaybackOptions>()
	.BindConfiguration(PlaybackOptions.SectionName)
	.Validate(o => o.IsValid(), "Invalid Playback configuration.")
	.ValidateOnStart();

builder.Services.AddOptions<UpdateCheckOptions>()
	.BindConfiguration(UpdateCheckOptions.SectionName)
	.Validate(o => o.IsValid(), "Invalid UpdateCheck configuration.")
	.ValidateOnStart();

builder.Services.AddOptions<WindowsServiceOptions>()
	.BindConfiguration(WindowsServiceOptions.SectionName);

builder.Services.AddOptions<CleanupOptions>()
	.BindConfiguration(CleanupOptions.SectionName)
	.Validate(o => o.IsValid(), "Invalid Cleanup configuration.")
	.ValidateOnStart();

builder.Services.AddOptions<ApiRateLimitOptions>()
	.BindConfiguration(ApiRateLimitOptions.SectionName)
	.Validate(o => o.IsValid(), "Invalid ApiRateLimit configuration.")
	.ValidateOnStart();

builder.Services.AddRateLimiter(options =>
{
	options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
	options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
	{
		var path = context.Request.Path.Value ?? "";
		if (path is "/health" or "/api/v1/health" or "/info" or "/api/v1/info")
		{
			return RateLimitPartition.GetNoLimiter("excluded");
		}

		var effective = context.RequestServices.GetRequiredService<Tindarr.Application.Abstractions.Ops.IEffectiveAdvancedSettings>();
		var apiLimitOpts = effective.GetApiRateLimitOptions();
		if (!apiLimitOpts.Enabled)
		{
			return RateLimitPartition.GetNoLimiter("disabled");
		}

		var partitionKey = context.User?.Identity?.IsAuthenticated == true
			? "user:" + (context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown")
			: "ip:" + (context.Connection.RemoteIpAddress?.ToString() ?? "unknown");

		return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
		{
			PermitLimit = apiLimitOpts.PermitLimit,
			Window = apiLimitOpts.Window,
			QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
			QueueLimit = 0
		});
	});
});

builder.Services.AddSingleton<IBaseUrlResolver>(sp =>
{
	var options = sp.GetRequiredService<IOptions<BaseUrlOptions>>().Value;
	return new BaseUrlResolver(options);
});

builder.Services.AddSingleton<ILanAddressResolver, LanAddressResolver>();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpContextCurrentUser>();

builder.Services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
builder.Services.AddSingleton<ITokenSigningKeyStore>(sp =>
	new DbOrFileTokenSigningKeyStore(sp.GetRequiredService<IOptions<JwtOptions>>(), effectiveDataDir));
builder.Services.AddSingleton<ITokenService, JwtTokenService>();
builder.Services.AddSingleton<IPlaybackTokenService, PlaybackTokenService>();
builder.Services.AddSingleton<ICastUrlTokenService, CastUrlTokenService>();
builder.Services.AddSingleton<Tindarr.Infrastructure.Casting.CastingSessionStore>();

builder.Services.AddSingleton<Tindarr.Application.Interfaces.Casting.ICastClient, SharpCasterCastClient>();

builder.Services.AddScoped<Tindarr.Application.Interfaces.Playback.IPlaybackProvider>(sp => sp.GetRequiredService<PlexPlaybackProvider>());
builder.Services.AddScoped<Tindarr.Application.Interfaces.Playback.IPlaybackProvider>(sp => sp.GetRequiredService<JellyfinPlaybackProvider>());
builder.Services.AddScoped<Tindarr.Application.Interfaces.Playback.IPlaybackProvider>(sp => sp.GetRequiredService<EmbyPlaybackProvider>());

builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IUserPreferencesRepository, UserPreferencesRepository>();
builder.Services.AddScoped<IAcceptedMovieRepository, AcceptedMovieRepository>();
builder.Services.AddScoped<IServiceSettingsRepository, ServiceSettingsRepository>();
builder.Services.AddScoped<IRadarrPendingAddRepository, RadarrPendingAddRepository>();
builder.Services.AddScoped<ILibraryCacheRepository, LibraryCacheRepository>();
builder.Services.AddScoped<IJoinAddressSettingsRepository, JoinAddressSettingsRepository>();
builder.Services.AddScoped<ICastingSettingsRepository, CastingSettingsRepository>();
builder.Services.AddScoped<IAdvancedSettingsRepository, Tindarr.Infrastructure.Persistence.Repositories.AdvancedSettingsRepository>();
builder.Services.AddSingleton<Tindarr.Application.Abstractions.Ops.IEffectiveAdvancedSettings, Tindarr.Infrastructure.Ops.EffectiveAdvancedSettings>();

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
	// Default: authenticated non-guest only.
	options.DefaultPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
		.RequireAuthenticatedUser()
		.RequireAssertion(ctx => !ctx.User.IsInRole(Policies.GuestRole))
		.Build();

	options.AddPolicy(Policies.AdminOnly, policy => policy.RequireRole(Policies.AdminRole));
	options.AddPolicy(Policies.CuratorOnly, policy => policy.RequireRole(Policies.CuratorRole));
	options.AddPolicy(Policies.ContributorOrCurator, policy => policy.RequireRole(Policies.ContributorRole, Policies.CuratorRole, Policies.AdminRole));
	options.AddPolicy(Policies.AllowGuests, policy => policy.RequireAuthenticatedUser());
	options.AddPolicy(Policies.RoomAccess, policy =>
	{
		policy.RequireAuthenticatedUser();
		policy.AddRequirements(new Tindarr.Api.Auth.RoomAccessRequirement());
	});
});

builder.Services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, Tindarr.Api.Auth.RoomAccessAuthorizationHandler>();

builder.Services.AddCors(options =>
{
	options.AddPolicy("devclient", policy =>
	{
		policy.AllowAnyHeader()
			.AllowAnyMethod()
			.AllowCredentials();
		// Issue 104: never use wildcard origin with credentials; allow only explicit origins.
		if (builder.Environment.IsDevelopment())
		{
			policy.WithOrigins(
				"http://localhost:5173",
				"http://127.0.0.1:5173",
				"http://localhost:3000",
				"http://127.0.0.1:3000",
				"http://localhost:6565",
				"http://127.0.0.1:6565"
			);
		}
		else
		{
			// Production: allow localhost/127.0.0.1 so the installed app (UI served from API at e.g. http://localhost:5000) works.
			policy.SetIsOriginAllowed(origin =>
			{
				if (string.IsNullOrEmpty(origin)) return false;
				if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || !uri.IsAbsoluteUri) return false;
				return (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || uri.Host == "127.0.0.1")
					&& (uri.Scheme == "http" || uri.Scheme == "https");
			});
		}
	});
});

var tmdbMetadataDbPath = ResolveTmdbMetadataDbPath(builder.Configuration, effectiveDataDir, builder.Environment);

builder.Services.AddScoped<EfCoreInteractionStore>();
builder.Services.AddSingleton<Tindarr.Infrastructure.Interactions.InMemoryInteractionStore>();
builder.Services.AddScoped<IInteractionStore, Tindarr.Infrastructure.Interactions.RoutingInteractionStore>();
builder.Services.AddMemoryCache();

builder.Services.AddHttpClient<Tindarr.Api.Services.IUpdateChecker, Tindarr.Api.Services.GitHubReleaseUpdateChecker>((sp, client) =>
{
	client.BaseAddress = new Uri("https://api.github.com/");
	client.Timeout = TimeSpan.FromSeconds(10);
	client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
	client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
	client.DefaultRequestHeaders.UserAgent.ParseAdd("Tindarr");
});

// Persist TMDB metadata separately from the main tindarr.db.
// This avoids repeated upstream TMDB calls across restarts.
builder.Services.AddSingleton<ITmdbCache>(sp =>
	new MemoryOrDbTmdbCache(sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>(), tmdbMetadataDbPath));
builder.Services.AddSingleton<ITmdbCacheAdmin>(sp => (ITmdbCacheAdmin)sp.GetRequiredService<ITmdbCache>());

builder.Services.AddSingleton<ITmdbMetadataStore>(_ => new TmdbMetadataStore(tmdbMetadataDbPath));

builder.Services.AddHttpClient("tmdb-images");
builder.Services.AddSingleton<ITmdbImageCache>(sp =>
{
	var client = sp.GetRequiredService<IHttpClientFactory>().CreateClient("tmdb-images");
	return new TmdbImageCache(
		client,
		sp.GetRequiredService<IOptions<TmdbOptions>>(),
		sp.GetRequiredService<Tindarr.Application.Abstractions.Ops.IEffectiveAdvancedSettings>(),
		tmdbMetadataDbPath);
});

builder.Services.AddSingleton<ITmdbBuildJob, TmdbBuildJob>();

builder.Services.AddSingleton<ITmdbRateLimiter, Tindarr.Infrastructure.Caching.TokenBucketRateLimiter>();
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

builder.Services.AddSingleton<Tindarr.Api.Services.IPlexLibrarySyncJobService, Tindarr.Api.Services.PlexLibrarySyncJobService>();

builder.Services.AddHttpClient<PlexPlaybackProvider>();
builder.Services.AddHttpClient<JellyfinPlaybackProvider>();
builder.Services.AddHttpClient<EmbyPlaybackProvider>();

builder.Services.AddScoped<Tindarr.Infrastructure.Integrations.Interactions.TmdbSwipeDeckCandidateBuilder>();
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
builder.Services.AddSingleton<Tindarr.Application.Interfaces.Rooms.IRoomLifetimeProvider, Tindarr.Infrastructure.Rooms.RoomLifetimeProvider>();
builder.Services.AddScoped<Tindarr.Application.Interfaces.Rooms.IRoomService, Tindarr.Application.Features.Rooms.RoomService>();

builder.Services.AddHostedService<Tindarr.Api.Hosting.RoomCleanupHostedService>();

// Keep DB location stable across Debug/Release so migrations and runtime match.
// Integration tests set Database:DataDir to a unique dir and Database:UseConfigDataDir=true so we do not override with ContentRootPath.
var useConfigDataDir = string.Equals(builder.Configuration["Database:UseConfigDataDir"], "true", StringComparison.OrdinalIgnoreCase);
var persistenceDataDir = useConfigDataDir ? null : (effectiveDataDir ?? builder.Environment.ContentRootPath);
builder.Services.AddTindarrPersistence(builder.Configuration, overrideDataDir: persistenceDataDir);
builder.Services.AddPlexCache(builder.Configuration, overrideDataDir: persistenceDataDir);
builder.Services.AddJellyfinCache(builder.Configuration, overrideDataDir: persistenceDataDir);
builder.Services.AddEmbyCache(builder.Configuration, overrideDataDir: persistenceDataDir);

var app = builder.Build();

// Ensure the main app database schema exists on startup.
// Without this, a fresh `tindarr.db` will cause runtime 500s (e.g., login, advanced settings) due to missing tables.
using (var scope = app.Services.CreateScope())
	{
	var db = scope.ServiceProvider.GetRequiredService<TindarrDbContext>();
	var plexCacheDb = scope.ServiceProvider.GetRequiredService<Tindarr.Infrastructure.PlexCache.PlexCacheDbContext>();
	var jellyfinCacheDb = scope.ServiceProvider.GetRequiredService<JellyfinCacheDbContext>();
	var embyCacheDb = scope.ServiceProvider.GetRequiredService<EmbyCacheDbContext>();

	// Main app DB: always run migrations so all tables (including AdvancedSettings) exist.
	db.Database.Migrate();

	// Fallback: if the AddAdvancedSettings migration was not applied (e.g. not discovered), create the table and record it.
	void EnsureAdvancedSettingsTableExists(TindarrDbContext dbContext)
	{
		try
		{
			dbContext.Database.ExecuteSqlRaw(@"CREATE TABLE IF NOT EXISTS AdvancedSettings (
Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
ApiRateLimitEnabled INTEGER NULL,
ApiRateLimitPermitLimit INTEGER NULL,
ApiRateLimitWindowMinutes INTEGER NULL,
CleanupEnabled INTEGER NULL,
CleanupIntervalMinutes INTEGER NULL,
CleanupPurgeGuestUsers INTEGER NULL,
CleanupGuestUserMaxAgeHours INTEGER NULL,
UpdatedAtUtc TEXT NOT NULL)");
			dbContext.Database.ExecuteSqlRaw(
				"INSERT OR IGNORE INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ('20260220000000_AddAdvancedSettings', '8.0.23')");
		}
		catch
		{
			// Table may already exist from migration; ignore.
		}

		// Add TmdbApiKey column if missing (e.g. table was created before 20260220120000 migration).
		try
		{
			dbContext.Database.ExecuteSqlRaw("ALTER TABLE AdvancedSettings ADD COLUMN TmdbApiKey TEXT NULL");
		}
		catch
		{
			// Column may already exist; ignore.
		}

		try
		{
			dbContext.Database.ExecuteSqlRaw("ALTER TABLE AdvancedSettings ADD COLUMN DateTimeDisplayMode TEXT NULL");
		}
		catch
		{
			// Column may already exist; ignore.
		}

		try
		{
			dbContext.Database.ExecuteSqlRaw("ALTER TABLE AdvancedSettings ADD COLUMN TimeZoneId TEXT NULL");
		}
		catch
		{
			// Column may already exist; ignore.
		}

		try
		{
			dbContext.Database.ExecuteSqlRaw("ALTER TABLE AdvancedSettings ADD COLUMN DateOrder TEXT NULL");
		}
		catch
		{
			// Column may already exist; ignore.
		}
	}
	EnsureAdvancedSettingsTableExists(db);

	// Cache DBs: always create on startup so all DBs exist even if the user never uses Plex/Jellyfin/Emby.
	plexCacheDb.Database.EnsureCreated();
	jellyfinCacheDb.Database.EnsureCreated();
	embyCacheDb.Database.EnsureCreated();
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
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllers();

app.MapHealthChecks("/health");
app.MapHealthChecks("/api/v1/health");

static object BuildInfo(IHostEnvironment env) => new
{
	Name = "Tindarr",
	Version = Tindarr.Application.Common.TindarrVersion.Current.ToString(3),
	Environment = env.EnvironmentName,
	UtcNow = DateTimeOffset.UtcNow.ToString("O")
};

app.MapGet("/info", (IHostEnvironment env) => Results.Ok(BuildInfo(env)));
app.MapGet("/api/v1/info", (IHostEnvironment env) => Results.Ok(BuildInfo(env)));

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

// Expose for integration tests (WebApplicationFactory<Program>).
public partial class Program { }
