using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tindarr.Application.Options;

namespace Tindarr.Infrastructure.EmbyCache;

public static class EmbyCacheServiceCollectionExtensions
{
	public static IServiceCollection AddEmbyCache(
		this IServiceCollection services,
		IConfiguration configuration,
		string? overrideDataDir = null)
	{
		var dbOptions = configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>() ?? new DatabaseOptions();
		var dataDir = ResolveDataDir(dbOptions, overrideDataDir);
		Directory.CreateDirectory(dataDir);

		var dbPath = Path.Combine(dataDir, "embycache.db");
		var connectionString = new SqliteConnectionStringBuilder
		{
			DataSource = dbPath,
			Mode = SqliteOpenMode.ReadWriteCreate
		}.ToString();

		services.AddDbContext<EmbyCacheDbContext>(options =>
		{
			options.UseSqlite(connectionString);

			if (dbOptions.EnableDetailedErrors)
			{
				options.EnableDetailedErrors();
			}

			if (dbOptions.EnableSensitiveDataLogging)
			{
				options.EnableSensitiveDataLogging();
			}
		});

		return services;
	}

	private static string ResolveDataDir(DatabaseOptions options, string? overrideDataDir)
	{
		if (!string.IsNullOrWhiteSpace(overrideDataDir))
		{
			return overrideDataDir;
		}

		if (!string.IsNullOrWhiteSpace(options.DataDir))
		{
			return options.DataDir;
		}

		return AppContext.BaseDirectory;
	}
}
