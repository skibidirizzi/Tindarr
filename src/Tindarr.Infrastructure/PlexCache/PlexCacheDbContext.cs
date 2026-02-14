using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Tindarr.Infrastructure.PlexCache.Entities;

namespace Tindarr.Infrastructure.PlexCache;

public sealed class PlexCacheDbContext(DbContextOptions<PlexCacheDbContext> options) : DbContext(options)
{
	public DbSet<PlexLibraryCacheItemEntity> LibraryItems => Set<PlexLibraryCacheItemEntity>();
	public DbSet<PlexDeckEntryEntity> DeckEntries => Set<PlexDeckEntryEntity>();
	public DbSet<PlexDeckGenreEntity> DeckGenres => Set<PlexDeckGenreEntity>();
	public DbSet<PlexDeckRegionEntity> DeckRegions => Set<PlexDeckRegionEntity>();

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		base.OnModelCreating(modelBuilder);

		// SQLite can't translate ORDER BY over DateTimeOffset. Store as a UTC round-trip string instead.
		var utcDateTimeOffsetStringConverter = new ValueConverter<DateTimeOffset, string>(
			v => v.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
			v => DateTimeOffset.Parse(v, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));

		modelBuilder.Entity<PlexLibraryCacheItemEntity>(builder =>
		{
			builder.ToTable("PlexLibraryCache");
			builder.HasKey(x => x.Id);
			builder.Property(x => x.ServerId).IsRequired();
			builder.HasIndex(x => new { x.ServerId, x.TmdbId }).IsUnique();
			builder.Property(x => x.UpdatedAtUtc)
				.IsRequired()
				.HasConversion(utcDateTimeOffsetStringConverter)
				.HasColumnType("TEXT");
		});

		modelBuilder.Entity<PlexDeckEntryEntity>(builder =>
		{
			builder.ToTable("PlexDeckEntries");
			builder.HasKey(x => x.Id);
			builder.Property(x => x.ServerId).IsRequired();
			builder.Property(x => x.Title).IsRequired();
			builder.Property(x => x.UpdatedAtUtc)
				.IsRequired()
				.HasConversion(utcDateTimeOffsetStringConverter)
				.HasColumnType("TEXT");
			builder.HasIndex(x => new { x.ServerId, x.TmdbId }).IsUnique();
			builder.HasIndex(x => new { x.ServerId, x.ReleaseYear });
			builder.HasIndex(x => new { x.ServerId, x.Rating });
			builder.HasIndex(x => new { x.ServerId, x.OriginalLanguage });
			builder.HasIndex(x => new { x.ServerId, x.IsAdult });
		});

		modelBuilder.Entity<PlexDeckGenreEntity>(builder =>
		{
			builder.ToTable("PlexDeckGenres");
			builder.HasKey(x => x.Id);
			builder.Property(x => x.ServerId).IsRequired();
			builder.Property(x => x.Genre).IsRequired();
			builder.HasIndex(x => new { x.ServerId, x.TmdbId });
			builder.HasIndex(x => new { x.ServerId, x.Genre });
		});

		modelBuilder.Entity<PlexDeckRegionEntity>(builder =>
		{
			builder.ToTable("PlexDeckRegions");
			builder.HasKey(x => x.Id);
			builder.Property(x => x.ServerId).IsRequired();
			builder.Property(x => x.Region).IsRequired();
			builder.HasIndex(x => new { x.ServerId, x.TmdbId });
			builder.HasIndex(x => new { x.ServerId, x.Region });
		});
	}
}
