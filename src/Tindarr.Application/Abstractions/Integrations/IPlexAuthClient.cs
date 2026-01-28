namespace Tindarr.Application.Abstractions.Integrations;

public interface IPlexAuthClient
{
	Task<PlexPinResult> CreatePinAsync(string clientIdentifier, CancellationToken cancellationToken);

	Task<PlexPinResult?> GetPinAsync(string clientIdentifier, long pinId, CancellationToken cancellationToken);

	Task<PlexTokenValidationResult> ValidateTokenAsync(string clientIdentifier, string authToken, CancellationToken cancellationToken);

	Task<IReadOnlyList<PlexServerResource>> GetServersAsync(string clientIdentifier, string authToken, CancellationToken cancellationToken);
}

public sealed record PlexPinResult(
	long Id,
	string Code,
	DateTimeOffset? ExpiresAtUtc,
	string? AuthToken);

public sealed record PlexTokenValidationResult(bool Ok, string? Message);

public sealed record PlexServerResource(
	string MachineIdentifier,
	string Name,
	string? Version,
	string? Platform,
	bool Owned,
	bool Online,
	string? AccessToken,
	IReadOnlyList<PlexServerConnection> Connections);

public sealed record PlexServerConnection(
	string Uri,
	bool Local,
	bool Relay,
	string Protocol);
