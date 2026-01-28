namespace Tindarr.Application.Abstractions.Integrations;

public interface IRadarrClient
{
	Task<RadarrConnectionTestResult> TestConnectionAsync(RadarrConnection connection, CancellationToken cancellationToken);

	Task<IReadOnlyList<RadarrQualityProfile>> GetQualityProfilesAsync(RadarrConnection connection, CancellationToken cancellationToken);

	Task<IReadOnlyList<RadarrRootFolder>> GetRootFoldersAsync(RadarrConnection connection, CancellationToken cancellationToken);

	Task<IReadOnlyList<RadarrLibraryMovie>> GetLibraryAsync(RadarrConnection connection, CancellationToken cancellationToken);

	Task<RadarrLookupMovie?> LookupMovieAsync(RadarrConnection connection, int tmdbId, CancellationToken cancellationToken);

	Task<int?> EnsureTagAsync(RadarrConnection connection, string tagLabel, CancellationToken cancellationToken);

	Task<RadarrAddMovieResult> AddMovieAsync(RadarrConnection connection, RadarrAddMovieRequest request, CancellationToken cancellationToken);
}

public sealed record RadarrConnection(string BaseUrl, string ApiKey);

public sealed record RadarrQualityProfile(int Id, string Name);

public sealed record RadarrRootFolder(int Id, string Path, long? FreeSpaceBytes);

public sealed record RadarrLibraryMovie(int TmdbId, int RadarrId, string Title);

public sealed record RadarrLookupMovie(
	int TmdbId,
	string Title,
	string? TitleSlug,
	int? Year,
	IReadOnlyList<RadarrLookupImage> Images);

public sealed record RadarrLookupImage(string CoverType, string? Url, string? RemoteUrl);

public sealed record RadarrAddMovieRequest(
	RadarrLookupMovie Lookup,
	int QualityProfileId,
	string RootFolderPath,
	IReadOnlyList<int> TagIds,
	bool SearchForMovie);

public sealed record RadarrAddMovieResult(bool Added, bool AlreadyExists, string? Message);

public sealed record RadarrConnectionTestResult(bool Ok, string? Message);
