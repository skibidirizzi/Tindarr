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
			PreferredRegions: ["US"],
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
	public async Task GetMovieDetailsAsync_MapsNormalizedDto()
	{
		var handler = new StubHandler(req =>
		{
			Assert.Contains("movie/123?", req.RequestUri!.ToString());

			return new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new StringContent(
					"""
					{"id":123,"title":"M","original_title":"M","overview":"O","poster_path":"/p.jpg","backdrop_path":"/b.jpg","release_date":"1999-03-31","vote_average":8.2,"vote_count":1000,"original_language":"en","runtime":136,"genres":[{"id":1,"name":"Action"},{"id":2,"name":"Sci-Fi"}]}
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
		Assert.Equal("https://image.tmdb.org/t/p/w500/p.jpg", details.PosterUrl);
		Assert.Equal("https://image.tmdb.org/t/p/w780/b.jpg", details.BackdropUrl);
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

