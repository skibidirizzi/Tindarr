using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Tindarr.Application.Abstractions.Persistence;
using Tindarr.Application.Options;
using Tindarr.Domain.Common;

namespace Tindarr.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/v1/plex/webhook")]
public sealed class PlexWebhookController(
	IPlexLibraryCacheRepository plexCache,
	IServiceSettingsRepository settingsRepo,
	IOptions<PlexOptions> plexOptions,
	ILogger<PlexWebhookController> logger) : ControllerBase
{
	private static readonly Regex TmdbIdRegex = new(@"tmdb[^0-9]*([0-9]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

	[HttpPost]
	[Consumes("multipart/form-data", "application/json")]
	public async Task<IActionResult> Ingest(CancellationToken cancellationToken)
	{
		if (!IsAuthorized(Request, plexOptions.Value.WebhookToken))
		{
			return Unauthorized();
		}

		string? payloadJson = null;
		if (Request.HasFormContentType)
		{
			var form = await Request.ReadFormAsync(cancellationToken);
			payloadJson = form["payload"].FirstOrDefault();
		}
		else
		{
			using var reader = new StreamReader(Request.Body);
			payloadJson = await reader.ReadToEndAsync(cancellationToken);
		}

		if (string.IsNullOrWhiteSpace(payloadJson))
		{
			return BadRequest("Missing payload.");
		}

		try
		{
			using var doc = JsonDocument.Parse(payloadJson);
			var root = doc.RootElement;

			var serverId = TryGetServerId(root);
			if (string.IsNullOrWhiteSpace(serverId))
			{
				return BadRequest("Missing server id.");
			}

			var scope = new ServiceScope(ServiceType.Plex, serverId);

			// Keep behavior consistent with Plex deck: if server isn't configured, fail loudly.
			var settings = await settingsRepo.GetAsync(scope, cancellationToken);
			if (settings is null)
			{
				return NotFound("Plex server is not configured in Tindarr.");
			}

			var eventName = TryGetString(root, "event") ?? string.Empty;
			var metadata = TryGetObject(root, "Metadata");
			if (metadata is null)
			{
				return Ok();
			}

			var type = TryGetString(metadata.Value, "type") ?? string.Empty;
			if (!string.Equals(type, "movie", StringComparison.OrdinalIgnoreCase))
			{
				return Ok();
			}

			var tmdbId = TryParseTmdbId(metadata.Value);
			if (tmdbId is null || tmdbId <= 0)
			{
				logger.LogDebug("plex webhook payload missing TMDB id. ServerId={ServerId} Event={Event}", serverId, eventName);
				return Ok();
			}

			var now = DateTimeOffset.UtcNow;
			if (IsDeleteEvent(eventName))
			{
				await plexCache.RemoveTmdbIdsAsync(scope, new[] { tmdbId.Value }, cancellationToken);
			}
			else
			{
				await plexCache.AddTmdbIdsAsync(scope, new[] { tmdbId.Value }, now, cancellationToken);
			}

			return Ok();
		}
		catch (JsonException ex)
		{
			logger.LogWarning(ex, "invalid plex webhook payload");
			return BadRequest("Invalid JSON payload.");
		}
	}

	private static bool IsAuthorized(HttpRequest request, string? configuredToken)
	{
		if (string.IsNullOrWhiteSpace(configuredToken))
		{
			return true;
		}

		var provided = request.Headers["X-Tindarr-Webhook-Token"].FirstOrDefault();
		if (string.IsNullOrWhiteSpace(provided))
		{
			provided = request.Query["token"].FirstOrDefault();
		}

		return string.Equals(provided, configuredToken, StringComparison.Ordinal);
	}

	private static bool IsDeleteEvent(string eventName)
	{
		return eventName.Contains("delete", StringComparison.OrdinalIgnoreCase)
			|| eventName.Contains("deleted", StringComparison.OrdinalIgnoreCase)
			|| eventName.Contains("removed", StringComparison.OrdinalIgnoreCase);
	}

	private static string? TryGetServerId(JsonElement root)
	{
		var server = TryGetObject(root, "Server");
		if (server is not null)
		{
			var uuid = TryGetString(server.Value, "uuid")
				?? TryGetString(server.Value, "machineIdentifier")
				?? TryGetString(server.Value, "machine_identifier");
			if (!string.IsNullOrWhiteSpace(uuid))
			{
				return uuid;
			}
		}

		return TryGetString(root, "serverUuid")
			?? TryGetString(root, "server_uuid")
			?? TryGetString(root, "serverId")
			?? TryGetString(root, "server_id");
	}

	private static int? TryParseTmdbId(JsonElement metadata)
	{
		foreach (var candidate in EnumerateGuidCandidates(metadata))
		{
			if (string.IsNullOrWhiteSpace(candidate))
			{
				continue;
			}

			if (!candidate.Contains("tmdb", StringComparison.OrdinalIgnoreCase)
				&& !candidate.Contains("themoviedb", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			var direct = ExtractDigitsAfterScheme(candidate);
			if (direct is not null)
			{
				return direct;
			}

			var match = TmdbIdRegex.Match(candidate);
			if (match.Success && int.TryParse(match.Groups[1].Value, out var id))
			{
				return id;
			}
		}

		return null;
	}

	private static int? ExtractDigitsAfterScheme(string candidate)
	{
		var idx = candidate.IndexOf("://", StringComparison.OrdinalIgnoreCase);
		if (idx < 0)
		{
			return null;
		}

		var remainder = candidate[(idx + 3)..];
		var digits = new string(remainder.TakeWhile(char.IsDigit).ToArray());
		return int.TryParse(digits, out var id) ? id : null;
	}

	private static IEnumerable<string?> EnumerateGuidCandidates(JsonElement metadata)
	{
		// guid can be string, or an array of objects with id fields.
		if (metadata.TryGetProperty("guid", out var guid))
		{
			foreach (var s in EnumerateGuidElement(guid))
			{
				yield return s;
			}
		}
		if (metadata.TryGetProperty("Guid", out var guidPascal))
		{
			foreach (var s in EnumerateGuidElement(guidPascal))
			{
				yield return s;
			}
		}
	}

	private static IEnumerable<string?> EnumerateGuidElement(JsonElement element)
	{
		if (element.ValueKind == JsonValueKind.String)
		{
			yield return element.GetString();
			yield break;
		}

		if (element.ValueKind == JsonValueKind.Array)
		{
			foreach (var entry in element.EnumerateArray())
			{
				if (entry.ValueKind == JsonValueKind.String)
				{
					yield return entry.GetString();
				}
				else if (entry.ValueKind == JsonValueKind.Object)
				{
					var id = TryGetString(entry, "id") ?? TryGetString(entry, "Id");
					if (!string.IsNullOrWhiteSpace(id))
					{
						yield return id;
					}
				}
			}
		}
	}

	private static JsonElement? TryGetObject(JsonElement root, string property)
	{
		if (root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Object)
		{
			return value;
		}
		return null;
	}

	private static string? TryGetString(JsonElement root, string property)
	{
		if (root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String)
		{
			return value.GetString();
		}
		return null;
	}
}
