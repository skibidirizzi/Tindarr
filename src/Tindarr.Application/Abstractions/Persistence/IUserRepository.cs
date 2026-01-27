namespace Tindarr.Application.Abstractions.Persistence;

public interface IUserRepository
{
	Task<UserRecord?> FindByIdAsync(string userId, CancellationToken cancellationToken);

	Task<IReadOnlyCollection<string>> GetRolesAsync(string userId, CancellationToken cancellationToken);

	Task<PasswordCredentialRecord?> GetPasswordCredentialAsync(string userId, CancellationToken cancellationToken);

	Task<bool> UserExistsAsync(string userId, CancellationToken cancellationToken);

	Task<IReadOnlyCollection<UserRecord>> ListAsync(int skip, int take, CancellationToken cancellationToken);

	Task CreateAsync(CreateUserRecord user, CancellationToken cancellationToken);

	Task UpdateDisplayNameAsync(string userId, string displayName, CancellationToken cancellationToken);

	Task SetPasswordAsync(string userId, byte[] passwordHash, byte[] passwordSalt, int passwordIterations, CancellationToken cancellationToken);

	Task SetRolesAsync(string userId, IReadOnlyCollection<string> roles, CancellationToken cancellationToken);

	Task DeleteAsync(string userId, CancellationToken cancellationToken);
}

public sealed record UserRecord(
	string Id,
	string DisplayName,
	DateTimeOffset CreatedAtUtc,
	bool HasPassword);

public sealed record CreateUserRecord(
	string Id,
	string DisplayName,
	DateTimeOffset CreatedAtUtc);

public sealed record PasswordCredentialRecord(
	byte[] PasswordHash,
	byte[] PasswordSalt,
	int PasswordIterations);

