using Microsoft.Extensions.Caching.Memory;
using Tindarr.Infrastructure.Caching;

namespace Tindarr.UnitTests.Infrastructure.Caching;

public sealed class MemoryOrDbTmdbCacheTests
{
	[Fact]
	public async Task MaxRows_can_be_set_and_read_back()
	{
		var dir = Path.Combine(Path.GetTempPath(), "tindarr-tests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(dir);
		var dbPath = Path.Combine(dir, "tmdbmetadata.db");

		var mem = new MemoryCache(new MemoryCacheOptions());
		var cache = new MemoryOrDbTmdbCache(mem, dbPath);

		var initial = await cache.GetMaxRowsAsync(CancellationToken.None);
		Assert.True(initial >= 200);

		await cache.SetMaxRowsAsync(1234, CancellationToken.None);
		var updated = await cache.GetMaxRowsAsync(CancellationToken.None);
		Assert.Equal(1234, updated);
	}

	[Fact]
	public async Task MaxRows_is_clamped_to_minimum()
	{
		var dir = Path.Combine(Path.GetTempPath(), "tindarr-tests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(dir);
		var dbPath = Path.Combine(dir, "tmdbmetadata.db");

		var mem = new MemoryCache(new MemoryCacheOptions());
		var cache = new MemoryOrDbTmdbCache(mem, dbPath);

		await cache.SetMaxRowsAsync(1, CancellationToken.None);
		var updated = await cache.GetMaxRowsAsync(CancellationToken.None);
		Assert.True(updated >= 200);
	}
}
