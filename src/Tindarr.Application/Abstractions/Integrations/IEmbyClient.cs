namespace Tindarr.Application.Abstractions.Integrations;

public interface IEmbyClient
{
	Task<EmbyConnectionTestResult> TestConnectionAsync(EmbyConnection connection, CancellationToken cancellationToken);

	Task<EmbySystemInfo> GetSystemInfoAsync(EmbyConnection connection, CancellationToken cancellationToken);

	Task<IReadOnlyList<int>> GetLibraryTmdbIdsAsync(EmbyConnection connection, CancellationToken cancellationToken);
}

public sealed record EmbyConnection(string BaseUrl, string ApiKey);

public sealed record EmbySystemInfo(string Id, string ServerName, string Version);

public sealed record EmbyConnectionTestResult(bool Ok, string? Message);
