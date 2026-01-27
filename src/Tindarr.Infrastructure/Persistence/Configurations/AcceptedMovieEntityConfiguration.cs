using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tindarr.Infrastructure.Persistence.Entities;

namespace Tindarr.Infrastructure.Persistence.Configurations;

public sealed class AcceptedMovieEntityConfiguration : IEntityTypeConfiguration<AcceptedMovieEntity>
{
	public void Configure(EntityTypeBuilder<AcceptedMovieEntity> builder)
	{
		builder.ToTable("accepted_movies");

		builder.HasKey(x => x.Id);
		builder.Property(x => x.Id).ValueGeneratedOnAdd();

		builder.Property(x => x.ServiceType).IsRequired();
		builder.Property(x => x.ServerId).IsRequired();
		builder.Property(x => x.TmdbId).IsRequired();
		builder.Property(x => x.AcceptedByUserId);
		builder.Property(x => x.AcceptedAtUtc).IsRequired();

		// Accepted movies MUST be stored per (ServiceType, ServerId) scope.
		builder.HasIndex(x => new { x.ServiceType, x.ServerId, x.TmdbId }).IsUnique();
		builder.HasIndex(x => new { x.ServiceType, x.ServerId, x.AcceptedAtUtc });
	}
}

