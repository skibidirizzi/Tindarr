namespace Tindarr.Application.Options;

public sealed class LoggingOptions
{
	public const string SectionName = "Logging";

	/// <summary>
	/// When true, request logging middleware will log request headers (redacted).
	/// </summary>
	public bool LogRequestHeaders { get; init; } = true;

	public bool IsValid() => true;
}

