namespace Tindarr.Application.Abstractions.Integrations;

public interface IJellyfinClient
{
	Task<JellyfinConnectionTestResult> TestConnectionAsync(JellyfinConnection connection, CancellationToken cancellationToken);

	Task<JellyfinSystemInfo> GetSystemInfoAsync(JellyfinConnection connection, CancellationToken cancellationToken);

	Task<IReadOnlyList<int>> GetLibraryTmdbIdsAsync(JellyfinConnection connection, CancellationToken cancellationToken);
}

public sealed record JellyfinConnection(string BaseUrl, string ApiKey);

public sealed record JellyfinSystemInfo(string Id, string ServerName, string Version);

public sealed record JellyfinConnectionTestResult(bool Ok, string? Message);
