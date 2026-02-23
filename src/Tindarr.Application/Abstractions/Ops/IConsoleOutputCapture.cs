namespace Tindarr.Application.Abstractions.Ops;

/// <summary>
/// Provides recent console (stdout/stderr) output for admin display.
/// </summary>
public interface IConsoleOutputCapture
{
	/// <summary>
	/// Returns the most recent console lines, newest last. Up to <paramref name="maxLines"/>.
	/// </summary>
	IReadOnlyList<string> GetRecentLines(int maxLines = 500);
}
