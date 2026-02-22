using Tindarr.Application.Abstractions.Persistence;
using Tindarr.Application.Abstractions.Security;
using Tindarr.Application.Interfaces.Auth;
using Tindarr.Application.Interfaces.Rooms;
using Tindarr.Application.Options;
using System.Security.Claims;

namespace Tindarr.Application.Features.Auth;

public sealed class AuthService(
	IUserRepository users,
	IPasswordHasher passwordHasher,
	ITokenService tokenService,
	IRoomService roomService,
	IRoomLifetimeProvider roomLifetimeProvider,
	Microsoft.Extensions.Options.IOptions<RegistrationOptions> registrationOptions) : IAuthService
{
	private readonly RegistrationOptions registration = registrationOptions.Value;

	public async Task<AuthSession> GuestAsync(string roomId, string? displayName, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(roomId))
		{
			throw new ArgumentException("RoomId is required.");
		}

		var room = await roomService.GetAsync(roomId, cancellationToken);
		if (room is null)
		{
			throw new InvalidOperationException("Room not found.");
		}

		if (room.IsClosed)
		{
			throw new InvalidOperationException("Room is closed to new users.");
		}

		var normalizedDisplayName = string.IsNullOrWhiteSpace(displayName) ? "Guest" : NormalizeDisplayName(displayName);
		var id = $"guest-{Guid.NewGuid():N}";

		var roles = new List<string> { "Guest" };
		var ttl = await roomLifetimeProvider.GetGuestSessionTtlAsync(cancellationToken).ConfigureAwait(false);
		var token = tokenService.IssueAccessToken(
			id,
			roles,
			additionalClaims: new[]
			{
				new Claim(TindarrClaimTypes.IsGuest, "1"),
				new Claim(TindarrClaimTypes.RoomId, room.RoomId),
				new Claim(TindarrClaimTypes.DisplayName, normalizedDisplayName)
			},
			lifetimeOverride: ttl);

		return new AuthSession(token.AccessToken, token.ExpiresAtUtc, id, normalizedDisplayName, roles);
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
		var existingUsers = await users.ListAsync(0, 1, cancellationToken);
		var isFirstUser = existingUsers.Count == 0;

		await users.CreateAsync(new CreateUserRecord(normalizedUserId, normalizedDisplayName, now), cancellationToken);

		var hashed = passwordHasher.Hash(password, registration.PasswordHashIterations);
		await users.SetPasswordAsync(normalizedUserId, hashed.Hash, hashed.Salt, hashed.Iterations, cancellationToken);

		var rolesToSet = new List<string> { registration.DefaultRole };
		if (isFirstUser || string.Equals(normalizedUserId, "admin", StringComparison.OrdinalIgnoreCase))
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

