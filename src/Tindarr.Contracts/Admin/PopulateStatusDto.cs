namespace Tindarr.Contracts.Admin;

/// <summary>
/// Progress of the Admin DB "Populate" (fetch-all-details and fetch-all-images) job.
/// </summary>
public sealed record PopulateStatusDto(
	string State,
	int DetailsTotal,
	int DetailsDone,
	int ImagesTotal,
	int ImagesDone,
	string? LastMessage);
