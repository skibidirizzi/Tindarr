namespace Tindarr.Contracts.Setup;

public sealed record SetupCompleteRequest(
	bool RunLibrarySync,
	bool RunTmdbBuild,
	bool RunFetchAllDetails = false,
	bool RunFetchAllImages = false);
