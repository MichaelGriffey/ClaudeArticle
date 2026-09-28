using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace Ingestion.Api.Security;

/// <summary>
/// Passes when the caller holds <see cref="Scope"/> as a delegated scope (people) or
/// <see cref="AppRole"/> as an app role (services), per ADR 0003. Microsoft Entra ID sends delegated
/// scopes as one space-separated <c>scp</c> claim and app roles as <c>roles</c> claims;
/// <c>dotnet user-jwts</c> sends <c>scope</c>. Comparison is ordinal: scopes and roles are identifiers.
/// </summary>
/// <remarks>The requirement is its own handler, so it needs no registration.</remarks>
public sealed class ScopeOrAppRoleRequirement(string scope, string appRole)
    : AuthorizationHandler<ScopeOrAppRoleRequirement>, IAuthorizationRequirement
{
    public string Scope { get; } = scope;
    public string AppRole { get; } = appRole;

    public static bool HasScope(ClaimsPrincipal user, string scope)
    {
        ArgumentNullException.ThrowIfNull(user);
        return user.FindAll(c => c.Type is "scp" or "scope")
            .SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Contains(scope, StringComparer.Ordinal);
    }

    public static bool HasAppRole(ClaimsPrincipal user, string appRole)
    {
        ArgumentNullException.ThrowIfNull(user);
        return user.HasClaim(c => c.Type == "roles" && string.Equals(c.Value, appRole, StringComparison.Ordinal));
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, ScopeOrAppRoleRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);

        if (HasScope(context.User, requirement.Scope) || HasAppRole(context.User, requirement.AppRole))
            context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
