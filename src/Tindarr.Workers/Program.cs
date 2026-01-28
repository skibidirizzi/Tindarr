using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting.WindowsServices;
using Tindarr.Application.Abstractions.Integrations;
using Tindarr.Application.Abstractions.Persistence;
using Tindarr.Application.Features.Radarr;
using Tindarr.Application.Interfaces.Integrations;
using Tindarr.Application.Interfaces.Ops;
using Tindarr.Application.Options;
using Tindarr.Application.Services;
using Tindarr.Infrastructure.Integrations.Radarr;
using Tindarr.Infrastructure.Persistence;
using Tindarr.Infrastructure.Persistence.Repositories;
using Tindarr.Workers.Jobs;

var isWindowsService = OperatingSystem.IsWindows() && !Environment.UserInteractive;

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
	Args = args,
	ContentRootPath = isWindowsService ? AppContext.BaseDirectory : null
});

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

builder.Services.AddOptions<RadarrOptions>()
	.BindConfiguration(RadarrOptions.SectionName)
	.Validate(o => o.IsValid(), "Invalid Radarr configuration.")
	.ValidateOnStart();

builder.Services.AddSingleton<IBaseUrlResolver>(sp =>
{
	var options = sp.GetRequiredService<IOptions<BaseUrlOptions>>().Value;
	return new BaseUrlResolver(options);
});

builder.Services.AddTindarrPersistence(builder.Configuration);

builder.Services.AddScoped<IAcceptedMovieRepository, AcceptedMovieRepository>();
builder.Services.AddScoped<IServiceSettingsRepository, ServiceSettingsRepository>();
builder.Services.AddScoped<ILibraryCacheRepository, LibraryCacheRepository>();
builder.Services.AddScoped<IRadarrService, RadarrService>();

builder.Services.AddHttpClient<IRadarrClient, RadarrClient>();

// Core-only worker stubs (periodic background services).
builder.Services.AddHostedService<OutboxWorker>();
builder.Services.AddHostedService<TmdbMetadataWorker>();
builder.Services.AddHostedService<MatchComputationWorker>();
builder.Services.AddHostedService<MediaServerSyncWorker>();
builder.Services.AddHostedService<RadarrAutoAddWorker>();
builder.Services.AddHostedService<CleanupWorker>();
builder.Services.AddHostedService<HealthHeartbeatWorker>();
builder.Services.AddHostedService<RecommendationWorker>();
builder.Services.AddHostedService<ImageCacheWorker>();
builder.Services.AddHostedService<CastSessionWorker>();

await builder.Build().RunAsync();
