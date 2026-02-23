using Microsoft.Extensions.Options;
using System.Diagnostics;
using Tindarr.Application.Options;

namespace Tindarr.Api.Middleware;

public sealed class RequestLoggingMiddleware(
	RequestDelegate next,
	ILogger<RequestLoggingMiddleware> logger,
	IOptions<LoggingOptions> loggingOptionsAccessor)
{
	private readonly LoggingOptions loggingOptions = loggingOptionsAccessor.Value;

	/// <summary>Path we do not log (avoids polluting the admin console output view with its own polling requests).</summary>
	private static readonly PathString ConsoleEndpoint = new("/api/v1/admin/console");

	public async Task InvokeAsync(HttpContext context)
	{
		var sw = Stopwatch.StartNew();
		var skipLog = context.Request.Path.StartsWithSegments(ConsoleEndpoint, StringComparison.OrdinalIgnoreCase);

		var correlationId =
			context.Items.TryGetValue(CorrelationIdMiddleware.ItemKey, out var cidObj) ? cidObj?.ToString() :
			context.Request.Headers.TryGetValue(CorrelationIdMiddleware.HeaderName, out var cidHeader) ? cidHeader.ToString() :
			context.TraceIdentifier;

		var pathAndQuery = SensitiveRedaction.RedactPathAndQuery(context.Request.Path, context.Request.QueryString);

		if (!skipLog)
		{
			// Only collect and log headers when enabled to reduce overhead/log volume.
			var headers = loggingOptions.LogRequestHeaders
				? context.Request.Headers.ToDictionary(
					kvp => kvp.Key,
					kvp => SensitiveRedaction.RedactHeader(kvp.Key, kvp.Value.ToString()))
				: null;

			logger.LogInformation(
				"HTTP {Method} {Path} CorrelationId={CorrelationId} Headers={Headers}",
				context.Request.Method,
				pathAndQuery,
				correlationId,
				headers);
		}

		await next(context);

		sw.Stop();

		if (!skipLog)
		{
			Dictionary<string, string?>? responseHeaders = null;
			if (loggingOptions.LogResponseHeaders)
			{
				responseHeaders = context.Response.Headers.ToDictionary(
					kvp => kvp.Key,
					kvp => (string?)SensitiveRedaction.RedactHeader(kvp.Key, kvp.Value.ToString()));
			}

			logger.LogInformation(
				"HTTP {Method} {Path} -> {StatusCode} ({ElapsedMs} ms) CorrelationId={CorrelationId} ContentType={ContentType} ResponseHeaders={ResponseHeaders}",
				context.Request.Method,
				pathAndQuery,
				context.Response.StatusCode,
				sw.ElapsedMilliseconds,
				correlationId,
				context.Response.ContentType,
				responseHeaders);
		}
	}
}
