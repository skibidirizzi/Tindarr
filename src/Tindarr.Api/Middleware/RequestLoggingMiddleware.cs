namespace Tindarr.Api.Middleware;

public sealed class RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
{
	public async Task InvokeAsync(HttpContext context)
	{
		var correlationId =
			context.Items.TryGetValue(CorrelationIdMiddleware.ItemKey, out var cidObj) ? cidObj?.ToString() :
			context.Request.Headers.TryGetValue(CorrelationIdMiddleware.HeaderName, out var cidHeader) ? cidHeader.ToString() :
			context.TraceIdentifier;

		var pathAndQuery = SensitiveRedaction.RedactPathAndQuery(context.Request.Path, context.Request.QueryString);

		var headers = context.Request.Headers
			.ToDictionary(kvp => kvp.Key, kvp => SensitiveRedaction.RedactHeader(kvp.Key, kvp.Value.ToString()));

		logger.LogInformation(
			"HTTP {Method} {Path} CorrelationId={CorrelationId} Headers={Headers}",
			context.Request.Method,
			pathAndQuery,
			correlationId,
			headers);

		await next(context);
	}
}
