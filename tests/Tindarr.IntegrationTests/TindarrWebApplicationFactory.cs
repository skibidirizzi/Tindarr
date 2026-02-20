using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Tindarr.IntegrationTests;

/// <summary>
/// Web application factory for integration tests. Uses Testing environment and an isolated DB per factory instance.
/// </summary>
public sealed class TindarrWebApplicationFactory : WebApplicationFactory<Program>
{
	protected override void ConfigureWebHost(IWebHostBuilder builder)
	{
		builder.UseEnvironment("Testing");

		// Unique directory per factory so main DB and Plex/Jellyfin/Emby cache DBs do not conflict across test classes.
		var uniqueDir = Path.Combine(Path.GetTempPath(), $"tindarr_integration_{Guid.NewGuid():N}");
		builder.ConfigureAppConfiguration((_, config) =>
		{
			config.AddInMemoryCollection(new Dictionary<string, string?>
			{
				["Database:DataDir"] = uniqueDir,
				["Database:SqliteFileName"] = "tindarr.db",
				["Database:UseConfigDataDir"] = "true"
			});
		});
	}
}
