using Tindarr.Application.Abstractions.Caching;

namespace Tindarr.Infrastructure.Integrations.Tmdb.Http;

public sealed class TmdbRateLimitingHandler(ITmdbRateLimiter limiter) : DelegatingHandler
{
	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		await limiter.WaitAsync(cancellationToken).ConfigureAwait(false);
		return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
	}
}

