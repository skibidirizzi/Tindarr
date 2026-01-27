using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Options;
using Tindarr.Application.Abstractions.Caching;
using Tindarr.Application.Options;

namespace Tindarr.Infrastructure.Integrations.Tmdb.Http;

public sealed class TmdbCachingHandler(ITmdbCache cache, IOptions<TmdbOptions> options) : DelegatingHandler
{
	private readonly TmdbOptions _tmdb = options.Value;

	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		if (request.Method != HttpMethod.Get || request.RequestUri is null)
		{
			return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
		}

		var normalizedUri = NormalizeUri(request.RequestUri!);
		var cacheKey = BuildKey(normalizedUri);
		var cached = await cache.GetAsync<TmdbCachedResponse>(cacheKey, cancellationToken).ConfigureAwait(false);
		if (cached is not null)
		{
			var msg = new HttpResponseMessage((HttpStatusCode)cached.StatusCode)
			{
				RequestMessage = request,
				Content = new StringContent(cached.Body, Encoding.UTF8)
			};

			if (!string.IsNullOrWhiteSpace(cached.ContentType))
			{
				msg.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(cached.ContentType);
			}

			return msg;
		}

		var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
		if (response.StatusCode != HttpStatusCode.OK)
		{
			return response;
		}

		// Cache only small-ish JSON payloads. If callers need streaming, do not use this handler.
		var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
		var contentType = response.Content.Headers.ContentType?.ToString();

		var toCache = new TmdbCachedResponse((int)response.StatusCode, contentType, body);
		var ttl = ResolveTtl(normalizedUri);
		await cache.SetAsync(cacheKey, toCache, ttl, cancellationToken).ConfigureAwait(false);

		// Return a fresh message; the original content has been buffered/consumed.
		var cloned = new HttpResponseMessage(response.StatusCode)
		{
			RequestMessage = request,
			Content = new StringContent(body, Encoding.UTF8)
		};

		if (!string.IsNullOrWhiteSpace(contentType))
		{
			cloned.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
		}

		return cloned;
	}

	private static string BuildKey(Uri uri)
	{
		// We include query string (preferences etc) but remove secret params like api_key.
		var withoutSecrets = StripQueryKeys(uri, keysToRemove: ["api_key", "apikey", "token", "access_token", "refresh_token"]);
		return "tmdb:http:" + withoutSecrets;
	}

	private TimeSpan ResolveTtl(Uri uri)
	{
		var path = uri.AbsolutePath.Trim('/').ToLowerInvariant();
		if (path.StartsWith("3/movie/", StringComparison.Ordinal) || path.StartsWith("movie/", StringComparison.Ordinal))
		{
			return TimeSpan.FromSeconds(Math.Max(0, _tmdb.DetailsCacheSeconds));
		}

		if (path.StartsWith("3/discover/", StringComparison.Ordinal) || path.StartsWith("discover/", StringComparison.Ordinal))
		{
			return TimeSpan.FromSeconds(Math.Max(0, _tmdb.DiscoverCacheSeconds));
		}

		// Default.
		return TimeSpan.FromSeconds(Math.Max(0, _tmdb.DetailsCacheSeconds));
	}

	private Uri NormalizeUri(Uri uri)
	{
		if (uri.IsAbsoluteUri)
		{
			return uri;
		}

		// Build a stable absolute URI for parsing/caching.
		// Host is irrelevant; only path/query are used.
		return new Uri("https://tmdb.local/" + uri.ToString().TrimStart('/'), UriKind.Absolute);
	}

	private static string StripQueryKeys(Uri uri, IReadOnlyList<string> keysToRemove)
	{
		if (string.IsNullOrWhiteSpace(uri.Query))
		{
			return uri.ToString();
		}

		var query = uri.Query.TrimStart('?');
		var parts = query.Split('&', StringSplitOptions.RemoveEmptyEntries);

		var kept = new List<string>(parts.Length);
		foreach (var part in parts)
		{
			var idx = part.IndexOf('=');
			var key = idx < 0 ? part : part[..idx];

			var isRemove = keysToRemove.Any(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
			if (!isRemove)
			{
				kept.Add(part);
			}
		}

		var builder = new UriBuilder(uri)
		{
			Query = kept.Count == 0 ? "" : string.Join("&", kept)
		};

		return builder.Uri.ToString();
	}
}

