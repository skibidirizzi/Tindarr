namespace Tindarr.Infrastructure.Persistence.Entities;

public sealed class UserRoleEntity
{
	public string UserId { get; set; } = "";
	public UserEntity User { get; set; } = null!;

	public string RoleName { get; set; } = "";
	public RoleEntity Role { get; set; } = null!;
}

