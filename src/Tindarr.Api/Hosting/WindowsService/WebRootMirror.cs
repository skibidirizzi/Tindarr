namespace Tindarr.Api.Hosting.WindowsService;

public static class WebRootMirror
{
	public static void MirrorDirectory(string sourceDir, string targetDir, ILogger logger)
	{
		if (!Directory.Exists(sourceDir))
		{
			logger.LogInformation("Webroot mirror skipped: source missing. Source={Source}", sourceDir);
			return;
		}

		Directory.CreateDirectory(targetDir);

		foreach (var sourcePath in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
		{
			var relative = Path.GetRelativePath(sourceDir, sourcePath);
			var targetPath = Path.Combine(targetDir, relative);

			Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

			if (ShouldCopy(sourcePath, targetPath))
			{
				File.Copy(sourcePath, targetPath, overwrite: true);
				File.SetLastWriteTimeUtc(targetPath, File.GetLastWriteTimeUtc(sourcePath));
			}
		}

		logger.LogInformation("Webroot mirrored. Source={Source} Target={Target}", sourceDir, targetDir);
	}

	private static bool ShouldCopy(string sourcePath, string targetPath)
	{
		if (!File.Exists(targetPath))
		{
			return true;
		}

		var srcInfo = new FileInfo(sourcePath);
		var dstInfo = new FileInfo(targetPath);

		if (srcInfo.Length != dstInfo.Length)
		{
			return true;
		}

		// Copy if source is newer (or clocks differ slightly).
		return srcInfo.LastWriteTimeUtc > dstInfo.LastWriteTimeUtc.AddSeconds(1);
	}
}

