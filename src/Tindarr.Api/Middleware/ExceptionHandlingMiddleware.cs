using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Tindarr.Api.Middleware;

public sealed class ExceptionHandlingMiddleware(
	RequestDelegate next,
	ILogger<ExceptionHandlingMiddleware> logger)
{
	public async Task InvokeAsync(HttpContext context)
	{
		try
		{
			await next(context);
		}
		catch (Exception ex)
		{
			var correlationId =
				context.Items.TryGetValue(CorrelationIdMiddleware.ItemKey, out var cidObj) ? cidObj?.ToString() :
				context.Request.Headers.TryGetValue(CorrelationIdMiddleware.HeaderName, out var cidHeader) ? cidHeader.ToString() :
				context.TraceIdentifier;

			logger.LogError(ex, "Unhandled exception. CorrelationId={CorrelationId}", correlationId);

			if (!context.Response.HasStarted)
			{
				var isInvalidOperation = ex is InvalidOperationException;
				context.Response.StatusCode = isInvalidOperation ? (int)HttpStatusCode.Conflict : (int)HttpStatusCode.InternalServerError;
				context.Response.ContentType = "application/problem+json";

				IHostEnvironment? hostEnvironment =
					context.RequestServices.GetService<IHostEnvironment>() ??
					context.RequestServices.GetService<IWebHostEnvironment>();

				var includeExceptionDetails = hostEnvironment is not null && hostEnvironment.IsDevelopment();
				var includeDetail = includeExceptionDetails || isInvalidOperation;
				if (includeDetail)
				{
					await context.Response.WriteAsJsonAsync(new
					{
						title = isInvalidOperation ? "Cannot complete restore" : "Unexpected error",
						status = context.Response.StatusCode,
						correlationId,
						exceptionType = includeExceptionDetails ? ex.GetType().FullName : null,
						detail = ex.Message,
						stackTrace = includeExceptionDetails ? ex.StackTrace : null
					});
				}
				else
				{
					await context.Response.WriteAsJsonAsync(new
					{
						title = "Unexpected error",
						status = context.Response.StatusCode,
						correlationId
					});
				}
			}
		}
	}
}
