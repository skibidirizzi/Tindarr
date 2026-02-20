using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tindarr.Contracts.Auth;
using Tindarr.Contracts.Common;
using Xunit;

namespace Tindarr.IntegrationTests.Api;

/// <summary>
/// Integration tests for configured scopes (Plex/Jellyfin/Emby/TMDB) - "integrations" area from issue 91.
/// </summary>
public sealed class ScopesIntegrationTests : IClassFixture<TindarrWebApplicationFactory>
{
	private readonly TindarrWebApplicationFactory _factory;
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		PropertyNameCaseInsensitive = true
	};

	public ScopesIntegrationTests(TindarrWebApplicationFactory factory)
	{
		_factory = factory;
	}

	[Fact]
	public async Task Scopes_returns_401_without_token()
	{
		var client = _factory.CreateClient();

		var response = await client.GetAsync("/api/v1/scopes");

		Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
	}

	[Fact]
	public async Task Scopes_returns_200_with_token_and_includes_tmdb()
	{
		var client = _factory.CreateClient();
		var userId = $"scopes-user-{Guid.NewGuid():N}";
		var registerResponse = await client.PostAsJsonAsync("/api/v1/auth/register",
			new RegisterRequest(userId, "Scopes Test User", "TestPassword1!"), JsonOptions);
		registerResponse.EnsureSuccessStatusCode();
		var auth = await registerResponse.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions);
		Assert.NotNull(auth);
		client.DefaultRequestHeaders.Authorization =
			new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", auth.AccessToken);

		var response = await client.GetAsync("/api/v1/scopes");

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		var scopes = await response.Content.ReadFromJsonAsync<List<ServiceScopeOptionDto>>(JsonOptions);
		Assert.NotNull(scopes);
		Assert.True(scopes.Count >= 1);
		var tmdb = scopes.FirstOrDefault(s =>
			string.Equals(s.ServiceType, "tmdb", StringComparison.OrdinalIgnoreCase) && s.ServerId == "tmdb");
		Assert.NotNull(tmdb);
	}
}
