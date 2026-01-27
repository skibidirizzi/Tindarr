using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Tindarr.Api.Auth;

public sealed class DevHeaderAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(DevHeaderAuthenticationDefaults.UserIdHeader, out var userIdValues))
        {
            return Task.FromResult(AuthenticateResult.Fail("Missing X-User-Id header."));
        }

        var userId = userIdValues.ToString();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid X-User-Id header."));
        }

        var role = Request.Headers.TryGetValue(DevHeaderAuthenticationDefaults.UserRoleHeader, out var roleValues)
            ? roleValues.ToString()
            : Policies.ContributorRole;

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new(ClaimTypes.Role, string.IsNullOrWhiteSpace(role) ? Policies.ContributorRole : role)
        };

        var identity = new ClaimsIdentity(claims, DevHeaderAuthenticationDefaults.Scheme);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, DevHeaderAuthenticationDefaults.Scheme);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
