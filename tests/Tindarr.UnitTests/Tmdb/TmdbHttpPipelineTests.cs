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

	[Fact]
	public async Task CachingAndRetryHandlers_DoNotCacheOrRetry_ForNonGetRequests()
	{
		var cache = new FakeCache();
		var tmdbOptions = Options.Create(new TmdbOptions
		{
			ApiKey = "api-key",
			DiscoverCacheSeconds = 60,
			DetailsCacheSeconds = 600
		});

		var inner = new CountingHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
		{
			Content = new StringContent("Service unavailable", Encoding.UTF8, "text/plain")
		});

		var retry = new TmdbRetryHandler(maxRetries: 3, delayProvider: static (_, _) => TimeSpan.Zero) { InnerHandler = inner };
		var caching = new TmdbCachingHandler(cache, tmdbOptions) { InnerHandler = retry };
		var client = new HttpClient(caching) { BaseAddress = new Uri("https://api.themoviedb.org/3/") };

		var r1 = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/movie/123/rating")
		{
			Content = new StringContent("{\"value\":8.0}", Encoding.UTF8, "application/json")
		});

		var r2 = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/movie/123/rating")
		{
			Content = new StringContent("{\"value\":8.0}", Encoding.UTF8, "application/json")
		});

		Assert.Equal(HttpStatusCode.ServiceUnavailable, r1.StatusCode);
		Assert.Equal(HttpStatusCode.ServiceUnavailable, r2.StatusCode);

		// Non-GET should not be retried or cached: one inner call per request.
		Assert.Equal(2, inner.CallCount);
	}

	[Fact]
	public async Task RetryHandler_GivesUpAfterMaxRetries_WhenAllAttemptsFail()
	{
		var inner = new CountingHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
		{
			Content = new StringContent("Internal server error", Encoding.UTF8, "text/plain")
		});

		var maxRetries = 3;
		var retry = new TmdbRetryHandler(maxRetries: maxRetries, delayProvider: static (_, _) => TimeSpan.Zero) { InnerHandler = inner };
		var client = new HttpClient(retry) { BaseAddress = new Uri("https://api.themoviedb.org/3/") };

		var resp = await client.GetAsync("/movie/123");

		Assert.Equal(HttpStatusCode.InternalServerError, resp.StatusCode);
		Assert.Equal(maxRetries + 1, inner.CallCount);
	}

	[Fact]
	public async Task RetryHandler_HonoursRetryAfterHeader_For429Responses()
	{
		var calls = 0;
		var inner = new CountingHandler(_ =>
		{
			calls++;
			if (calls == 1)
			{
				var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
				{
					Content = new StringContent("Too Many Requests", Encoding.UTF8, "text/plain")
				};
				response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMilliseconds(200));
				return response;
			}

			return new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new StringContent("ok", Encoding.UTF8, "text/plain")
			};
		});

		// Use default delay calculation (which should respect Retry-After).
		var retry = new TmdbRetryHandler(maxRetries: 2) { InnerHandler = inner };
		var client = new HttpClient(retry) { BaseAddress = new Uri("https://api.themoviedb.org/3/") };

		var sw = System.Diagnostics.Stopwatch.StartNew();
		var resp = await client.GetAsync("/movie/123");
		sw.Stop();

		Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
		Assert.Equal(2, inner.CallCount);
		Assert.True(sw.Elapsed >= TimeSpan.FromMilliseconds(150), $"Expected Retry-After delay to be honoured; elapsed was {sw.Elapsed}.");
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

