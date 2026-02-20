using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Tindarr.Infrastructure.JellyfinCache.Entities;

namespace Tindarr.Infrastructure.JellyfinCache;

public sealed class JellyfinCacheDbContext(DbContextOptions<JellyfinCacheDbContext> options) : DbContext(options)
{
	public DbSet<JellyfinLibraryCacheItemEntity> LibraryItems => Set<JellyfinLibraryCacheItemEntity>();

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		base.OnModelCreating(modelBuilder);

		var utcDateTimeOffsetStringConverter = new ValueConverter<DateTimeOffset, string>(
			v => v.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
			v => DateTimeOffset.Parse(v, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));

		modelBuilder.Entity<JellyfinLibraryCacheItemEntity>(builder =>
		{
			builder.ToTable("JellyfinLibraryCache");
			builder.HasKey(x => x.Id);
			builder.Property(x => x.ServerId).IsRequired();
			builder.HasIndex(x => new { x.ServerId, x.TmdbId }).IsUnique();
			builder.Property(x => x.UpdatedAtUtc)
				.IsRequired()
				.HasConversion(utcDateTimeOffsetStringConverter)
				.HasColumnType("TEXT");
		});
	}
}
