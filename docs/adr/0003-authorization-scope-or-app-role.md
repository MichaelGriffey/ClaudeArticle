# ADR 0003: Writes accept the delegated scope or the app role

- Status: Accepted
- Work item: AB#TBD (architecture hardening), AB#1234

## Context

The write policy required a claim named `scope` whose whole value was `readings:write`. Microsoft
Entra ID, the intended identity provider, issues delegated scopes as one space-separated `scp`
claim, and service-to-service (client credentials) tokens carry app roles in `roles`. Real Entra
callers would have received 403. The test authentication handler minted exactly the claim the
policy expected, so no test could catch it. Writers are both people (operators, tools) and
services (device gateways).

Options considered:

- **A. Our own requirement for scope or app role.** Accept `readings:write` in `scp` or `scope`
  (space-separated) or `Readings.Write` in `roles`. No new dependency.
- **B. Microsoft.Identity.Web.** Maintained scope-or-app-permission checks, but a new dependency
  (license and vulnerability review) that couples the service to Entra.
- **C. App roles only**, or **D. delegated scopes only.** Each locks out one kind of caller.

## Decision

Option A. `ScopeOrAppRoleRequirement` passes when an authenticated principal has the scope in any
`scp` or `scope` claim (split on spaces, ordinal comparison) or the role in any `roles` claim.
JwtBearer keeps claim names as issued (`MapInboundClaims = false`). The test authentication handler
issues Entra-shaped claims (`scp`, `roles`). Local development keeps `dotnet user-jwts create
--scope "readings:write"`, which issues a `scope` claim.

Outside Development the service refuses to start without a bearer authority (or signing keys) and
an audience, instead of starting and rejecting every caller.

The policy also requires a `sub` claim, so every accepted write names a subject in the audit trail
([ADR 0008](0008-audit-trail.md)). Entra ID and `dotnet user-jwts` tokens always carry one.

## Consequences

- Entra app registration exposes the `readings:write` scope and the `Readings.Write` app role.
- About 40 lines of authorization code are ours to maintain and test.
- Tests: `AuthorizationTests` in `tests/Ingestion.IntegrationTests`, and the startup check tests.
