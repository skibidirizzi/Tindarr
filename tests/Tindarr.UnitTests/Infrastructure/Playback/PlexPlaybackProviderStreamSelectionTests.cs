using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tindarr.Application.Abstractions.Integrations;
using Tindarr.Application.Abstractions.Persistence;
using Tindarr.Application.Interfaces.Playback;
using Tindarr.Application.Options;
using Tindarr.Domain.Common;
using Tindarr.Infrastructure.Playback.Providers;

namespace Tindarr.UnitTests.Infrastructure.Playback;

public sealed class PlexPlaybackProviderStreamSelectionTests
{
	[Fact]
	public async Task BuildMovieStreamRequestAsync_AddsAudioAndSubtitleStreamIds()
	{
		var metadataXml = """
		<MediaContainer size="1">
		  <Video ratingKey="999" type="movie">
		    <Media id="1" selected="1">
		      <Part id="10" key="/library/parts/10/file.mkv" selected="1">
		        <Stream id="201" streamType="2" codec="ac3" channels="6" languageTag="en" displayTitle="English" default="1" />
		        <Stream id="202" streamType="2" codec="ac3" channels="6" languageTag="en" displayTitle="English Commentary" title="Commentary" default="0" />
		        <Stream id="301" streamType="3" codec="srt" languageTag="en" location="embedded" forced="0" default="0" />
		        <Stream id="302" streamType="3" codec="srt" languageTag="en" location="external" forced="0" default="1" />
		      </Part>
		    </Media>
		  </Video>
		</MediaContainer>
		""";

		var httpClient = new HttpClient(new PlexMetadataHandler(metadataXml));

		var settingsRepo = new FakeServiceSettingsRepository(
			server: CreateServiceSettingsRecord(
				serviceType: ServiceType.Plex,
				serverId: "server-1",
				plexServerUri: "http://127.0.0.1:32400",
				plexServerAccessToken: "server-token",
				plexClientIdentifier: "client-1"),
			account: CreateServiceSettingsRecord(
				serviceType: ServiceType.Plex,
				serverId: PlexConstants.AccountServerId,
				plexServerUri: "http://127.0.0.1:32400",
				plexAuthToken: "account-token",
				plexClientIdentifier: "client-1"));

		var castingSettingsRepo = new FakeCastingSettingsRepository(new CastingSettingsRecord(
			PreferredSubtitleSource: "auto",
			PreferredSubtitleLanguage: "en",
			PreferredSubtitleTrackSource: "external",
			SubtitleFallback: "auto",
			SubtitleLanguageFallback: null,
			SubtitleTrackSourceFallback: null,
			PreferredAudioStyle: "auto",
			PreferredAudioLanguage: "en",
			PreferredAudioTrackKind: "main",
			AudioFallback: null,
			AudioLanguageFallback: null,
			AudioTrackKindFallback: null,
			UpdatedAtUtc: DateTimeOffset.UtcNow));

		var plexCache = new FakePlexLibraryCacheRepository(ratingKey: "999");
		var options = Options.Create(new PlexOptions());

		var provider = new PlexPlaybackProvider(
			settingsRepo,
			castingSettingsRepo,
			plexCache,
			httpClient,
			options,
			NullLogger<PlexPlaybackProvider>.Instance);

		var request = await provider.BuildMovieStreamRequestAsync(new ServiceScope(ServiceType.Plex, "server-1"), 123, CancellationToken.None);
		var uriString = request.Uri.ToString();

		Assert.Contains("audioStreamID=201", uriString, StringComparison.Ordinal);
		Assert.Contains("subtitleStreamID=302", uriString, StringComparison.Ordinal);
	}

	[Fact]
	public async Task BuildMovieStreamRequestAsync_RespectsAudioTrackKindCommentary()
	{
		var metadataXml = """
		<MediaContainer size="1">
		  <Video ratingKey="999" type="movie">
		    <Media id="1" selected="1">
		      <Part id="10" selected="1">
		        <Stream id="201" streamType="2" codec="ac3" channels="6" languageTag="en" displayTitle="English" default="1" />
		        <Stream id="202" streamType="2" codec="ac3" channels="6" languageTag="en" displayTitle="English Commentary" title="Director Commentary" default="0" />
		      </Part>
		    </Media>
		  </Video>
		</MediaContainer>
		""";

		var httpClient = new HttpClient(new PlexMetadataHandler(metadataXml));

		var settingsRepo = new FakeServiceSettingsRepository(
			server: CreateServiceSettingsRecord(
				serviceType: ServiceType.Plex,
				serverId: "server-1",
				plexServerUri: "http://127.0.0.1:32400",
				plexServerAccessToken: "server-token",
				plexClientIdentifier: "client-1"),
			account: CreateServiceSettingsRecord(
				serviceType: ServiceType.Plex,
				serverId: PlexConstants.AccountServerId,
				plexServerUri: "http://127.0.0.1:32400",
				plexAuthToken: "account-token",
				plexClientIdentifier: "client-1"));

		var castingSettingsRepo = new FakeCastingSettingsRepository(new CastingSettingsRecord(
			PreferredSubtitleSource: "none",
			PreferredSubtitleLanguage: null,
			PreferredSubtitleTrackSource: "auto",
			SubtitleFallback: null,
			SubtitleLanguageFallback: null,
			SubtitleTrackSourceFallback: null,
			PreferredAudioStyle: "auto",
			PreferredAudioLanguage: "en",
			PreferredAudioTrackKind: "commentary",
			AudioFallback: null,
			AudioLanguageFallback: null,
			AudioTrackKindFallback: null,
			UpdatedAtUtc: DateTimeOffset.UtcNow));

		var plexCache = new FakePlexLibraryCacheRepository(ratingKey: "999");
		var options = Options.Create(new PlexOptions());

		var provider = new PlexPlaybackProvider(
			settingsRepo,
			castingSettingsRepo,
			plexCache,
			httpClient,
			options,
			NullLogger<PlexPlaybackProvider>.Instance);

		var request = await provider.BuildMovieStreamRequestAsync(new ServiceScope(ServiceType.Plex, "server-1"), 123, CancellationToken.None);
		Assert.Contains("audioStreamID=202", request.Uri.ToString(), StringComparison.Ordinal);
		Assert.DoesNotContain("subtitleStreamID=", request.Uri.ToString(), StringComparison.Ordinal);
	}

	private static ServiceSettingsRecord CreateServiceSettingsRecord(
		ServiceType serviceType,
		string serverId,
		string? plexServerUri = null,
		string? plexServerAccessToken = null,
		string? plexAuthToken = null,
		string? plexClientIdentifier = null)
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
			EmbyBaseUrl: null,
			EmbyApiKey: null,
			EmbyServerName: null,
			EmbyServerVersion: null,
			EmbyLastLibrarySyncUtc: null,
			PlexClientIdentifier: plexClientIdentifier,
			PlexAuthToken: plexAuthToken,
			PlexServerName: null,
			PlexServerUri: plexServerUri,
			PlexServerVersion: null,
			PlexServerPlatform: null,
			PlexServerOwned: null,
			PlexServerOnline: null,
			PlexServerAccessToken: plexServerAccessToken,
			PlexLastLibrarySyncUtc: null,
			UpdatedAtUtc: DateTimeOffset.UtcNow);
	}

	private sealed class PlexMetadataHandler(string metadataXml) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var response = new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new StringContent(metadataXml, Encoding.UTF8, "application/xml")
			};
			return Task.FromResult(response);
		}
	}

	private sealed class FakeCastingSettingsRepository(CastingSettingsRecord? record) : ICastingSettingsRepository
	{
		public Task<CastingSettingsRecord?> GetAsync(CancellationToken cancellationToken) => Task.FromResult(record);

		public Task<CastingSettingsRecord> UpsertAsync(CastingSettingsUpsert upsert, CancellationToken cancellationToken) =>
			throw new NotSupportedException();
	}

	private sealed class FakePlexLibraryCacheRepository(string ratingKey) : IPlexLibraryCacheRepository
	{
		public Task<IReadOnlyCollection<int>> GetTmdbIdsAsync(ServiceScope scope, CancellationToken cancellationToken) =>
			throw new NotSupportedException();

		public Task<int> CountTmdbIdsAsync(ServiceScope scope, CancellationToken cancellationToken) =>
			throw new NotSupportedException();

		public Task<IReadOnlyList<PlexLibraryItem>> ListItemsAsync(ServiceScope scope, int skip, int take, CancellationToken cancellationToken) =>
			throw new NotSupportedException();

		public Task<string?> TryGetRatingKeyAsync(ServiceScope scope, int tmdbId, CancellationToken cancellationToken) =>
			Task.FromResult<string?>(ratingKey);

		public Task ReplaceTmdbIdsAsync(ServiceScope scope, IReadOnlyCollection<int> tmdbIds, DateTimeOffset syncedAtUtc, CancellationToken cancellationToken) =>
			throw new NotSupportedException();

		public Task ReplaceItemsAsync(ServiceScope scope, IReadOnlyCollection<PlexLibraryItem> items, DateTimeOffset syncedAtUtc, CancellationToken cancellationToken) =>
			throw new NotSupportedException();

		public Task AddTmdbIdsAsync(ServiceScope scope, IReadOnlyCollection<int> tmdbIds, DateTimeOffset syncedAtUtc, CancellationToken cancellationToken) =>
			throw new NotSupportedException();

		public Task RemoveTmdbIdsAsync(ServiceScope scope, IReadOnlyCollection<int> tmdbIds, CancellationToken cancellationToken) =>
			throw new NotSupportedException();
	}

	private sealed class FakeServiceSettingsRepository(ServiceSettingsRecord server, ServiceSettingsRecord account) : IServiceSettingsRepository
	{
		public Task<ServiceSettingsRecord?> GetAsync(ServiceScope scope, CancellationToken cancellationToken)
		{
			if (string.Equals(scope.ServerId, PlexConstants.AccountServerId, StringComparison.OrdinalIgnoreCase))
			{
				return Task.FromResult<ServiceSettingsRecord?>(account);
			}

			return Task.FromResult<ServiceSettingsRecord?>(server);
		}

		public Task<IReadOnlyList<ServiceSettingsRecord>> ListAsync(ServiceType serviceType, CancellationToken cancellationToken) =>
			throw new NotSupportedException();

		public Task UpsertAsync(ServiceScope scope, ServiceSettingsUpsert upsert, CancellationToken cancellationToken) =>
			throw new NotSupportedException();
	}
}
