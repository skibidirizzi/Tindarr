using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tindarr.Contracts.Auth;
using Tindarr.Contracts.Playback;
using Xunit;

namespace Tindarr.IntegrationTests.Api;

/// <summary>
/// Integration tests for playback gateway (prepare token, URL generation) - "playback" area from issue 91.
/// </summary>
public sealed class PlaybackIntegrationTests : IClassFixture<TindarrWebApplicationFactory>
{
	private readonly TindarrWebApplicationFactory _factory;
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		PropertyNameCaseInsensitive = true
	};

	public PlaybackIntegrationTests(TindarrWebApplicationFactory factory)
	{
		_factory = factory;
	}

	[Fact]
	public async Task Prepare_returns_401_without_token()
	{
		var client = _factory.CreateClient();
		var request = new PrepareMoviePlaybackRequest("tmdb", "tmdb", 12345);

		var response = await client.PostAsJsonAsync("/api/v1/playback/movie/prepare", request, JsonOptions);

		Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
	}

	[Fact]
	public async Task Prepare_returns_200_with_token_and_returns_content_url_and_expiry()
	{
		var client = _factory.CreateClient();
		var userId = $"playback-user-{Guid.NewGuid():N}";
		var registerResponse = await client.PostAsJsonAsync("/api/v1/auth/register",
			new RegisterRequest(userId, "Playback Test User", "TestPassword1!"), JsonOptions);
		registerResponse.EnsureSuccessStatusCode();
		var auth = await registerResponse.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions);
		Assert.NotNull(auth);
		client.DefaultRequestHeaders.Authorization =
			new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", auth.AccessToken);

		var request = new PrepareMoviePlaybackRequest("tmdb", "tmdb", 12345);
		var response = await client.PostAsJsonAsync("/api/v1/playback/movie/prepare", request, JsonOptions);

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		var prepare = await response.Content.ReadFromJsonAsync<PreparePlaybackResponse>(JsonOptions);
		Assert.NotNull(prepare);
		Assert.False(string.IsNullOrEmpty(prepare.ContentUrl));
		Assert.True(prepare.ContentUrl.Contains("token=", StringComparison.Ordinal));
		Assert.True(prepare.ContentUrl.Contains("/api/v1/playback/", StringComparison.Ordinal));
		Assert.True(prepare.ExpiresAtUnixSeconds > 0);
	}
}
