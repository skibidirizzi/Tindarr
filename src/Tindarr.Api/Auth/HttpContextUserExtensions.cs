using System.Security.Claims;

namespace Tindarr.Api.Auth;

public static class HttpContextUserExtensions
{
    public static string GetUserId(this ClaimsPrincipal user)
    {
        return user.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown";
    }
}
