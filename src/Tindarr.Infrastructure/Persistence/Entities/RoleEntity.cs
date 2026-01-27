namespace Tindarr.Infrastructure.Persistence.Entities;

public sealed class RoleEntity
{
	public string Name { get; set; } = "";

	public DateTimeOffset CreatedAtUtc { get; set; }

	public List<UserRoleEntity> UserRoles { get; set; } = new();
}

