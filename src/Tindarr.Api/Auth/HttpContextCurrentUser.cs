using System.Security.Claims;
using Tindarr.Application.Abstractions.Security;

namespace Tindarr.Api.Auth;

public sealed class HttpContextCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
	public string UserId => accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown";

	public IReadOnlyCollection<string> Roles =>
		accessor.HttpContext?.User.FindAll(ClaimTypes.Role).Select(c => c.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
		?? [];

	public bool IsInRole(string role)
	{
		return Roles.Contains(role, StringComparer.OrdinalIgnoreCase);
	}
}

