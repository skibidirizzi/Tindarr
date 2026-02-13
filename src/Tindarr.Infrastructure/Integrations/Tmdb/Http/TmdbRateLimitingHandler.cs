using Tindarr.Application.Abstractions.Caching;

namespace Tindarr.Infrastructure.Integrations.Tmdb.Http;

public sealed class TmdbRateLimitingHandler(ITmdbRateLimiter limiter) : DelegatingHandler
{
	public static readonly AsyncLocal<bool> BypassRateLimit = new();

	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		if (!BypassRateLimit.Value)
		{
			await limiter.WaitAsync(cancellationToken).ConfigureAwait(false);
		}
		return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
	}
}

