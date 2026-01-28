using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tindarr.Infrastructure.Persistence.Entities;

namespace Tindarr.Infrastructure.Persistence.Configurations;

public sealed class LibraryCacheEntityConfiguration : IEntityTypeConfiguration<LibraryCacheEntity>
{
	public void Configure(EntityTypeBuilder<LibraryCacheEntity> builder)
	{
		builder.ToTable("library_cache");

		builder.HasKey(x => x.Id);
		builder.Property(x => x.Id).ValueGeneratedOnAdd();

		builder.Property(x => x.ServiceType).IsRequired();
		builder.Property(x => x.ServerId).IsRequired();
		builder.Property(x => x.TmdbId).IsRequired();
		builder.Property(x => x.SyncedAtUtc).IsRequired();

		builder.HasIndex(x => new { x.ServiceType, x.ServerId, x.TmdbId }).IsUnique();
		builder.HasIndex(x => new { x.ServiceType, x.ServerId });
	}
}
