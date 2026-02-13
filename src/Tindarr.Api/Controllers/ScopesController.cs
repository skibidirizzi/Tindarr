using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tindarr.Application.Abstractions.Integrations;
using Tindarr.Application.Abstractions.Persistence;
using Tindarr.Contracts.Common;
using Tindarr.Domain.Common;

namespace Tindarr.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/scopes")]
public sealed class ScopesController(IServiceSettingsRepository settingsRepo) : ControllerBase
{
	[HttpGet]
	public async Task<ActionResult<IReadOnlyList<ServiceScopeOptionDto>>> ListConfiguredScopes(CancellationToken cancellationToken)
	{
		var options = new List<ServiceScopeOptionDto>
		{
			new(
				ServiceType.Tmdb.ToString().ToLowerInvariant(),
				"tmdb",
				"TMDB (tmdb)")
		};

		static string NormalizeServiceType(ServiceType serviceType) => serviceType.ToString().ToLowerInvariant();

		static ServiceScopeOptionDto Map(
			ServiceType serviceType,
			string serverId,
			string? name,
			string? baseUrl)
		{
			var service = NormalizeServiceType(serviceType);
			var label = !string.IsNullOrWhiteSpace(name)
				? $"{name} ({serverId})"
				: !string.IsNullOrWhiteSpace(baseUrl)
					? $"{service} {baseUrl} ({serverId})"
					: $"{service} ({serverId})";

			return new ServiceScopeOptionDto(service, serverId, label);
		}

		var radarrRows = await settingsRepo.ListAsync(ServiceType.Radarr, cancellationToken).ConfigureAwait(false);
		options.AddRange(radarrRows
			.Where(x => !string.IsNullOrWhiteSpace(x.ServerId))
			.Where(x => !string.IsNullOrWhiteSpace(x.RadarrBaseUrl))
			.Select(x => Map(ServiceType.Radarr, x.ServerId, name: null, baseUrl: x.RadarrBaseUrl)));

		var plexRows = await settingsRepo.ListAsync(ServiceType.Plex, cancellationToken).ConfigureAwait(false);
		options.AddRange(plexRows
			.Where(x => !string.IsNullOrWhiteSpace(x.ServerId))
			.Where(x => !string.Equals(x.ServerId, PlexConstants.AccountServerId, StringComparison.OrdinalIgnoreCase))
			.Where(x => !string.IsNullOrWhiteSpace(x.PlexServerUri))
			.Select(x => Map(ServiceType.Plex, x.ServerId, name: x.PlexServerName, baseUrl: x.PlexServerUri)));

		var jellyfinRows = await settingsRepo.ListAsync(ServiceType.Jellyfin, cancellationToken).ConfigureAwait(false);
		options.AddRange(jellyfinRows
			.Where(x => !string.IsNullOrWhiteSpace(x.ServerId))
			.Where(x => !string.IsNullOrWhiteSpace(x.JellyfinBaseUrl))
			.Select(x => Map(ServiceType.Jellyfin, x.ServerId, name: x.JellyfinServerName, baseUrl: x.JellyfinBaseUrl)));

		var embyRows = await settingsRepo.ListAsync(ServiceType.Emby, cancellationToken).ConfigureAwait(false);
		options.AddRange(embyRows
			.Where(x => !string.IsNullOrWhiteSpace(x.ServerId))
			.Where(x => !string.IsNullOrWhiteSpace(x.EmbyBaseUrl))
			.Select(x => Map(ServiceType.Emby, x.ServerId, name: x.EmbyServerName, baseUrl: x.EmbyBaseUrl)));

		// de-dupe + stable order
		var distinct = options
			.GroupBy(x => (x.ServiceType, x.ServerId), StringTupleComparer.OrdinalIgnoreCase)
			.Select(g => g.First())
			.OrderBy(x => x.ServiceType)
			.ThenBy(x => x.DisplayName)
			.ToList();

		return Ok(distinct);
	}

	private sealed class StringTupleComparer : IEqualityComparer<(string ServiceType, string ServerId)>
	{
		public static readonly StringTupleComparer OrdinalIgnoreCase = new();

		public bool Equals((string ServiceType, string ServerId) x, (string ServiceType, string ServerId) y)
		{
			return string.Equals(x.ServiceType, y.ServiceType, StringComparison.OrdinalIgnoreCase)
				&& string.Equals(x.ServerId, y.ServerId, StringComparison.OrdinalIgnoreCase);
		}

		public int GetHashCode((string ServiceType, string ServerId) obj)
		{
			return HashCode.Combine(
				StringComparer.OrdinalIgnoreCase.GetHashCode(obj.ServiceType),
				StringComparer.OrdinalIgnoreCase.GetHashCode(obj.ServerId));
		}
	}
}
