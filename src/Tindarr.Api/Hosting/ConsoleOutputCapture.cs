using System.Text;
using Tindarr.Application.Abstractions.Ops;

namespace Tindarr.Api.Hosting;

/// <summary>
/// Captures stdout/stderr into a ring buffer and exposes it for admin console mirroring.
/// Call <see cref="Install"/> early in host startup (before builder) so console output is captured.
/// </summary>
public sealed class ConsoleOutputCapture : IConsoleOutputCapture
{
	private const int MaxLines = 2000;
	private readonly List<string> _lines = [];
	private readonly object _linesLock = new();

	public IReadOnlyList<string> GetRecentLines(int maxLines = 500)
	{
		lock (_linesLock)
		{
			var count = Math.Min(maxLines, _lines.Count);
			if (count <= 0) return [];
			return _lines.TakeLast(count).ToList();
		}
	}

	internal void AppendLine(string line)
	{
		if (string.IsNullOrEmpty(line)) return;
		lock (_linesLock)
		{
			_lines.Add(line);
			if (_lines.Count > MaxLines)
				_lines.RemoveAt(0);
		}
	}

	/// <summary>
	/// Redirects Console.Out and Console.Error to teed writers that also append to this capture.
	/// Call once at startup before any console output.
	/// </summary>
	public void Install()
	{
		var outWriter = new TeeTextWriter(Console.Out, AppendLine);
		var errWriter = new TeeTextWriter(Console.Error, AppendLine);
		Console.SetOut(outWriter);
		Console.SetError(errWriter);
	}

	private sealed class TeeTextWriter : TextWriter
	{
		private readonly TextWriter _inner;
		private readonly Action<string> _onLine;
		private readonly StringBuilder _buffer = new();

		public override Encoding Encoding => _inner.Encoding;

		public TeeTextWriter(TextWriter inner, Action<string> onLine)
		{
			_inner = inner;
			_onLine = onLine;
		}

		public override void Write(char value)
		{
			_inner.Write(value);
			if (value == '\n')
				FlushLine();
			else if (value != '\r')
				_buffer.Append(value);
		}

		public override void Write(string? value)
		{
			if (value is null) return;
			_inner.Write(value);
			foreach (var c in value)
			{
				if (c == '\n')
					FlushLine();
				else if (c != '\r')
					_buffer.Append(c);
			}
		}

		public override void WriteLine(string? value)
		{
			_inner.WriteLine(value);
			_buffer.Append(value);
			FlushLine();
		}

		private void FlushLine()
		{
			var line = _buffer.ToString().TrimEnd('\r', '\n');
			_buffer.Clear();
			if (line.Length > 0)
				_onLine(line);
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing && _buffer.Length > 0)
				FlushLine();
			base.Dispose(disposing);
		}
	}
}
