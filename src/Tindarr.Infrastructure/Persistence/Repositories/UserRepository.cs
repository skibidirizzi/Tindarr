using Microsoft.EntityFrameworkCore;
using Tindarr.Application.Abstractions.Persistence;
using Tindarr.Infrastructure.Persistence.Entities;

namespace Tindarr.Infrastructure.Persistence.Repositories;

public sealed class UserRepository(TindarrDbContext db) : IUserRepository
{
	public async Task<int> PurgeGuestUsersAsync(DateTimeOffset createdBeforeUtc, CancellationToken cancellationToken)
	{
		// NOTE: SQLite DateTimeOffset translation is intentionally limited in LINQ.
		// Use SQL with a subquery to keep this operation set-based and avoid parameter limits.
		await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

		// Remove orphanable data that does not have FK relationships to users.
		await db.Database.ExecuteSqlInterpolatedAsync($@"
DELETE FROM interactions
WHERE UserId IN (
	SELECT Id FROM users
	WHERE Id LIKE 'guest-%'
	  AND CreatedAtUtc < {createdBeforeUtc}
	  AND PasswordHash IS NULL
	  AND PasswordSalt IS NULL
	  AND PasswordIterations IS NULL
);
", cancellationToken);

		// Best-effort audit cleanup.
		await db.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE accepted_movies
SET AcceptedByUserId = NULL
WHERE AcceptedByUserId IN (
	SELECT Id FROM users
	WHERE Id LIKE 'guest-%'
	  AND CreatedAtUtc < {createdBeforeUtc}
	  AND PasswordHash IS NULL
	  AND PasswordSalt IS NULL
	  AND PasswordIterations IS NULL
);
", cancellationToken);

		// Deleting users cascades to roles/preferences via FKs.
		var deleted = await db.Database.ExecuteSqlInterpolatedAsync($@"
DELETE FROM users
WHERE Id LIKE 'guest-%'
	AND CreatedAtUtc < {createdBeforeUtc}
	AND PasswordHash IS NULL
	AND PasswordSalt IS NULL
	AND PasswordIterations IS NULL;
", cancellationToken);

		await tx.CommitAsync(cancellationToken);
		return deleted;
	}

	public async Task<UserRecord?> FindByIdAsync(string userId, CancellationToken cancellationToken)
	{
		var user = await db.Users
			.AsNoTracking()
			.Where(x => x.Id == userId)
			.Select(x => new UserRecord(
				x.Id,
				x.DisplayName,
				x.CreatedAtUtc,
				x.PasswordHash != null && x.PasswordSalt != null && x.PasswordIterations != null))
			.SingleOrDefaultAsync(cancellationToken);

		return user;
	}

	public async Task<IReadOnlyCollection<string>> GetRolesAsync(string userId, CancellationToken cancellationToken)
	{
		var roles = await db.UserRoles
			.AsNoTracking()
			.Where(x => x.UserId == userId)
			.Select(x => x.RoleName)
			.ToListAsync(cancellationToken);

		return roles;
	}

	public async Task<PasswordCredentialRecord?> GetPasswordCredentialAsync(string userId, CancellationToken cancellationToken)
	{
		var creds = await db.Users
			.AsNoTracking()
			.Where(x => x.Id == userId)
			.Select(x => new
			{
				x.PasswordHash,
				x.PasswordSalt,
				x.PasswordIterations
			})
			.SingleOrDefaultAsync(cancellationToken);

		if (creds?.PasswordHash is null || creds.PasswordSalt is null || creds.PasswordIterations is null)
		{
			return null;
		}

		return new PasswordCredentialRecord(creds.PasswordHash, creds.PasswordSalt, creds.PasswordIterations.Value);
	}

	public async Task<bool> UserExistsAsync(string userId, CancellationToken cancellationToken)
	{
		return await db.Users.AsNoTracking().AnyAsync(x => x.Id == userId, cancellationToken);
	}

	public async Task<IReadOnlyCollection<UserRecord>> ListAsync(int skip, int take, CancellationToken cancellationToken)
	{
		var users = await db.Users
			.AsNoTracking()
			.Skip(Math.Max(0, skip))
			.Take(Math.Clamp(take, 1, 500))
			.Select(x => new UserRecord(
				x.Id,
				x.DisplayName,
				x.CreatedAtUtc,
				x.PasswordHash != null && x.PasswordSalt != null && x.PasswordIterations != null))
			.ToListAsync(cancellationToken);

		// SQLite provider cannot translate DateTimeOffset ORDER BY reliably; sort in-memory.
		users = users.OrderBy(x => x.CreatedAtUtc).ToList();

		return users;
	}

	public async Task CreateAsync(CreateUserRecord user, CancellationToken cancellationToken)
	{
		var entity = new UserEntity
		{
			Id = user.Id,
			DisplayName = user.DisplayName,
			CreatedAtUtc = user.CreatedAtUtc
		};

		db.Users.Add(entity);
		await db.SaveChangesAsync(cancellationToken);
	}

	public async Task UpdateDisplayNameAsync(string userId, string displayName, CancellationToken cancellationToken)
	{
		var user = await db.Users.SingleOrDefaultAsync(x => x.Id == userId, cancellationToken);
		if (user is null)
		{
			throw new InvalidOperationException("User not found.");
		}

		user.DisplayName = displayName;
		await db.SaveChangesAsync(cancellationToken);
	}

	public async Task SetPasswordAsync(string userId, byte[] passwordHash, byte[] passwordSalt, int passwordIterations, CancellationToken cancellationToken)
	{
		var user = await db.Users.SingleOrDefaultAsync(x => x.Id == userId, cancellationToken);
		if (user is null)
		{
			throw new InvalidOperationException("User not found.");
		}

		user.PasswordHash = passwordHash;
		user.PasswordSalt = passwordSalt;
		user.PasswordIterations = passwordIterations;

		await db.SaveChangesAsync(cancellationToken);
	}

	public async Task SetRolesAsync(string userId, IReadOnlyCollection<string> roles, CancellationToken cancellationToken)
	{
		var normalized = roles
			.Where(r => !string.IsNullOrWhiteSpace(r))
			.Select(r => r.Trim())
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();

		var user = await db.Users
			.Include(x => x.UserRoles)
			.SingleOrDefaultAsync(x => x.Id == userId, cancellationToken);

		if (user is null)
		{
			throw new InvalidOperationException("User not found.");
		}

		// Remove roles not in target set.
		user.UserRoles.RemoveAll(ur => !normalized.Contains(ur.RoleName, StringComparer.OrdinalIgnoreCase));

		// Add missing roles.
		foreach (var role in normalized)
		{
			if (user.UserRoles.Any(ur => string.Equals(ur.RoleName, role, StringComparison.OrdinalIgnoreCase)))
			{
				continue;
			}

			user.UserRoles.Add(new UserRoleEntity { UserId = userId, RoleName = role });
		}

		await db.SaveChangesAsync(cancellationToken);
	}

	public async Task DeleteAsync(string userId, CancellationToken cancellationToken)
	{
		var user = await db.Users.SingleOrDefaultAsync(x => x.Id == userId, cancellationToken);
		if (user is null)
		{
			return;
		}

		db.Users.Remove(user);
		await db.SaveChangesAsync(cancellationToken);
	}
}

