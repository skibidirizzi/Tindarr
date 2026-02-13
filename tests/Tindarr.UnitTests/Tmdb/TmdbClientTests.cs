using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tindarr.Application.Abstractions.Persistence;
using Tindarr.Application.Options;
using Tindarr.Infrastructure.Integrations.Tmdb;

namespace Tindarr.UnitTests.Tmdb;

public sealed class TmdbClientTests
{
	private static readonly UserPreferencesRecord DummyPreferences = new(
		IncludeAdult: false,
		MinReleaseYear: null,
		MaxReleaseYear: null,
		MinRating: null,
		MaxRating: null,
		PreferredGenres: [],
		ExcludedGenres: [],
		PreferredOriginalLanguages: [],
		ExcludedOriginalLanguages: [],
		PreferredRegions: [],
		ExcludedRegions: [],
		SortBy: "popularity.desc",
		UpdatedAtUtc: DateTimeOffset.UtcNow);

	private static TmdbClient CreateClient(HttpMessageHandler handler, string? apiKey = "KEY", string? readAccessToken = "")
	{
		var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.themoviedb.org/3/") };
		return new TmdbClient(http, Options.Create(new TmdbOptions
		{
			ApiKey = apiKey ?? "",
			ReadAccessToken = readAccessToken ?? "",
			ImageBaseUrl = "https://image.tmdb.org/t/p/",
			PosterSize = "w500",
			BackdropSize = "w780"
		}), NullLogger<TmdbClient>.Instance);
	}

	[Fact]
	public async Task DiscoverAsync_IncludesPreferencesInQuery_AndMapsSwipeCards()
	{
		Uri? observedUri = null;

		var handler = new StubHandler(req =>
		{
			observedUri = req.RequestUri;
			return new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new StringContent(
					"""
					{"page":1,"total_pages":1,"results":[{"id":42,"title":"Test","original_title":"Test","overview":"Hello","poster_path":"/p.jpg","backdrop_path":"/b.jpg","release_date":"2001-02-03","vote_average":8.1}]}
					""",
					Encoding.UTF8,
					"application/json")
			};
		});

		var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.themoviedb.org/3/") };
		var tmdb = new TmdbClient(http, Options.Create(new TmdbOptions
		{
			ApiKey = "KEY",
			ImageBaseUrl = "https://image.tmdb.org/t/p/",
			PosterSize = "w500",
			BackdropSize = "w780"
		}), NullLogger<TmdbClient>.Instance);

		var prefs = new UserPreferencesRecord(
			IncludeAdult: true,
			MinReleaseYear: 2000,
			MaxReleaseYear: 2005,
			MinRating: 7.5,
			MaxRating: 9.5,
			PreferredGenres: [1, 2],
			ExcludedGenres: [3],
			PreferredOriginalLanguages: ["en"],
			ExcludedOriginalLanguages: [],
			PreferredRegions: ["US"],
			ExcludedRegions: [],
			SortBy: "vote_average.desc",
			UpdatedAtUtc: DateTimeOffset.UtcNow);

		var results = await tmdb.DiscoverAsync(prefs, page: 1, limit: 1, CancellationToken.None);

		Assert.NotNull(observedUri);
		Assert.Contains("discover/movie", observedUri!.ToString());

		var query = ParseQuery(observedUri);
		Assert.Equal("KEY", query["api_key"]);
		Assert.Equal("true", query["include_adult"]);
		Assert.Equal("vote_average.desc", query["sort_by"]);
		Assert.Equal("2000-01-01", query["primary_release_date.gte"]);
		Assert.Equal("2005-12-31", query["primary_release_date.lte"]);
		Assert.Equal("7.5", query["vote_average.gte"]);
		Assert.Equal("9.5", query["vote_average.lte"]);
		Assert.Equal("1|2", query["with_genres"]);
		Assert.Equal("3", query["without_genres"]);
		Assert.Equal("en", query["with_original_language"]);
		Assert.Equal("US", query["region"]);

		Assert.Single(results);
		Assert.Equal(42, results[0].TmdbId);
		Assert.Equal("Test", results[0].Title);
		Assert.Equal("Hello", results[0].Overview);
		Assert.Equal("https://image.tmdb.org/t/p/w500/p.jpg", results[0].PosterUrl);
		Assert.Equal("https://image.tmdb.org/t/p/w780/b.jpg", results[0].BackdropUrl);
		Assert.Equal(2001, results[0].ReleaseYear);
		Assert.Equal(8.1, results[0].Rating);
	}

	[Fact]
	public async Task DiscoverAsync_FiltersExcludedOriginalLanguages()
	{
		var handler = new StubHandler(_ =>
		{
			return new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new StringContent(
					"""
					{"page":1,"total_pages":1,"results":[
					  {"id":1,"title":"A","original_title":"A","overview":"","poster_path":null,"backdrop_path":null,"release_date":"2001-01-01","original_language":"en","vote_average":5.0},
					  {"id":2,"title":"B","original_title":"B","overview":"","poster_path":null,"backdrop_path":null,"release_date":"2002-01-01","original_language":"ja","vote_average":6.0}
					]}
					""",
					Encoding.UTF8,
					"application/json")
			};
		});

		var tmdb = CreateClient(handler);
		var prefs = DummyPreferences with { ExcludedOriginalLanguages = ["en"] };

		var results = await tmdb.DiscoverAsync(prefs, page: 1, limit: 10, CancellationToken.None);

		Assert.Single(results);
		Assert.Equal(2, results[0].TmdbId);
	}

	[Fact]
	public async Task DiscoverAsync_WhenHasNoCredentials_DoesNotCallHttpClient_AndReturnsEmpty()
	{
		var handler = new StubHandler(_ =>
		{
			throw new Xunit.Sdk.XunitException("HTTP should not be called when HasCredentials is false.");
		});

		var tmdb = CreateClient(handler, apiKey: "", readAccessToken: "");
		var results = await tmdb.DiscoverAsync(DummyPreferences, page: 1, limit: 10, CancellationToken.None);

		Assert.NotNull(results);
		Assert.Empty(results);
	}

	[Fact]
	public async Task GetMovieDetailsAsync_WhenHasNoCredentials_DoesNotCallHttpClient_AndReturnsNull()
	{
		var handler = new StubHandler(_ =>
		{
			throw new Xunit.Sdk.XunitException("HTTP should not be called when HasCredentials is false.");
		});

		var tmdb = CreateClient(handler, apiKey: "", readAccessToken: "");
		var details = await tmdb.GetMovieDetailsAsync(123, CancellationToken.None);
		Assert.Null(details);
	}

	[Theory]
	[InlineData(HttpStatusCode.TooManyRequests)]
	[InlineData(HttpStatusCode.InternalServerError)]
	public async Task DiscoverAsync_WhenNonSuccessNonNotFoundStatus_ReturnsEmptyAndDoesNotThrow(HttpStatusCode statusCode)
	{
		var handler = new StubHandler(_ => new HttpResponseMessage(statusCode)
		{
			Content = new StringContent(string.Empty, Encoding.UTF8, "application/json")
		});

		var tmdb = CreateClient(handler);
		var results = await tmdb.DiscoverAsync(DummyPreferences, page: 1, limit: 10, CancellationToken.None);

		Assert.NotNull(results);
		Assert.Empty(results);
	}

	[Theory]
	[InlineData(HttpStatusCode.TooManyRequests)]
	[InlineData(HttpStatusCode.InternalServerError)]
	public async Task GetMovieDetailsAsync_WhenNonSuccessNonNotFoundStatus_ReturnsNullAndDoesNotThrow(HttpStatusCode statusCode)
	{
		var handler = new StubHandler(_ => new HttpResponseMessage(statusCode)
		{
			Content = new StringContent(string.Empty, Encoding.UTF8, "application/json")
		});

		var tmdb = CreateClient(handler);
		var details = await tmdb.GetMovieDetailsAsync(123, CancellationToken.None);
		Assert.Null(details);
	}

	[Fact]
	public async Task DiscoverAsync_WhenResponseJsonIsMalformed_ReturnsEmptyAndDoesNotThrow()
	{
		var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
		{
			Content = new StringContent("this is not valid json", Encoding.UTF8, "application/json")
		});

		var tmdb = CreateClient(handler);
		var results = await tmdb.DiscoverAsync(DummyPreferences, page: 1, limit: 10, CancellationToken.None);

		Assert.NotNull(results);
		Assert.Empty(results);
	}

	[Fact]
	public async Task GetMovieDetailsAsync_WhenResponseJsonIsMalformed_ReturnsNullAndDoesNotThrow()
	{
		var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
		{
			Content = new StringContent("{ invalid json", Encoding.UTF8, "application/json")
		});

		var tmdb = CreateClient(handler);
		var details = await tmdb.GetMovieDetailsAsync(123, CancellationToken.None);
		Assert.Null(details);
	}

	[Fact]
	public async Task DiscoverAsync_Paginates_ClampsPageAndLimit_AndStopsAtTotalPages()
	{
		var observedPages = new List<int>();

		var handler = new StubHandler(req =>
		{
			var query = ParseQuery(req.RequestUri!);
			var page = int.Parse(query["page"]);
			observedPages.Add(page);

			// Simulate 3 pages, 1 result per page.
			var json = $$"""
			{"page":{{page}},"total_pages":3,"results":[{"id":{{page}},"title":"Movie {{page}}","original_title":"Movie {{page}}","overview":"","poster_path":"/p.jpg","backdrop_path":"/b.jpg","release_date":"2001-01-01","vote_average":8.0}]}
			""";

			return new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new StringContent(json, Encoding.UTF8, "application/json")
			};
		});

		var tmdb = CreateClient(handler);
		var results = await tmdb.DiscoverAsync(DummyPreferences, page: -5, limit: 10, CancellationToken.None);

		Assert.Equal([1, 2, 3], observedPages);
		Assert.Equal(3, results.Count);
		Assert.Equal([1, 2, 3], results.Select(r => r.TmdbId).ToArray());
	}

	[Fact]
	public async Task GetMovieDetailsAsync_MapsNormalizedDto()
	{
		var handler = new StubHandler(req =>
		{
			Assert.Contains("movie/123?", req.RequestUri!.ToString());
			Assert.Contains("append_to_response=release_dates", req.RequestUri!.ToString());

			return new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new StringContent(
					"""
					{"id":123,"title":"M","original_title":"M","overview":"O","poster_path":"/p.jpg","backdrop_path":"/b.jpg","release_date":"1999-03-31","vote_average":8.2,"vote_count":1000,"original_language":"en","runtime":136,"genres":[{"id":1,"name":"Action"},{"id":2,"name":"Sci-Fi"}],"release_dates":{"results":[{"iso_3166_1":"US","release_dates":[{"certification":"PG-13","type":3,"release_date":"1999-03-31T00:00:00.000Z"}]}]}}
					""",
					Encoding.UTF8,
					"application/json")
			};
		});

		var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.themoviedb.org/3/") };
		var tmdb = new TmdbClient(http, Options.Create(new TmdbOptions
		{
			ApiKey = "KEY",
			ImageBaseUrl = "https://image.tmdb.org/t/p/",
			PosterSize = "w500",
			BackdropSize = "w780"
		}), NullLogger<TmdbClient>.Instance);

		var details = await tmdb.GetMovieDetailsAsync(123, CancellationToken.None);

		Assert.NotNull(details);
		Assert.Equal(123, details!.TmdbId);
		Assert.Equal("M", details.Title);
		Assert.Equal("O", details.Overview);
		Assert.Equal(1999, details.ReleaseYear);
		Assert.Equal("1999-03-31", details.ReleaseDate);
		Assert.Equal(8.2, details.Rating);
		Assert.Equal(1000, details.VoteCount);
		Assert.Equal("en", details.OriginalLanguage);
		Assert.Equal(136, details.RuntimeMinutes);
		Assert.Equal(["Action", "Sci-Fi"], details.Genres);
		Assert.Equal("PG-13", details.MpaaRating);
		Assert.Equal("https://image.tmdb.org/t/p/w500/p.jpg", details.PosterUrl);
		Assert.Equal("https://image.tmdb.org/t/p/w780/b.jpg", details.BackdropUrl);
	}

	[Fact]
	public async Task GetMovieDetailsAsync_ExtractsMpaaRating_PrefersTheatrical_AndSkipsEmptyCertifications()
	{
		var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
		{
			Content = new StringContent(
				"""
				{
				  "id": 123,
				  "title": "M",
				  "poster_path": "/p.jpg",
				  "backdrop_path": "/b.jpg",
				  "release_date": "1999-03-31",
				  "vote_average": 8.2,
				  "release_dates": {
				    "results": [
				      {
				        "iso_3166_1": "US",
				        "release_dates": [
				          { "certification": "  ", "type": 3, "release_date": "1999-03-31T00:00:00.000Z" },
				          { "certification": "PG-13", "type": 4, "release_date": "1999-04-01T00:00:00.000Z" },
				          { "certification": "R", "type": 3, "release_date": "1999-04-02T00:00:00.000Z" }
				        ]
				      }
				    ]
				  }
				}
				""",
				Encoding.UTF8,
				"application/json")
		});

		var tmdb = CreateClient(handler);
		var details = await tmdb.GetMovieDetailsAsync(123, CancellationToken.None);

		Assert.NotNull(details);
		Assert.Equal("R", details!.MpaaRating);
	}

	[Fact]
	public async Task DiscoverAsync_BuildImageUrl_NormalizesBaseSizeAndPath()
	{
		var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
		{
			Content = new StringContent(
				"""
				{"page":1,"total_pages":1,"results":[{"id":42,"title":"Test","original_title":"Test","overview":"","poster_path":"p.jpg","backdrop_path":"b.jpg","release_date":"2001-02-03","vote_average":8.1}]}
				""",
				Encoding.UTF8,
				"application/json")
		});

		var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.themoviedb.org/3/") };
		var tmdb = new TmdbClient(http, Options.Create(new TmdbOptions
		{
			ApiKey = "KEY",
			ImageBaseUrl = "https://image.tmdb.org/t/p///",
			PosterSize = "/w500/",
			BackdropSize = "/w780/"
		}), NullLogger<TmdbClient>.Instance);

		var results = await tmdb.DiscoverAsync(DummyPreferences, page: 1, limit: 1, CancellationToken.None);
		Assert.Single(results);
		Assert.Equal("https://image.tmdb.org/t/p/w500/p.jpg", results[0].PosterUrl);
		Assert.Equal("https://image.tmdb.org/t/p/w780/b.jpg", results[0].BackdropUrl);
	}

	private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			return Task.FromResult(responder(request));
		}
	}

	private static Dictionary<string, string> ParseQuery(Uri uri)
	{
		var dict = new Dictionary<string, string>(StringComparer.Ordinal);
		var query = uri.Query.TrimStart('?');
		if (string.IsNullOrWhiteSpace(query))
		{
			return dict;
		}

		foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
		{
			var idx = part.IndexOf('=');
			if (idx <= 0)
			{
				continue;
			}

			var k = Uri.UnescapeDataString(part[..idx]);
			var v = Uri.UnescapeDataString(part[(idx + 1)..]);
			dict[k] = v;
		}

		return dict;
	}
}

