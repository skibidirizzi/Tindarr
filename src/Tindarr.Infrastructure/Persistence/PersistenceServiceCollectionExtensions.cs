using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tindarr.Application.Options;

namespace Tindarr.Infrastructure.Persistence;

public static class PersistenceServiceCollectionExtensions
{
	public static IServiceCollection AddTindarrPersistence(
		this IServiceCollection services,
		IConfiguration configuration,
		string? overrideDataDir = null)
	{
		var dbOptions = configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>() ?? new DatabaseOptions();

		var dataDir = ResolveDataDir(dbOptions, overrideDataDir);
		Directory.CreateDirectory(dataDir);

		var dbPath = ResolveSqlitePath(dbOptions, dataDir);
		var connectionString = new SqliteConnectionStringBuilder
		{
			DataSource = dbPath,
			Mode = SqliteOpenMode.ReadWriteCreate
		}.ToString();

		var migrationsAssembly = typeof(TindarrDbContext).Assembly.GetName().Name;

		services.AddDbContext<TindarrDbContext>(options =>
		{
			options.UseSqlite(connectionString, sqlite =>
			{
				if (!string.IsNullOrWhiteSpace(migrationsAssembly))
				{
					sqlite.MigrationsAssembly(migrationsAssembly);
				}
			});

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

		// Default: keep DB next to the running host (dev-friendly; service mode should override).
		return AppContext.BaseDirectory;
	}

	private static string ResolveSqlitePath(DatabaseOptions options, string dataDir)
	{
		// Allow providing full absolute path via SqliteFileName.
		if (Path.IsPathRooted(options.SqliteFileName))
		{
			return options.SqliteFileName;
		}

		return Path.Combine(dataDir, options.SqliteFileName);
	}
}

