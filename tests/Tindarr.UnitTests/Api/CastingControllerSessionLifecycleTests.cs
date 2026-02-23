using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using Tindarr.Api.Controllers;
using Tindarr.Application.Abstractions.Persistence;
using Tindarr.Application.Abstractions.Security;
using Tindarr.Application.Interfaces.Casting;
using Tindarr.Application.Interfaces.Ops;
using Tindarr.Application.Interfaces.Playback;
using Tindarr.Application.Interfaces.Rooms;
using Tindarr.Application.Options;
using Tindarr.Contracts.Casting;
using Tindarr.Domain.Common;
using Tindarr.Domain.Interactions;
using Tindarr.Domain.Rooms;
using Tindarr.Infrastructure.Casting;

namespace Tindarr.UnitTests.Api;

public sealed class CastingControllerSessionLifecycleTests
{
	[Fact]
	public void RegisterSession_WhenDeviceRecasts_EndsPreviousSession()
	{
		var store = new CastingSessionStore(new MemoryCache(new MemoryCacheOptions()));

		store.RegisterSession(
			sessionId: "s1",
			deviceId: "device-1",
			contentTitle: "Movie 1",
			contentSubtitle: "Plex",
			contentType: "video/mp4",
			contentRuntimeSeconds: 10);

		store.RegisterSession(
			sessionId: "s2",
			deviceId: "device-1",
			contentTitle: "Movie 2",
			contentSubtitle: "Plex",
			contentType: "video/mp4",
			contentRuntimeSeconds: 10);

		var sessions = store.GetActiveSessions();
		Assert.Single(sessions);
		Assert.Equal("s2", sessions[0].SessionId);

		var events = store.GetRecentEvents();
		Assert.Contains(events, e => e.EventType == "session_ended" && e.Message.Contains("ended", StringComparison.OrdinalIgnoreCase));
	}

	[Fact]
	public async Task CastMovie_WhenCastAsyncThrows_DoesNotLeaveActiveSession()
	{
		var store = new CastingSessionStore(new MemoryCache(new MemoryCacheOptions()));
		var castClient = new TestCastClient(throwOnCast: true);
		var directProvider = new TestDirectPlaybackProvider(
			ServiceType.Plex,
			directUri: new Uri("http://10.0.0.2/movie.mp4"));

		var controller = CreateController(castClient, store, playbackProviders: [directProvider]);

		var request = new CastMovieRequest(
			DeviceId: "device-1",
			ServiceType: "Plex",
			ServerId: "server-1",
			TmdbId: 123,
			Title: "Test Movie");

		await Assert.ThrowsAsync<InvalidOperationException>(() => controller.CastMovie(request, CancellationToken.None));

		Assert.Empty(store.GetActiveSessions());

		var events = store.GetRecentEvents();
		Assert.Contains(events, e => e.EventType == "session_error" && e.DeviceId == "device-1");
	}

	[Fact]
	public async Task CastMovie_WhenCastAsyncSucceeds_RegistersActiveSession()
	{
		var store = new CastingSessionStore(new MemoryCache(new MemoryCacheOptions()));
		var castClient = new TestCastClient(throwOnCast: false);
		var directProvider = new TestDirectPlaybackProvider(
			ServiceType.Plex,
			directUri: new Uri("http://10.0.0.2/movie.mp4"));

		var controller = CreateController(castClient, store, playbackProviders: [directProvider]);

		var request = new CastMovieRequest(
			DeviceId: "device-1",
			ServiceType: "Plex",
			ServerId: "server-1",
			TmdbId: 123,
			Title: "Test Movie");

		var result = await controller.CastMovie(request, CancellationToken.None);
		Assert.IsType<Microsoft.AspNetCore.Mvc.OkResult>(result);

		var sessions = store.GetActiveSessions();
		Assert.Single(sessions);
		Assert.Equal("device-1", sessions[0].DeviceId);
		Assert.Equal("active", sessions[0].SessionState);
	}

	[Fact]
	public async Task GetMovieCastUrl_WhenDirectUrlAvailable_RegistersActiveSession()
	{
		var store = new CastingSessionStore(new MemoryCache(new MemoryCacheOptions()));
		var castClient = new TestCastClient(throwOnCast: false);
		var directProvider = new TestDirectPlaybackProvider(
			ServiceType.Plex,
			directUri: new Uri("http://10.0.0.2/movie.mp4"));

		var controller = CreateController(castClient, store, playbackProviders: [directProvider]);

		var request = new GetMovieCastUrlRequest(
			ServiceType: "Plex",
			ServerId: "server-1",
			TmdbId: 123,
			Title: "Test Movie",
			DeviceId: "Living Room TV");

		var result = await controller.GetMovieCastUrl(request, CancellationToken.None);
		var ok = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(result.Result);
		var dto = Assert.IsType<CastMediaUrlDto>(ok.Value);
		Assert.False(string.IsNullOrWhiteSpace(dto.SessionId));

		var sessions = store.GetActiveSessions();
		Assert.Single(sessions);
		Assert.Equal("Living Room TV", sessions[0].DeviceId);
		Assert.Equal("Test Movie", sessions[0].ContentTitle);
	}

	[Fact]
	public void EndCastingSession_WhenSessionExists_RemovesFromActiveSessions()
	{
		var store = new CastingSessionStore(new MemoryCache(new MemoryCacheOptions()));
		var controller = CreateController(new TestCastClient(throwOnCast: false), store, playbackProviders: []);

		store.RegisterSession(
			sessionId: "s1",
			deviceId: "Living Room TV",
			contentTitle: "Test Movie",
			contentSubtitle: "Plex",
			contentType: "video/mp4",
			contentRuntimeSeconds: 10);

		var result = controller.EndCastingSession("s1");
		Assert.IsType<Microsoft.AspNetCore.Mvc.OkResult>(result);
		Assert.Empty(store.GetActiveSessions());
	}

	[Fact]
	public async Task GetRoomQrCastUrl_WhenRoomExists_RegistersActiveSession()
	{
		var store = new CastingSessionStore(new MemoryCache(new MemoryCacheOptions()));
		var controller = new CastingController(
			castClient: new TestCastClient(throwOnCast: false),
			castUrlTokenService: new TestCastUrlTokenService(),
			playbackTokenService: new TestPlaybackTokenService(),
			playbackProviders: [],
			roomService: new TestRoomService(roomId: "room-1"),
			baseUrlResolver: new TestBaseUrlResolver(),
			joinAddressSettings: new TestJoinAddressSettingsRepository(),
			baseUrlOptions: Options.Create(new BaseUrlOptions { Lan = "http://10.0.0.1:5000" }),
			logger: NullLogger<CastingController>.Instance,
			castingSessionStore: store);

		controller.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
		{
			HttpContext = new DefaultHttpContext()
		};
		controller.ControllerContext.HttpContext.Request.Scheme = "http";
		controller.ControllerContext.HttpContext.Request.Host = new HostString("10.0.0.1", 6565);
		controller.ControllerContext.HttpContext.RequestServices = new ServiceCollection().BuildServiceProvider();

		var result = await controller.GetRoomQrCastUrl("room-1", variant: null, CancellationToken.None);
		var ok = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(result.Result);
		var dto = Assert.IsType<CastMediaUrlDto>(ok.Value);
		Assert.False(string.IsNullOrWhiteSpace(dto.SessionId));

		var sessions = store.GetActiveSessions();
		Assert.Single(sessions);
		Assert.Equal("Join room", sessions[0].ContentTitle);
		Assert.Equal("room-1", sessions[0].ContentSubtitle);
		Assert.Equal("image/png", sessions[0].ContentType);
	}

	private static CastingController CreateController(
		ICastClient castClient,
		CastingSessionStore store,
		IEnumerable<IPlaybackProvider> playbackProviders)
	{
		return new CastingController(
			castClient: castClient,
			castUrlTokenService: new TestCastUrlTokenService(),
			playbackTokenService: new TestPlaybackTokenService(),
			playbackProviders: playbackProviders,
			roomService: new TestRoomService(),
			baseUrlResolver: new TestBaseUrlResolver(),
			joinAddressSettings: new TestJoinAddressSettingsRepository(),
			baseUrlOptions: Options.Create(new BaseUrlOptions { Lan = "http://10.0.0.1:5000" }),
			logger: NullLogger<CastingController>.Instance,
			castingSessionStore: store);
	}

	private sealed class TestCastClient(bool throwOnCast) : ICastClient
	{
		public Task<IReadOnlyList<CastDevice>> DiscoverAsync(CancellationToken cancellationToken)
			=> Task.FromResult<IReadOnlyList<CastDevice>>([]);

		public Task CastAsync(string deviceId, CastMedia media, CancellationToken cancellationToken)
		{
			if (throwOnCast)
			{
				throw new InvalidOperationException("Cast failed");
			}

			return Task.CompletedTask;
		}
	}

	private sealed class TestDirectPlaybackProvider(ServiceType serviceType, Uri directUri) : IDirectPlaybackProvider
	{
		public ServiceType ServiceType => serviceType;

		public Task<UpstreamPlaybackRequest> BuildMovieStreamRequestAsync(ServiceScope scope, int tmdbId, CancellationToken cancellationToken)
			=> throw new NotImplementedException();

		public Task<Uri?> TryBuildDirectMovieStreamUrlAsync(ServiceScope scope, int tmdbId, CancellationToken cancellationToken)
			=> Task.FromResult<Uri?>(directUri);
	}

	private sealed class TestCastUrlTokenService : ICastUrlTokenService
	{
		public string IssueRoomQrToken(string roomId, DateTimeOffset nowUtc) => "token";

		public bool TryValidateRoomQrToken(string token, string roomId, DateTimeOffset nowUtc) => true;
	}

	private sealed class TestPlaybackTokenService : IPlaybackTokenService
	{
		public string IssueMovieToken(ServiceScope scope, int tmdbId, DateTimeOffset nowUtc) => "token";

		public bool TryValidateMovieToken(string token, ServiceScope scope, int tmdbId, DateTimeOffset nowUtc) => true;
	}

	private sealed class TestRoomService(string? roomId = null) : IRoomService
	{
		public Task<RoomState> CreateAsync(string ownerUserId, ServiceScope scope, string? roomName, CancellationToken cancellationToken)
			=> throw new NotImplementedException();

		public Task<RoomState> JoinAsync(string roomId, string userId, CancellationToken cancellationToken)
			=> throw new NotImplementedException();

		public Task<RoomState> CloseAsync(string roomId, string ownerUserId, CancellationToken cancellationToken)
			=> throw new NotImplementedException();

		public Task<RoomState?> GetAsync(string requestedRoomId, CancellationToken cancellationToken)
		{
			if (string.IsNullOrWhiteSpace(roomId))
			{
				throw new NotImplementedException();
			}
			if (!string.Equals(requestedRoomId, roomId, StringComparison.Ordinal))
			{
				return Task.FromResult<RoomState?>(null);
			}
			var now = DateTimeOffset.UtcNow;
			return Task.FromResult<RoomState?>(new RoomState(
				RoomId: roomId,
				OwnerUserId: "owner",
				Scope: new ServiceScope(ServiceType.Plex, "server-1"),
				IsClosed: false,
				CreatedAtUtc: now,
				LastActivityAtUtc: now,
				Members: []));
		}

		public Task<IReadOnlyList<SwipeCard>> GetSwipeDeckAsync(string roomId, string userId, int limit, CancellationToken cancellationToken)
			=> throw new NotImplementedException();

		public Task<Interaction> AddInteractionAsync(string roomId, string userId, int tmdbId, InteractionAction action, CancellationToken cancellationToken)
			=> throw new NotImplementedException();

		public Task<IReadOnlyList<int>> ListMatchesAsync(string roomId, CancellationToken cancellationToken)
			=> throw new NotImplementedException();
	}

	private sealed class TestBaseUrlResolver : IBaseUrlResolver
	{
		public Uri GetBaseUri(IPAddress? clientIp = null) => new("http://10.0.0.1:6565");

		public Uri Combine(string relativePathAndQuery, IPAddress? clientIp = null) => new(GetBaseUri(clientIp), relativePathAndQuery);

		public bool IsLanClient(IPAddress ip) => true;
	}

	private sealed class TestJoinAddressSettingsRepository : IJoinAddressSettingsRepository
	{
		public Task<JoinAddressSettingsRecord?> GetAsync(CancellationToken cancellationToken)
			=> Task.FromResult<JoinAddressSettingsRecord?>(null);

		public Task<JoinAddressSettingsRecord> UpsertAsync(JoinAddressSettingsUpsert upsert, CancellationToken cancellationToken)
			=> throw new NotImplementedException();
	}
}
