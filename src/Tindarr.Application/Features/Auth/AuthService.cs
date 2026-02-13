using Tindarr.Application.Abstractions.Persistence;
using Tindarr.Application.Abstractions.Security;
using Tindarr.Application.Interfaces.Auth;
using Tindarr.Application.Options;

namespace Tindarr.Application.Features.Auth;

public sealed class AuthService(
	IUserRepository users,
	IPasswordHasher passwordHasher,
	ITokenService tokenService,
	Microsoft.Extensions.Options.IOptions<RegistrationOptions> registrationOptions) : IAuthService
{
	private readonly RegistrationOptions registration = registrationOptions.Value;

	public async Task<AuthSession> GuestAsync(string? displayName, CancellationToken cancellationToken)
	{
		var normalizedDisplayName = string.IsNullOrWhiteSpace(displayName) ? "Guest" : NormalizeDisplayName(displayName);
		var now = DateTimeOffset.UtcNow;

		// Try a handful of times to avoid collisions.
		for (var attempt = 0; attempt < 10; attempt++)
		{
			var id = $"guest-{Guid.NewGuid():N}";
			if (await users.UserExistsAsync(id, cancellationToken))
			{
				continue;
			}

			await users.CreateAsync(new CreateUserRecord(id, normalizedDisplayName, now), cancellationToken);
			await users.SetRolesAsync(id, new[] { registration.DefaultRole }, cancellationToken);

			var roles = await users.GetRolesAsync(id, cancellationToken);
			var token = tokenService.IssueAccessToken(id, roles.ToList());
			return new AuthSession(token.AccessToken, token.ExpiresAtUtc, id, normalizedDisplayName, roles.ToList());
		}

		throw new InvalidOperationException("Could not create guest session.");
	}

	public async Task<AuthSession> RegisterAsync(string userId, string displayName, string password, CancellationToken cancellationToken)
	{
		if (!registration.AllowOpenRegistration)
		{
			throw new InvalidOperationException("Registration is disabled.");
		}

		var normalizedUserId = NormalizeUserId(userId);
		var normalizedDisplayName = NormalizeDisplayName(displayName);

		if (await users.UserExistsAsync(normalizedUserId, cancellationToken))
		{
			throw new InvalidOperationException("User already exists.");
		}

		var now = DateTimeOffset.UtcNow;
		await users.CreateAsync(new CreateUserRecord(normalizedUserId, normalizedDisplayName, now), cancellationToken);

		var hashed = passwordHasher.Hash(password, registration.PasswordHashIterations);
		await users.SetPasswordAsync(normalizedUserId, hashed.Hash, hashed.Salt, hashed.Iterations, cancellationToken);

		var rolesToSet = new List<string> { registration.DefaultRole };
		if (string.Equals(normalizedUserId, "admin", StringComparison.OrdinalIgnoreCase))
		{
			rolesToSet.Add("Admin");
		}

		await users.SetRolesAsync(normalizedUserId, rolesToSet, cancellationToken);
		var roles = await users.GetRolesAsync(normalizedUserId, cancellationToken);

		var token = tokenService.IssueAccessToken(normalizedUserId, roles.ToList());
		return new AuthSession(token.AccessToken, token.ExpiresAtUtc, normalizedUserId, normalizedDisplayName, roles.ToList());
	}

	public async Task<AuthSession> LoginAsync(string userId, string password, CancellationToken cancellationToken)
	{
		var normalizedUserId = NormalizeUserId(userId);

		var user = await users.FindByIdAsync(normalizedUserId, cancellationToken);
		if (user is null)
		{
			throw new InvalidOperationException("Invalid credentials.");
		}

		var creds = await users.GetPasswordCredentialAsync(normalizedUserId, cancellationToken);
		if (creds is null)
		{
			throw new InvalidOperationException("Invalid credentials.");
		}

		if (!passwordHasher.Verify(password, creds.PasswordHash, creds.PasswordSalt, creds.PasswordIterations))
		{
			throw new InvalidOperationException("Invalid credentials.");
		}

		var roles = await users.GetRolesAsync(normalizedUserId, cancellationToken);
		var token = tokenService.IssueAccessToken(normalizedUserId, roles.ToList());

		return new AuthSession(token.AccessToken, token.ExpiresAtUtc, user.Id, user.DisplayName, roles.ToList());
	}

	public async Task<AuthUserInfo> GetMeAsync(string userId, CancellationToken cancellationToken)
	{
		var normalizedUserId = NormalizeUserId(userId);
		var user = await users.FindByIdAsync(normalizedUserId, cancellationToken)
			?? throw new InvalidOperationException("User not found.");

		var roles = await users.GetRolesAsync(normalizedUserId, cancellationToken);
		return new AuthUserInfo(user.Id, user.DisplayName, roles.ToList());
	}

	public async Task SetPasswordAsync(string userId, string? currentPassword, string newPassword, CancellationToken cancellationToken)
	{
		var normalizedUserId = NormalizeUserId(userId);

		var user = await users.FindByIdAsync(normalizedUserId, cancellationToken);
		if (user is null)
		{
			throw new InvalidOperationException("User not found.");
		}

		var existing = await users.GetPasswordCredentialAsync(normalizedUserId, cancellationToken);
		if (existing is not null)
		{
			if (string.IsNullOrWhiteSpace(currentPassword)
				|| !passwordHasher.Verify(currentPassword, existing.PasswordHash, existing.PasswordSalt, existing.PasswordIterations))
			{
				throw new InvalidOperationException("Invalid current password.");
			}
		}

		var hashed = passwordHasher.Hash(newPassword, registration.PasswordHashIterations);
		await users.SetPasswordAsync(normalizedUserId, hashed.Hash, hashed.Salt, hashed.Iterations, cancellationToken);
	}

	private static string NormalizeUserId(string value)
	{
		var v = (value ?? string.Empty).Trim();
		if (string.IsNullOrWhiteSpace(v))
		{
			throw new ArgumentException("UserId is required.");
		}

		if (v.Any(char.IsWhiteSpace))
		{
			throw new ArgumentException("UserId must not contain whitespace.");
		}

		return v.ToLowerInvariant();
	}

	private static string NormalizeDisplayName(string value)
	{
		var v = (value ?? string.Empty).Trim();
		if (string.IsNullOrWhiteSpace(v))
		{
			throw new ArgumentException("DisplayName is required.");
		}

		return v;
	}
}

public sealed record AuthSession(
	string AccessToken,
	DateTimeOffset ExpiresAtUtc,
	string UserId,
	string DisplayName,
	IReadOnlyList<string> Roles);

public sealed record AuthUserInfo(string UserId, string DisplayName, IReadOnlyList<string> Roles);

