namespace Tindarr.Infrastructure.Persistence.Entities;

public sealed class UserEntity
{
	public string Id { get; set; } = "";

	public string DisplayName { get; set; } = "";

	// Auth is not wired yet, but we store the columns now so we don't need a schema rewrite later.
	public byte[]? PasswordHash { get; set; }
	public byte[]? PasswordSalt { get; set; }
	public int? PasswordIterations { get; set; }

	public DateTimeOffset CreatedAtUtc { get; set; }

	public List<UserRoleEntity> UserRoles { get; set; } = new();
	public UserPreferencesEntity? Preferences { get; set; }
}

