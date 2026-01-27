using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Tindarr.Api.Auth;
using Tindarr.Api.Hosting.WindowsService;
using Tindarr.Api.Middleware;
using Tindarr.Application.Features.Interactions;
using Tindarr.Application.Interfaces.Interactions;
using Tindarr.Application.Interfaces.Ops;
using Tindarr.Application.Options;
using Tindarr.Application.Services;
using Tindarr.Infrastructure.Integrations.Tmdb;
using Tindarr.Infrastructure.Interactions;
using Tindarr.Infrastructure.Persistence;

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

if (OperatingSystem.IsWindows())
{
	// Safe to call in both console and service mode; it only activates when appropriate.
	builder.Host.UseWindowsService();
}

builder.Services.AddControllers();

builder.Services.AddOptions<BaseUrlOptions>()
	.BindConfiguration(BaseUrlOptions.SectionName)
	.Validate(o => o.IsValid(), "Invalid BaseUrl configuration.")
	.ValidateOnStart();

builder.Services.AddOptions<DatabaseOptions>()
	.BindConfiguration(DatabaseOptions.SectionName)
	.Validate(o => o.IsValid(), "Invalid Database configuration.")
	.ValidateOnStart();

builder.Services.AddOptions<WindowsServiceOptions>()
	.BindConfiguration(WindowsServiceOptions.SectionName);

builder.Services.AddSingleton<IBaseUrlResolver>(sp =>
{
	var options = sp.GetRequiredService<IOptions<BaseUrlOptions>>().Value;
	return new BaseUrlResolver(options);
});

builder.Services.AddAuthentication(DevHeaderAuthenticationDefaults.Scheme)
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

builder.Services.AddScoped<IInteractionStore, EfCoreInteractionStore>();
builder.Services.AddSingleton<ISwipeDeckSource, InMemorySwipeDeckSource>();
builder.Services.AddScoped<IInteractionService, InteractionService>();
builder.Services.AddScoped<ISwipeDeckService, SwipeDeckService>();

builder.Services.AddTindarrPersistence(builder.Configuration, overrideDataDir: dataDirOverride);

var app = builder.Build();

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
