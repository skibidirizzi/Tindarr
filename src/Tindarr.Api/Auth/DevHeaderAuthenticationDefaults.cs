namespace Tindarr.Api.Auth;

public static class DevHeaderAuthenticationDefaults
{
    public const string Scheme = "devheader";
    public const string UserIdHeader = "X-User-Id";
    public const string UserRoleHeader = "X-User-Role";
}
