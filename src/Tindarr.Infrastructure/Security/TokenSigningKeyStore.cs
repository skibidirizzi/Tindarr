using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using Tindarr.Application.Abstractions.Security;
using Tindarr.Application.Options;

namespace Tindarr.Infrastructure.Security;

public sealed class DbOrFileTokenSigningKeyStore : ITokenSigningKeyStore
{
	private static string WriteLockName => OperatingSystem.IsWindows() ? "Global\\Tindarr.SigningKeyStore" : "Tindarr.SigningKeyStore";

	private readonly string filePath;
	private readonly object sync = new();

	private PersistedSigningKeys? cached;
	private IReadOnlyCollection<SigningKey>? cachedKeys;

	public DbOrFileTokenSigningKeyStore(
		Microsoft.Extensions.Options.IOptions<JwtOptions> jwtOptions,
		string? signingKeysDirectoryOverride = null)
	{
		var opts = jwtOptions.Value;
		filePath = ResolveFilePath(opts.SigningKeysFileName, signingKeysDirectoryOverride);
	}

	public SigningKey GetActiveSigningKey()
	{
		EnsureLoaded();
		return cachedKeys!.First(k => k.KeyId == cached!.ActiveKeyId);
	}

	public IReadOnlyCollection<SigningKey> GetAllSigningKeys()
	{
		EnsureLoaded();
		return cachedKeys!;
	}

	public string GetActiveKeyId()
	{
		EnsureLoaded();
		return cached!.ActiveKeyId;
	}

	private void EnsureLoaded()
	{
		if (cached is not null && cachedKeys is not null)
		{
			return;
		}

		lock (sync)
		{
			if (cached is not null && cachedKeys is not null)
			{
				return;
			}

			var persisted = LoadOrCreate();
			cached = persisted;
			cachedKeys = persisted.Keys
				.Select(k => new SigningKey(k.KeyId, Convert.FromBase64String(k.KeyMaterialBase64)))
				.ToList();
		}
	}

	private PersistedSigningKeys LoadOrCreate()
	{
		var dir = Path.GetDirectoryName(filePath)!;
		Directory.CreateDirectory(dir);

		// Read with shared read so multiple processes can read; no write lock yet.
		if (File.Exists(filePath))
		{
			try
			{
				using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
				using var reader = new StreamReader(fs);
				var json = reader.ReadToEnd();
				var loaded = JsonSerializer.Deserialize<PersistedSigningKeys>(json);
				if (loaded is not null && loaded.Keys.Count > 0 && !string.IsNullOrWhiteSpace(loaded.ActiveKeyId))
				{
					return loaded;
				}
			}
			catch (IOException)
			{
				// File may be locked by writer or missing; fall through to create.
			}
		}

		// Cross-process write lock so only one process creates/updates the file (issue 111).
		using var mutex = new Mutex(false, WriteLockName);
		try
		{
			if (!mutex.WaitOne(TimeSpan.FromSeconds(30)))
			{
				throw new InvalidOperationException("Could not acquire signing key store write lock within 30 seconds.");
			}
		}
		catch (AbandonedMutexException)
		{
			// Previous holder exited without releasing; we own the mutex.
		}

		try
		{
			// Double-check after acquiring lock: another process may have created the file.
			if (File.Exists(filePath))
			{
				using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
				using var reader = new StreamReader(fs);
				var json = reader.ReadToEnd();
				var loaded = JsonSerializer.Deserialize<PersistedSigningKeys>(json);
				if (loaded is not null && loaded.Keys.Count > 0 && !string.IsNullOrWhiteSpace(loaded.ActiveKeyId))
				{
					return loaded;
				}
			}

			var now = DateTimeOffset.UtcNow;
			var keyId = "k1";
			var keyMaterial = RandomNumberGenerator.GetBytes(32);
			var created = new PersistedSigningKeys
			{
				ActiveKeyId = keyId,
				Keys =
				[
					new PersistedSigningKey
					{
						KeyId = keyId,
						KeyMaterialBase64 = Convert.ToBase64String(keyMaterial),
						CreatedAtUtc = now
					}
				]
			};

			var serialized = JsonSerializer.Serialize(created, new JsonSerializerOptions { WriteIndented = true });
			using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
			{
				using var writer = new StreamWriter(fs);
				writer.Write(serialized);
			}

			RestrictFilePermissions(filePath);
			return created;
		}
		finally
		{
			mutex.ReleaseMutex();
		}
	}

	/// <summary>
	/// Restrict file to current user only (issue 111). On Unix sets mode 600; no-op on Windows.
	/// </summary>
	private static void RestrictFilePermissions(string path)
	{
		if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
		{
			return;
		}

		try
		{
			File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
		}
		catch (InvalidOperationException)
		{
			// Filesystem may not support Unix file modes; ignore.
		}
	}

	private static string ResolveFilePath(string fileNameOrPath, string? directoryOverride = null)
	{
		var trimmed = fileNameOrPath.Trim();
		if (Path.IsPathRooted(trimmed))
		{
			return trimmed;
		}

		var baseDir = !string.IsNullOrWhiteSpace(directoryOverride)
			? directoryOverride
			: AppContext.BaseDirectory;
		return Path.Combine(baseDir, trimmed);
	}

	private sealed class PersistedSigningKeys
	{
		public string ActiveKeyId { get; set; } = "";
		public List<PersistedSigningKey> Keys { get; set; } = new();
	}

	private sealed class PersistedSigningKey
	{
		public string KeyId { get; set; } = "";
		public string KeyMaterialBase64 { get; set; } = "";
		public DateTimeOffset CreatedAtUtc { get; set; }
	}
}

