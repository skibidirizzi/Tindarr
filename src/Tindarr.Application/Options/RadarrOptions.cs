namespace Tindarr.Application.Options;

public sealed class RadarrOptions
{
	public const string SectionName = "Radarr";

	public string DefaultTagLabel { get; init; } = "tindarr";

	public int LibrarySyncMinutes { get; init; } = 15;

	public int AutoAddMinutes { get; init; } = 2;

	public int AutoAddBatchSize { get; init; } = 50;

	public bool IsValid()
	{
		return LibrarySyncMinutes is >= 1 and <= 1440
			&& AutoAddMinutes is >= 1 and <= 1440
			&& AutoAddBatchSize is >= 1 and <= 500
			&& !string.IsNullOrWhiteSpace(DefaultTagLabel);
	}
}
