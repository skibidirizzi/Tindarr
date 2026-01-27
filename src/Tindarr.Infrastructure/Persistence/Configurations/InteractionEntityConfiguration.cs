using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tindarr.Infrastructure.Persistence.Entities;

namespace Tindarr.Infrastructure.Persistence.Configurations;

public sealed class InteractionEntityConfiguration : IEntityTypeConfiguration<InteractionEntity>
{
	public void Configure(EntityTypeBuilder<InteractionEntity> builder)
	{
		builder.ToTable("interactions");

		builder.HasKey(x => x.Id);
		builder.Property(x => x.Id).ValueGeneratedOnAdd();

		builder.Property(x => x.UserId).IsRequired();
		builder.Property(x => x.ServiceType).IsRequired();
		builder.Property(x => x.ServerId).IsRequired();

		builder.Property(x => x.TmdbId).IsRequired();
		builder.Property(x => x.Action).IsRequired();
		builder.Property(x => x.CreatedAtUtc).IsRequired();

		// Enforce service scoping for queries + ensure fast "seen" lookups.
		builder.HasIndex(x => new { x.UserId, x.ServiceType, x.ServerId, x.TmdbId });
		builder.HasIndex(x => new { x.UserId, x.ServiceType, x.ServerId, x.CreatedAtUtc });
	}
}

