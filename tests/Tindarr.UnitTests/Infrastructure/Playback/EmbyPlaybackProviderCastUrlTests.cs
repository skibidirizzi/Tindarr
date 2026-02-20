using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Tindarr.Application.Abstractions.Persistence;
using Tindarr.Application.Interfaces.Playback;
using Tindarr.Domain.Common;
using Tindarr.Infrastructure.Playback.Providers;
using Xunit;

namespace Tindarr.UnitTests.Infrastructure.Playback;

public sealed class EmbyPlaybackProviderCastUrlTests
{
	[Fact]
	public async Task BuildMovieCastStreamRequestAsync_includes_DeviceId_and_MediaSourceId()
	{
		// Arrange
		var tmdbId = 123;
		var scope = new ServiceScope(ServiceType.Emby, "emby-main");

		var settingsRepo = new FakeServiceSettingsRepository(CreateServiceSettingsRecord(
			serviceType: ServiceType.Emby,
			serverId: "emby-main",
			embyBaseUrl: "http://emby.local:8096",
			embyApiKey: "api-key"));
		var castingSettingsRepo = new FakeCastingSettingsRepository(null);
		var httpClient = new HttpClient(new FakeEmbyHandler(tmdbId));

		var provider = new EmbyPlaybackProvider(
			settingsRepo,
			castingSettingsRepo,
			httpClient,
			NullLogger<EmbyPlaybackProvider>.Instance);

		// Act
		var request = await provider.BuildMovieCastStreamRequestAsync(scope, tmdbId, CancellationToken.None);
		var url = request.Uri.ToString();

		// Assert
		Assert.Contains("/emby/Videos/", url, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("/stream.mp4?", url, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("DeviceId=tindarr-cast%3Aemby-main", url, StringComparison.Ordinal);
		Assert.Contains("MediaSourceId=ms-1", url, StringComparison.Ordinal);
		Assert.Contains("Static=false", url, StringComparison.Ordinal);
		Assert.Contains("AudioCodec=aac", url, StringComparison.Ordinal);
		Assert.Contains("VideoCodec=h264", url, StringComparison.Ordinal);
		Assert.Contains("MaxWidth=1920", url, StringComparison.Ordinal);
		Assert.Contains("MaxHeight=1080", url, StringComparison.Ordinal);
		Assert.Contains("VideoBitRate=8000000", url, StringComparison.Ordinal);
	}

	private sealed class FakeEmbyHandler : HttpMessageHandler
	{
		private readonly int _tmdbId;

		public FakeEmbyHandler(int tmdbId)
		{
			_tmdbId = tmdbId;
		}

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var pathAndQuery = request.RequestUri?.PathAndQuery ?? string.Empty;

			if (pathAndQuery.Contains("/emby/Users", StringComparison.OrdinalIgnoreCase)
				&& request.Method == HttpMethod.Get
				&& !pathAndQuery.Contains("/emby/Users/", StringComparison.OrdinalIgnoreCase))
			{
				return Task.FromResult(Json(new[]
				{
					new { Id = "user-1", Policy = new { IsAdministrator = true } }
				}));
			}

			if (pathAndQuery.Contains("/emby/Users/user-1/Items", StringComparison.OrdinalIgnoreCase)
				&& request.Method == HttpMethod.Get
				&& !pathAndQuery.Contains("/emby/Users/user-1/Items/item-1", StringComparison.OrdinalIgnoreCase))
			{
				return Task.FromResult(Json(new
				{
					Items = new[]
					{
						new
						{
							Id = "item-1",
							ProviderIds = new Dictionary<string, string> { ["Tmdb"] = _tmdbId.ToString() }
						}
					}
				}));
			}

			if (pathAndQuery.Contains("/emby/Users/user-1/Items/item-1", StringComparison.OrdinalIgnoreCase)
				&& request.Method == HttpMethod.Get)
			{
				return Task.FromResult(Json(new
				{
					MediaStreams = Array.Empty<object>()
				}));
			}

			if (pathAndQuery.Contains("/emby/Items/item-1/PlaybackInfo", StringComparison.OrdinalIgnoreCase)
				&& request.Method == HttpMethod.Get)
			{
				return Task.FromResult(Json(new
				{
					MediaSources = new[]
					{
						new { Id = "ms-1" }
					},
					PlaySessionId = "ps-1"
				}));
			}

			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
			{
				Content = new StringContent($"No route matched: {request.Method} {pathAndQuery}")
			});
		}

		private static HttpResponseMessage Json(object payload)
		{
			var json = JsonSerializer.Serialize(payload);
			return new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new StringContent(json, Encoding.UTF8, "application/json")
			};
		}
	}

	private static ServiceSettingsRecord CreateServiceSettingsRecord(
		ServiceType serviceType,
		string serverId,
		string? embyBaseUrl = null,
		string? embyApiKey = null)
	{
		return new ServiceSettingsRecord(
			ServiceType: serviceType,
			ServerId: serverId,
			RadarrBaseUrl: "",
			RadarrApiKey: "",
			RadarrQualityProfileId: null,
			RadarrRootFolderPath: null,
			RadarrTagLabel: null,
			RadarrTagId: null,
			RadarrAutoAddEnabled: false,
			RadarrAutoAddIntervalMinutes: null,
			RadarrLastAutoAddRunUtc: null,
			RadarrLastAutoAddAcceptedId: null,
			RadarrLastLibrarySyncUtc: null,
			MatchMinUsers: null,
			MatchMinUserPercent: null,
			JellyfinBaseUrl: null,
			JellyfinApiKey: null,
			JellyfinServerName: null,
			JellyfinServerVersion: null,
			JellyfinLastLibrarySyncUtc: null,
			EmbyBaseUrl: embyBaseUrl,
			EmbyApiKey: embyApiKey,
			EmbyServerName: null,
			EmbyServerVersion: null,
			EmbyLastLibrarySyncUtc: null,
			PlexClientIdentifier: null,
			PlexAuthToken: null,
			PlexServerName: null,
			PlexServerUri: null,
			PlexServerVersion: null,
			PlexServerPlatform: null,
			PlexServerOwned: null,
			PlexServerOnline: null,
			PlexServerAccessToken: null,
			PlexLastLibrarySyncUtc: null,
			UpdatedAtUtc: DateTimeOffset.UtcNow);
	}

	private sealed class FakeServiceSettingsRepository(ServiceSettingsRecord record) : IServiceSettingsRepository
	{
		public Task<ServiceSettingsRecord?> GetAsync(ServiceScope scope, CancellationToken cancellationToken)
		{
			if (scope.ServiceType != record.ServiceType)
			{
				return Task.FromResult<ServiceSettingsRecord?>(null);
			}

			if (!string.Equals(scope.ServerId, record.ServerId, StringComparison.OrdinalIgnoreCase))
			{
				return Task.FromResult<ServiceSettingsRecord?>(null);
			}

			return Task.FromResult<ServiceSettingsRecord?>(record);
		}

		public Task<IReadOnlyList<ServiceSettingsRecord>> ListAsync(ServiceType serviceType, CancellationToken cancellationToken) =>
			throw new NotSupportedException();

		public Task UpsertAsync(ServiceScope scope, ServiceSettingsUpsert upsert, CancellationToken cancellationToken) =>
			throw new NotSupportedException();

		public Task<bool> DeleteAsync(ServiceScope scope, CancellationToken cancellationToken) =>
			throw new NotSupportedException();
	}

	private sealed class FakeCastingSettingsRepository(CastingSettingsRecord? record) : ICastingSettingsRepository
	{
		public Task<CastingSettingsRecord?> GetAsync(CancellationToken cancellationToken) => Task.FromResult(record);

		public Task<CastingSettingsRecord> UpsertAsync(CastingSettingsUpsert upsert, CancellationToken cancellationToken) =>
			throw new NotSupportedException();
	}
}
