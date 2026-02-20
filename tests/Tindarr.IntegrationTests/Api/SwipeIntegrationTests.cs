using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tindarr.Contracts.Auth;
using Tindarr.Contracts.Interactions;
using Xunit;

namespace Tindarr.IntegrationTests.Api;

public sealed class SwipeIntegrationTests : IClassFixture<TindarrWebApplicationFactory>
{
	private readonly TindarrWebApplicationFactory _factory;
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		PropertyNameCaseInsensitive = true,
		Converters = { new JsonStringEnumConverter() }
	};

	public SwipeIntegrationTests(TindarrWebApplicationFactory factory)
	{
		_factory = factory;
	}

	private static async Task<HttpClient> CreateAuthenticatedClientAsync(TindarrWebApplicationFactory factory)
	{
		var client = factory.CreateClient();
		var userId = $"swipe-user-{Guid.NewGuid():N}";
		var response = await client.PostAsJsonAsync("/api/v1/auth/register",
			new RegisterRequest(userId, "Swipe Test User", "TestPassword1!"), JsonOptions);
		response.EnsureSuccessStatusCode();
		var auth = await response.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions);
		Assert.NotNull(auth);
		client.DefaultRequestHeaders.Authorization =
			new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", auth.AccessToken);
		return client;
	}

	[Fact]
	public async Task Create_swipe_tmdb_returns_200()
	{
		var client = await CreateAuthenticatedClientAsync(_factory);
		var request = new SwipeRequest(12345, SwipeActionDto.Like, "tmdb", "tmdb");

		var response = await client.PostAsJsonAsync("/api/v1/interactions", request, JsonOptions);

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		var swipe = await response.Content.ReadFromJsonAsync<SwipeResponse>(JsonOptions);
		Assert.NotNull(swipe);
		Assert.Equal(12345, swipe.TmdbId);
		Assert.Equal(SwipeActionDto.Like, swipe.Action);
	}

	[Fact]
	public async Task List_interactions_returns_200_and_includes_created_swipe()
	{
		var client = await CreateAuthenticatedClientAsync(_factory);
		var request = new SwipeRequest(99999, SwipeActionDto.Nope, "tmdb", "tmdb");
		await client.PostAsJsonAsync("/api/v1/interactions", request, JsonOptions);

		var response = await client.GetAsync("/api/v1/interactions?serviceType=tmdb&serverId=tmdb&limit=50");

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		var list = await response.Content.ReadFromJsonAsync<InteractionListResponse>(JsonOptions);
		Assert.NotNull(list);
		Assert.Equal("Tmdb", list.ServiceType); // API returns enum ToString() (PascalCase)
		Assert.Equal("tmdb", list.ServerId);
		var found = list.Items.FirstOrDefault(i => i.TmdbId == 99999 && i.Action == SwipeActionDto.Nope);
		Assert.NotNull(found);
	}
}
