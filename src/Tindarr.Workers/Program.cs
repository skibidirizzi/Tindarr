using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting.WindowsServices;
using Tindarr.Application.Interfaces.Ops;
using Tindarr.Application.Options;
using Tindarr.Application.Services;
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

builder.Services.AddSingleton<IBaseUrlResolver>(sp =>
{
	var options = sp.GetRequiredService<IOptions<BaseUrlOptions>>().Value;
	return new BaseUrlResolver(options);
});

// Core-only worker stubs (periodic background services).
builder.Services.AddHostedService<OutboxWorker>();
builder.Services.AddHostedService<TmdbMetadataWorker>();
builder.Services.AddHostedService<MatchComputationWorker>();
builder.Services.AddHostedService<MediaServerSyncWorker>();
builder.Services.AddHostedService<RadarrRequestWorker>();
builder.Services.AddHostedService<CleanupWorker>();
builder.Services.AddHostedService<HealthHeartbeatWorker>();
builder.Services.AddHostedService<RecommendationWorker>();
builder.Services.AddHostedService<ImageCacheWorker>();
builder.Services.AddHostedService<CastSessionWorker>();

await builder.Build().RunAsync();
