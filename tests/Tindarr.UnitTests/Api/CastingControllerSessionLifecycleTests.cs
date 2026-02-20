using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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

	private sealed class TestRoomService : IRoomService
	{
		public Task<RoomState> CreateAsync(string ownerUserId, ServiceScope scope, CancellationToken cancellationToken)
			=> throw new NotImplementedException();

		public Task<RoomState> JoinAsync(string roomId, string userId, CancellationToken cancellationToken)
			=> throw new NotImplementedException();

		public Task<RoomState> CloseAsync(string roomId, string ownerUserId, CancellationToken cancellationToken)
			=> throw new NotImplementedException();

		public Task<RoomState?> GetAsync(string roomId, CancellationToken cancellationToken)
			=> throw new NotImplementedException();

		public Task<IReadOnlyList<SwipeCard>> GetSwipeDeckAsync(string roomId, string userId, int limit, CancellationToken cancellationToken)
			=> throw new NotImplementedException();

		public Task<Interaction> AddInteractionAsync(string roomId, string userId, int tmdbId, InteractionAction action, CancellationToken cancellationToken)
			=> throw new NotImplementedException();

		public Task<IReadOnlyList<int>> ListMatchesAsync(string roomId, CancellationToken cancellationToken)
			=> throw new NotImplementedException();
	}

	private sealed class TestBaseUrlResolver : IBaseUrlResolver
	{
		public Uri GetBaseUri(IPAddress? clientIp = null) => new("http://10.0.0.1:5000");

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
