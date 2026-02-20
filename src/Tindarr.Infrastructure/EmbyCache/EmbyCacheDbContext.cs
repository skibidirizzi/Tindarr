using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Tindarr.Infrastructure.EmbyCache.Entities;

namespace Tindarr.Infrastructure.EmbyCache;

public sealed class EmbyCacheDbContext(DbContextOptions<EmbyCacheDbContext> options) : DbContext(options)
{
	public DbSet<EmbyLibraryCacheItemEntity> LibraryItems => Set<EmbyLibraryCacheItemEntity>();

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		base.OnModelCreating(modelBuilder);

		var utcDateTimeOffsetStringConverter = new ValueConverter<DateTimeOffset, string>(
			v => v.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
			v => DateTimeOffset.Parse(v, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));

		modelBuilder.Entity<EmbyLibraryCacheItemEntity>(builder =>
		{
			builder.ToTable("EmbyLibraryCache");
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
