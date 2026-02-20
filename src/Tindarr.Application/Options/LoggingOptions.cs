namespace Tindarr.Application.Options;

public sealed class LoggingOptions
{
	public const string SectionName = "Logging";

	/// <summary>
	/// When true, request logging middleware will log request headers (redacted).
	/// </summary>
	public bool LogRequestHeaders { get; init; } = true;

	/// <summary>
	/// When true, request logging middleware will log response headers (redacted).
	/// </summary>
	public bool LogResponseHeaders { get; init; } = false;

	public bool IsValid() => true;
}

