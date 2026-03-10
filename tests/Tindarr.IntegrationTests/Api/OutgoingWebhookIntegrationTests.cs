using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tindarr.Contracts.Auth;
using Tindarr.Contracts.Interactions;
using Xunit;

namespace Tindarr.IntegrationTests.Api;

public sealed class OutgoingWebhookIntegrationTests : IClassFixture<TindarrWebApplicationFactory>
{
	private readonly TindarrWebApplicationFactory _factory;
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		PropertyNameCaseInsensitive = true,
		Converters = { new JsonStringEnumConverter() }
	};

	public OutgoingWebhookIntegrationTests(TindarrWebApplicationFactory factory)
	{
		_factory = factory;
	}

	private static async Task<HttpClient> CreateAuthenticatedAdminClientAsync(WebApplicationFactory<Program> factory)
	{
		var client = factory.CreateClient();
		var userId = $"webhook-admin-{Guid.NewGuid():N}";
		var response = await client.PostAsJsonAsync("/api/v1/auth/register",
			new RegisterRequest(userId, "Webhook Admin", "TestPassword1!"), JsonOptions);
		if (!response.IsSuccessStatusCode)
		{
			var errorBody = await response.Content.ReadAsStringAsync();
			Assert.Fail($"Register request failed: {(int)response.StatusCode} {response.StatusCode}\n{errorBody}");
		}
		var auth = await response.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions);
		Assert.NotNull(auth);
		client.DefaultRequestHeaders.Authorization =
			new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", auth!.AccessToken);
		return client;
	}

	private sealed record CapturedWebhook(string Url, string EventType, string BodyJson);

	private sealed class WebhookCaptureStore
	{
		private readonly Channel<CapturedWebhook> _channel = Channel.CreateUnbounded<CapturedWebhook>(
			new UnboundedChannelOptions { SingleReader = false, SingleWriter = false });

		public void Add(CapturedWebhook captured) => _channel.Writer.TryWrite(captured);

		public async Task<CapturedWebhook> ReadAsync(TimeSpan timeout)
		{
			using var cts = new CancellationTokenSource(timeout);
			return await _channel.Reader.ReadAsync(cts.Token);
		}
	}

	private sealed class WebhookCaptureHandler(WebhookCaptureStore store) : HttpMessageHandler
	{
		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var eventType = request.Headers.TryGetValues("X-Tindarr-Event", out var values)
				? values.FirstOrDefault() ?? string.Empty
				: string.Empty;

			var body = request.Content is null
				? string.Empty
				: await request.Content.ReadAsStringAsync(cancellationToken);

			store.Add(new CapturedWebhook(request.RequestUri?.ToString() ?? string.Empty, eventType, body));

			return new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new StringContent("ok")
			};
		}
	}

	[Fact]
	public async Task Like_swipe_posts_outgoing_webhook_when_enabled()
	{
		var store = new WebhookCaptureStore();
		var factory = _factory.WithWebHostBuilder(builder =>
		{
			builder.ConfigureAppConfiguration((_, config) =>
			{
				config.AddInMemoryCollection(new Dictionary<string, string?>
				{
					["Registration:DefaultRole"] = "Admin"
				});
			});

			builder.ConfigureServices(services =>
			{
				services.AddSingleton(store);
				services.AddHttpClient(Tindarr.Workers.Jobs.OutgoingWebhookDeliveryWorker.HttpClientName)
					.ConfigurePrimaryHttpMessageHandler(() => new WebhookCaptureHandler(store));
			});
		});

		var client = await CreateAuthenticatedAdminClientAsync(factory);

		var update = new
		{
			notificationsSet = true,
			notificationsEnabled = true,
			notificationsWebhookUrls = new[] { "https://webhook.test/receiver" },
			notificationsEventLikes = true,
			notificationsEventMatches = false,
			notificationsEventRoomCreated = false,
			notificationsEventLogin = false,
			notificationsEventUserCreated = false,
			notificationsEventAuthFailures = false,
		};

		var putResp = await client.PutAsJsonAsync("/api/v1/admin/advanced-settings", update, JsonOptions);
		if (!putResp.IsSuccessStatusCode)
		{
			var errorBody = await putResp.Content.ReadAsStringAsync();
			Assert.Fail($"Advanced settings update failed: {(int)putResp.StatusCode} {putResp.StatusCode}\n{errorBody}");
		}

		var swipeResp = await client.PostAsJsonAsync("/api/v1/interactions",
			new SwipeRequest(424242, SwipeActionDto.Like, "tmdb", "tmdb"), JsonOptions);
		if (!swipeResp.IsSuccessStatusCode)
		{
			var errorBody = await swipeResp.Content.ReadAsStringAsync();
			Assert.Fail($"Swipe request failed: {(int)swipeResp.StatusCode} {swipeResp.StatusCode}\n{errorBody}");
		}

		var captured = await store.ReadAsync(TimeSpan.FromSeconds(5));
		Assert.Equal("tindarr.like", captured.EventType);
		Assert.Equal("https://webhook.test/receiver", captured.Url);

		using var doc = JsonDocument.Parse(captured.BodyJson);
		Assert.Equal("tindarr.like", doc.RootElement.GetProperty("event").GetString());
		var data = doc.RootElement.GetProperty("data");
		Assert.Equal(424242, data.GetProperty("tmdbId").GetInt32());
		Assert.Equal("like", data.GetProperty("action").GetString()?.ToLowerInvariant());
	}
}
