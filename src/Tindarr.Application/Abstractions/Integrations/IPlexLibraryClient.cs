namespace Tindarr.Application.Abstractions.Integrations;

public interface IPlexLibraryClient
{
	Task<IReadOnlyList<PlexLibrarySection>> GetMovieSectionsAsync(PlexServerConnectionInfo connection, CancellationToken cancellationToken);

	Task<IReadOnlyList<PlexLibraryItem>> GetLibraryItemsAsync(PlexServerConnectionInfo connection, int sectionKey, CancellationToken cancellationToken);
}

public sealed record PlexServerConnectionInfo(
	string BaseUrl,
	string AccessToken,
	string ClientIdentifier);

public sealed record PlexLibrarySection(
	int Key,
	string Title,
	string Type);

public sealed record PlexLibraryItem(
	int TmdbId,
	string Title,
	string? RatingKey,
	string? Guid);
