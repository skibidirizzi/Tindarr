using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using Tindarr.Application.Abstractions.Caching;
using Tindarr.Application.Options;
using Tindarr.Infrastructure.Integrations.Tmdb.Http;

namespace Tindarr.UnitTests.Tmdb;

public sealed class TmdbHttpPipelineTests
{
	[Fact]
	public async Task CachingHandler_StripsApiKeyAndCachesOkResponses()
	{
		var cache = new FakeCache();
		var tmdbOptions = Options.Create(new TmdbOptions
		{
			ApiKey = "not-used-by-handler",
			DiscoverCacheSeconds = 60,
			DetailsCacheSeconds = 600
		});

		var inner = new CountingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
		{
			Content = new StringContent("{\"ok\":true}", Encoding.UTF8, "application/json")
		});

		var caching = new TmdbCachingHandler(cache, tmdbOptions) { InnerHandler = inner };
		var client = new HttpClient(caching);

		var url1 = new Uri("https://api.themoviedb.org/3/movie/123?api_key=SECRET_A&language=en");
		var url2 = new Uri("https://api.themoviedb.org/3/movie/123?api_key=SECRET_B&language=en");

		var r1 = await client.GetAsync(url1);
		var b1 = await r1.Content.ReadAsStringAsync();

		var r2 = await client.GetAsync(url2);
		var b2 = await r2.Content.ReadAsStringAsync();

		Assert.Equal(1, inner.CallCount);
		Assert.Equal(b1, b2);
	}

	[Fact]
	public async Task RateLimitingHandler_WaitsOncePerRequest()
	{
		var limiter = new FakeLimiter();
		var inner = new CountingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
		{
			Content = new StringContent("ok", Encoding.UTF8, "text/plain")
		});

		var rateLimited = new TmdbRateLimitingHandler(limiter) { InnerHandler = inner };
		var client = new HttpClient(rateLimited);

		await client.GetAsync("https://api.themoviedb.org/3/movie/1");
		await client.GetAsync("https://api.themoviedb.org/3/movie/2");
		await client.GetAsync("https://api.themoviedb.org/3/movie/3");

		Assert.Equal(3, limiter.WaitCount);
		Assert.Equal(3, inner.CallCount);
	}

	[Fact]
	public async Task RetryHandler_RetriesTransientFailures()
	{
		var calls = 0;
		var inner = new CountingHandler(_ =>
		{
			calls++;
			return calls < 3
				? new HttpResponseMessage(HttpStatusCode.InternalServerError)
				: new HttpResponseMessage(HttpStatusCode.OK)
				{
					Content = new StringContent("ok", Encoding.UTF8, "text/plain")
				};
		});

		var retry = new TmdbRetryHandler(maxRetries: 3, delayProvider: static (_, _) => TimeSpan.Zero) { InnerHandler = inner };
		var client = new HttpClient(retry);

		var resp = await client.GetAsync("https://api.themoviedb.org/3/movie/1");

		Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
		Assert.Equal(3, inner.CallCount);
	}

	private sealed class FakeLimiter : ITmdbRateLimiter
	{
		public int WaitCount { get; private set; }

		public ValueTask WaitAsync(CancellationToken cancellationToken)
		{
			WaitCount++;
			return ValueTask.CompletedTask;
		}
	}

	private sealed class FakeCache : ITmdbCache
	{
		private readonly ConcurrentDictionary<string, object> _items = new();

		public ValueTask<T?> GetAsync<T>(string key, CancellationToken cancellationToken)
		{
			return ValueTask.FromResult(_items.TryGetValue(key, out var value) ? (T?)value : default);
		}

		public ValueTask SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken)
		{
			_items[key] = value!;
			return ValueTask.CompletedTask;
		}
	}

	private sealed class CountingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
	{
		public int CallCount { get; private set; }

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			CallCount++;
			return Task.FromResult(responder(request));
		}
	}
}

