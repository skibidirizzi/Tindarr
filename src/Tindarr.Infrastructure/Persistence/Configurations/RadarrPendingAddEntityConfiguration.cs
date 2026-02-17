using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tindarr.Infrastructure.Persistence.Entities;

namespace Tindarr.Infrastructure.Persistence.Configurations;

public sealed class RadarrPendingAddEntityConfiguration : IEntityTypeConfiguration<RadarrPendingAddEntity>
{
	public void Configure(EntityTypeBuilder<RadarrPendingAddEntity> builder)
	{
		builder.ToTable("radarr_pending_adds");

		builder.HasKey(x => x.Id);
		builder.Property(x => x.Id).ValueGeneratedOnAdd();

		builder.Property(x => x.ServiceType).IsRequired();
		builder.Property(x => x.ServerId).IsRequired();
		builder.Property(x => x.UserId).IsRequired();
		builder.Property(x => x.TmdbId).IsRequired();

		builder.Property(x => x.ReadyAtUtc).IsRequired();
		builder.Property(x => x.CreatedAtUtc).IsRequired();
		builder.Property(x => x.CanceledAtUtc);
		builder.Property(x => x.ProcessedAtUtc);
		builder.Property(x => x.AttemptCount).IsRequired();
		builder.Property(x => x.LastError);

		builder.HasIndex(x => new { x.ServiceType, x.ServerId, x.ReadyAtUtc });
		builder.HasIndex(x => new { x.ServiceType, x.ServerId, x.UserId, x.TmdbId });
	}
}
