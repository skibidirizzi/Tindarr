using System.Diagnostics;

namespace Tindarr.Api.Middleware;

public sealed class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
	public const string HeaderName = "X-Correlation-Id";
	public const string ItemKey = "CorrelationId";

	private const int MaxCorrelationIdLength = 128;

	public async Task InvokeAsync(HttpContext context)
	{
		var correlationId = GetOrCreateCorrelationId(context);

		context.Items[ItemKey] = correlationId;
		context.TraceIdentifier = correlationId;

		Activity.Current?.SetTag("correlation_id", correlationId);

		context.Response.OnStarting(() =>
		{
			context.Response.Headers[HeaderName] = correlationId;
			return Task.CompletedTask;
		});

		using var _ = logger.BeginScope(new Dictionary<string, object?>
		{
			["CorrelationId"] = correlationId
		});

		await next(context);
	}

	private static string GetOrCreateCorrelationId(HttpContext context)
	{
		if (context.Request.Headers.TryGetValue(HeaderName, out var headerValue))
		{
			var candidate = headerValue.ToString().Trim();
			if (IsValidCorrelationId(candidate))
			{
				return candidate;
			}
		}

		var generated = Guid.NewGuid().ToString("N");
		context.Request.Headers[HeaderName] = generated;
		return generated;
	}

	private static bool IsValidCorrelationId(string value)
	{
		if (string.IsNullOrWhiteSpace(value) || value.Length > MaxCorrelationIdLength)
		{
			return false;
		}

		// Conservative allowlist: avoids log injection and weird header values.
		foreach (var ch in value)
		{
			if (char.IsLetterOrDigit(ch))
			{
				continue;
			}

			if (ch is '-' or '_' or '.' or ':' or '/')
			{
				continue;
			}

			return false;
		}

		return true;
	}
}
