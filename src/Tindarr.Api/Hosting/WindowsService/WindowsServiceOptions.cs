namespace Tindarr.Api.Hosting.WindowsService;

public sealed class WindowsServiceOptions
{
	public const string SectionName = "WindowsService";

	/// <summary>
	/// Service name to register with SCM (informational; does not auto-register).
	/// </summary>
	public string ServiceName { get; init; } = "Tindarr.Api";

	/// <summary>
	/// Persistent data directory (ProgramData by default when running as a service).
	/// </summary>
	public string? DataDir { get; init; }

	/// <summary>
	/// If true, the app will serve static files from DataDir\WebRootSubdir when running as a service.
	/// </summary>
	public bool UseDataDirWebRoot { get; init; } = true;

	/// <summary>
	/// If true, mirror installed webroot (ContentRoot\SourceWebRootSubdir) into the target webroot on startup.
	/// </summary>
	public bool MirrorWebRootOnStart { get; init; } = true;

	/// <summary>
	/// Subdirectory under ContentRoot where installed web assets live.
	/// </summary>
	public string SourceWebRootSubdir { get; init; } = "wwwroot";

	/// <summary>
	/// Subdirectory under DataDir used as the service web root.
	/// </summary>
	public string WebRootSubdir { get; init; } = "wwwroot";
}

