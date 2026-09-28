using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ingestion.Testing;

/// <summary>
/// Authenticates a request the way Microsoft Entra ID tokens look after validation (ADR 0003):
/// <see cref="ScopeHeader"/> becomes one space-separated <c>scp</c> claim, and each comma-separated
/// entry of <see cref="AppRolesHeader"/> becomes a <c>roles</c> claim. No header means anonymous, so
/// tests can prove 401 and 403 as well as 200.
/// </summary>
public sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string ScopeHeader = "X-Test-Scope";
    public const string AppRolesHeader = "X-Test-App-Roles";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new List<Claim>();
        if (Request.Headers.TryGetValue(ScopeHeader, out var scopes))
            claims.Add(new Claim("scp", scopes.ToString()));
        if (Request.Headers.TryGetValue(AppRolesHeader, out var roles))
            claims.AddRange(roles.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries).Select(r => new Claim("roles", r)));

        if (claims.Count == 0)
            return Task.FromResult(AuthenticateResult.NoResult());

        var identity = new ClaimsIdentity(claims, SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
