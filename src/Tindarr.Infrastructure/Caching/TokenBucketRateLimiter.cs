using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;
using Tindarr.Application.Abstractions.Caching;
using Tindarr.Application.Options;

namespace Tindarr.Infrastructure.Caching;

public sealed class TokenBucketRateLimiter : ITmdbRateLimiter, IDisposable
{
	private readonly System.Threading.RateLimiting.TokenBucketRateLimiter _limiter;

	public TokenBucketRateLimiter(IOptions<TmdbOptions> options)
	{
		var tmdb = options.Value;

		// TMDB's published limits vary by account type. Keep this conservative by default.
		var perSecond = Math.Clamp(tmdb.RequestsPerSecond, 1, 50);

		_limiter = new System.Threading.RateLimiting.TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
		{
			TokenLimit = perSecond,
			TokensPerPeriod = perSecond,
			ReplenishmentPeriod = TimeSpan.FromSeconds(1),
			AutoReplenishment = true,
			QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
			QueueLimit = perSecond * 10
		});
	}

	public async ValueTask WaitAsync(CancellationToken cancellationToken)
	{
		using var lease = await _limiter.AcquireAsync(permitCount: 1, cancellationToken).ConfigureAwait(false);
		// AcquireAsync waits when queued; if it still fails, proceed without throwing.
	}

	public void Dispose()
	{
		_limiter.Dispose();
	}
}

