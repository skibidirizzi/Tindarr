using System.Security.Cryptography;
using System.Text.Json;
using Tindarr.Application.Abstractions.Security;
using Tindarr.Application.Options;

namespace Tindarr.Infrastructure.Security;

public sealed class DbOrFileTokenSigningKeyStore : ITokenSigningKeyStore
{
	private readonly string filePath;
	private readonly object sync = new();

	private PersistedSigningKeys? cached;
	private IReadOnlyCollection<SigningKey>? cachedKeys;

	public DbOrFileTokenSigningKeyStore(Microsoft.Extensions.Options.IOptions<JwtOptions> jwtOptions)
	{
		var opts = jwtOptions.Value;
		filePath = ResolveFilePath(opts.SigningKeysFileName);
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
		Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

		if (File.Exists(filePath))
		{
			var json = File.ReadAllText(filePath);
			var loaded = JsonSerializer.Deserialize<PersistedSigningKeys>(json);
			if (loaded is not null && loaded.Keys.Count > 0 && !string.IsNullOrWhiteSpace(loaded.ActiveKeyId))
			{
				return loaded;
			}
		}

		// Create a new signing key set.
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
		File.WriteAllText(filePath, serialized);
		return created;
	}

	private static string ResolveFilePath(string fileNameOrPath)
	{
		var trimmed = fileNameOrPath.Trim();
		if (Path.IsPathRooted(trimmed))
		{
			return trimmed;
		}

		return Path.Combine(AppContext.BaseDirectory, trimmed);
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

