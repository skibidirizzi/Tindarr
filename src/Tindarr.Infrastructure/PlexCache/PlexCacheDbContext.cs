using Microsoft.EntityFrameworkCore;
using Tindarr.Infrastructure.PlexCache.Entities;

namespace Tindarr.Infrastructure.PlexCache;

public sealed class PlexCacheDbContext(DbContextOptions<PlexCacheDbContext> options) : DbContext(options)
{
	public DbSet<PlexLibraryCacheItemEntity> LibraryItems => Set<PlexLibraryCacheItemEntity>();

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		base.OnModelCreating(modelBuilder);

		modelBuilder.Entity<PlexLibraryCacheItemEntity>(builder =>
		{
			builder.ToTable("PlexLibraryCache");
			builder.HasKey(x => x.Id);
			builder.Property(x => x.ServerId).IsRequired();
			builder.HasIndex(x => new { x.ServerId, x.TmdbId }).IsUnique();
			builder.Property(x => x.UpdatedAtUtc).IsRequired();
		});
	}
}
