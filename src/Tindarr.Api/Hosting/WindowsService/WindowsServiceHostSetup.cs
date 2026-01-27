namespace Tindarr.Api.Hosting.WindowsService;

public static class WindowsServiceHostSetup
{
	public static bool IsRunningAsWindowsService()
	{
		// WindowsServiceHelpers.IsWindowsService() can fail in some hosting edge-cases.
		// Environment.UserInteractive is a reliable signal for services launched by SCM.
		return OperatingSystem.IsWindows() && !Environment.UserInteractive;
	}

	public static string GetDefaultDataDir(string appFolderName = "Tindarr")
	{
		var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
		return Path.Combine(programData, appFolderName);
	}

	public static string ResolveDataDir(WindowsServiceOptions options)
	{
		var dir = string.IsNullOrWhiteSpace(options.DataDir) ? GetDefaultDataDir() : options.DataDir.Trim();
		return Path.GetFullPath(dir);
	}

	public static string ResolveSourceWebRoot(string contentRoot, WindowsServiceOptions options)
	{
		var subdir = string.IsNullOrWhiteSpace(options.SourceWebRootSubdir) ? "wwwroot" : options.SourceWebRootSubdir.Trim();
		return Path.Combine(contentRoot, subdir);
	}

	public static string ResolveTargetWebRoot(string dataDir, WindowsServiceOptions options)
	{
		var subdir = string.IsNullOrWhiteSpace(options.WebRootSubdir) ? "wwwroot" : options.WebRootSubdir.Trim();
		return Path.Combine(dataDir, subdir);
	}
}

