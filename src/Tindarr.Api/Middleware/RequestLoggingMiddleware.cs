using Microsoft.Extensions.Options;
using Tindarr.Application.Options;

namespace Tindarr.Api.Middleware;

public sealed class RequestLoggingMiddleware(
	RequestDelegate next,
	ILogger<RequestLoggingMiddleware> logger,
	IOptions<LoggingOptions> loggingOptionsAccessor)
{
	private readonly LoggingOptions loggingOptions = loggingOptionsAccessor.Value;

	public async Task InvokeAsync(HttpContext context)
	{
		var correlationId =
			context.Items.TryGetValue(CorrelationIdMiddleware.ItemKey, out var cidObj) ? cidObj?.ToString() :
			context.Request.Headers.TryGetValue(CorrelationIdMiddleware.HeaderName, out var cidHeader) ? cidHeader.ToString() :
			context.TraceIdentifier;

		var pathAndQuery = SensitiveRedaction.RedactPathAndQuery(context.Request.Path, context.Request.QueryString);

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

		await next(context);
	}
}
