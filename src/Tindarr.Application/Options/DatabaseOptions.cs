namespace Tindarr.Application.Options;

public sealed class DatabaseOptions
{
	public const string SectionName = "Database";

	// Only sqlite is supported currently (per di_map.yaml).
	public string Provider { get; init; } = "Sqlite";

	// Optional override. If empty, hosts decide a sensible default (or override in service mode).
	public string DataDir { get; init; } = "";

	// Can be a filename (resolved relative to DataDir) or an absolute path.
	public string SqliteFileName { get; init; } = "tindarr.db";

	public bool EnableSensitiveDataLogging { get; init; } = false;
	public bool EnableDetailedErrors { get; init; } = false;

	public bool IsValid()
	{
		if (!Provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		return !string.IsNullOrWhiteSpace(SqliteFileName);
	}
}

