using Tindarr.Application.Features.Auth;

namespace Tindarr.Application.Interfaces.Auth;

public interface IAuthService
{
	Task<AuthSession> RegisterAsync(string userId, string displayName, string password, CancellationToken cancellationToken);

	Task<AuthSession> LoginAsync(string userId, string password, CancellationToken cancellationToken);

	Task<AuthUserInfo> GetMeAsync(string userId, CancellationToken cancellationToken);

	Task SetPasswordAsync(string userId, string? currentPassword, string newPassword, CancellationToken cancellationToken);
}

