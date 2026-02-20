using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tindarr.Application.Abstractions.Security;
using Tindarr.Contracts.Auth;
using Xunit;

namespace Tindarr.IntegrationTests.Api;

public sealed class AuthIntegrationTests : IClassFixture<TindarrWebApplicationFactory>
{
	private readonly TindarrWebApplicationFactory _factory;
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		PropertyNameCaseInsensitive = true
	};

	public AuthIntegrationTests(TindarrWebApplicationFactory factory)
	{
		_factory = factory;
	}

	[Fact]
	public async Task Register_returns_200_and_token()
	{
		var client = _factory.CreateClient();
		var userId = $"test-user-{Guid.NewGuid():N}";
		var request = new RegisterRequest(userId, "Test User", "TestPassword1!");

		var response = await client.PostAsJsonAsync("/api/v1/auth/register", request, JsonOptions);

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		var auth = await response.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions);
		Assert.NotNull(auth);
		Assert.NotEmpty(auth.AccessToken);
		Assert.Equal(userId, auth.UserId);
		Assert.Equal("Test User", auth.DisplayName);
		Assert.True(auth.Roles.Count > 0);
	}

	[Fact]
	public async Task Login_after_register_returns_200_and_token()
	{
		var client = _factory.CreateClient();
		var userId = $"test-user-{Guid.NewGuid():N}";
		var password = "TestPassword1!";
		await client.PostAsJsonAsync("/api/v1/auth/register",
			new RegisterRequest(userId, "Test User", password), JsonOptions);

		var response = await client.PostAsJsonAsync("/api/v1/auth/login",
			new LoginRequest(userId, password), JsonOptions);

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		var auth = await response.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions);
		Assert.NotNull(auth);
		Assert.NotEmpty(auth.AccessToken);
		Assert.Equal(userId, auth.UserId);
	}

	[Fact]
	public async Task Me_returns_401_without_token()
	{
		var client = _factory.CreateClient();

		var response = await client.GetAsync("/api/v1/auth/me");

		Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
	}

	[Fact]
	public async Task Me_returns_200_with_valid_token()
	{
		var client = _factory.CreateClient();
		var userId = $"test-user-{Guid.NewGuid():N}";
		var registerResponse = await client.PostAsJsonAsync("/api/v1/auth/register",
			new RegisterRequest(userId, "Me Test User", "TestPassword1!"), JsonOptions);
		registerResponse.EnsureSuccessStatusCode();
		var auth = await registerResponse.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions);
		Assert.NotNull(auth);
		client.DefaultRequestHeaders.Authorization =
			new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", auth.AccessToken);

		var response = await client.GetAsync("/api/v1/auth/me");

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		var me = await response.Content.ReadFromJsonAsync<MeResponse>(JsonOptions);
		Assert.NotNull(me);
		Assert.Equal(userId, me.UserId);
		Assert.Equal("Me Test User", me.DisplayName);
	}

	[Fact]
	public async Task Me_returns_401_when_token_valid_but_user_no_longer_exists()
	{
		var tokenService = _factory.Services.GetRequiredService<ITokenService>();
		var tokenResult = tokenService.IssueAccessToken("nonexistent-user-" + Guid.NewGuid().ToString("N"), ["User"]);

		var client = _factory.CreateClient();
		client.DefaultRequestHeaders.Authorization =
			new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokenResult.AccessToken);

		var response = await client.GetAsync("/api/v1/auth/me");

		Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
	}

	[Fact]
	public async Task Me_returns_400_when_token_has_invalid_user_id()
	{
		var tokenService = _factory.Services.GetRequiredService<ITokenService>();
		var tokenResult = tokenService.IssueAccessToken("", ["User"]);

		var client = _factory.CreateClient();
		client.DefaultRequestHeaders.Authorization =
			new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokenResult.AccessToken);

		var response = await client.GetAsync("/api/v1/auth/me");

		Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
	}
}
