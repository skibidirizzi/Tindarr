using Microsoft.EntityFrameworkCore;
using Tindarr.Infrastructure.Persistence.Entities;

namespace Tindarr.Infrastructure.Persistence;

public sealed class TindarrDbContext : DbContext
{
	public TindarrDbContext(DbContextOptions<TindarrDbContext> options)
		: base(options)
	{
	}

	public DbSet<UserEntity> Users => Set<UserEntity>();
	public DbSet<RoleEntity> Roles => Set<RoleEntity>();
	public DbSet<UserRoleEntity> UserRoles => Set<UserRoleEntity>();
	public DbSet<UserPreferencesEntity> UserPreferences => Set<UserPreferencesEntity>();
	public DbSet<InteractionEntity> Interactions => Set<InteractionEntity>();
	public DbSet<AcceptedMovieEntity> AcceptedMovies => Set<AcceptedMovieEntity>();
	public DbSet<ServiceSettingsEntity> ServiceSettings => Set<ServiceSettingsEntity>();
	public DbSet<LibraryCacheEntity> LibraryCache => Set<LibraryCacheEntity>();
	public DbSet<JoinAddressSettingsEntity> JoinAddressSettings => Set<JoinAddressSettingsEntity>();

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		// EntityTypeConfiguration<T> classes live in this assembly under Persistence/Configurations.
		modelBuilder.ApplyConfigurationsFromAssembly(typeof(TindarrDbContext).Assembly);
		base.OnModelCreating(modelBuilder);
	}
}
