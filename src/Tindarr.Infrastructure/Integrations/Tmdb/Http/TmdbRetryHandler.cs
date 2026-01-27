using System.Net;

namespace Tindarr.Infrastructure.Integrations.Tmdb.Http;

public sealed class TmdbRetryHandler : DelegatingHandler
{
	private readonly int _maxRetries;
	private readonly Func<int, HttpResponseMessage?, TimeSpan> _delayProvider;

	public TmdbRetryHandler(int maxRetries = 3, Func<int, HttpResponseMessage?, TimeSpan>? delayProvider = null)
	{
		_maxRetries = Math.Clamp(maxRetries, 0, 10);
		_delayProvider = delayProvider ?? ComputeDelay;
	}

	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		// Only retry idempotent requests. TMDB calls here are GETs.
		if (request.Method != HttpMethod.Get && request.Method != HttpMethod.Head)
		{
			return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
		}

		for (var attempt = 0; ; attempt++)
		{
			try
			{
				using var attemptRequest = attempt == 0 ? null : Clone(request);
				var response = await base.SendAsync(attempt == 0 ? request : attemptRequest!, cancellationToken).ConfigureAwait(false);
				if (!IsRetryable(response.StatusCode) || attempt >= _maxRetries)
				{
					return response;
				}

				var delay = _delayProvider(attempt, response);
				if (delay > TimeSpan.Zero)
				{
					await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
				}
			}
			catch (HttpRequestException) when (attempt < _maxRetries)
			{
				await Task.Delay(_delayProvider(attempt, null), cancellationToken).ConfigureAwait(false);
			}
			catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < _maxRetries)
			{
				await Task.Delay(_delayProvider(attempt, null), cancellationToken).ConfigureAwait(false);
			}
		}
	}

	private static HttpRequestMessage Clone(HttpRequestMessage request)
	{
		var clone = new HttpRequestMessage(request.Method, request.RequestUri);

		foreach (var header in request.Headers)
		{
			clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
		}

		foreach (var opt in request.Options)
		{
			clone.Options.Set(new HttpRequestOptionsKey<object?>(opt.Key), opt.Value);
		}

		clone.Version = request.Version;
		clone.VersionPolicy = request.VersionPolicy;

		// No content cloning needed for GET/HEAD (the only allowed retry methods here).
		return clone;
	}

	private static bool IsRetryable(HttpStatusCode statusCode)
	{
		return statusCode is HttpStatusCode.RequestTimeout
			or HttpStatusCode.TooManyRequests
			or >= HttpStatusCode.InternalServerError;
	}

	private static TimeSpan ComputeDelay(int attempt, HttpResponseMessage? response)
	{
		// Prefer server-driven Retry-After for 429.
		if (response?.StatusCode == HttpStatusCode.TooManyRequests
			&& response.Headers.RetryAfter is not null)
		{
			if (response.Headers.RetryAfter.Delta is { } delta)
			{
				return Clamp(delta);
			}

			if (response.Headers.RetryAfter.Date is { } date)
			{
				var until = date - DateTimeOffset.UtcNow;
				return Clamp(until);
			}
		}

		// Exponential backoff with jitter.
		var baseMs = 250 * Math.Pow(2, attempt); // 250, 500, 1000, 2000...
		var jitter = Random.Shared.NextDouble() * 200; // 0-200ms
		return Clamp(TimeSpan.FromMilliseconds(baseMs + jitter));
	}

	private static TimeSpan Clamp(TimeSpan delay)
	{
		if (delay < TimeSpan.Zero) return TimeSpan.Zero;
		if (delay > TimeSpan.FromSeconds(10)) return TimeSpan.FromSeconds(10);
		return delay;
	}
}

