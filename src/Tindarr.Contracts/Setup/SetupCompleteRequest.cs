namespace Tindarr.Contracts.Setup;

public sealed record SetupCompleteRequest(
	bool RunLibrarySync,
	bool RunTmdbBuild,
	bool RunFetchAllDetails = true, // Required: true by default so fetch-all-details runs on setup complete.
	bool RunFetchAllImages = false);
