using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Ingestion.Api.Security;
using Ingestion.Testing;
using Xunit;
using static Ingestion.IntegrationTests.Problems;

namespace Ingestion.IntegrationTests;

/// <summary>ADR 0003: writes accept the delegated scope (people) or the app role (services), as Entra ID issues them.</summary>
public sealed class AuthorizationTests
{
    private static readonly Uri Url = new("/sensors/TMP-07/readings", UriKind.Relative);
    private static readonly object Reading = new { value = 20.0, observedAt = IngestionApi.DefaultStart };

    [Theory]
    [InlineData("readings:write")]
    [InlineData("openid readings:read readings:write")]      // Entra: one space-separated scp claim
    public async Task A_caller_with_the_write_scope_may_write(string scopes)
    {
        await using var api = new IngestionApi();
        using var client = api.CreateClientWithScope(scopes);

        using var response = await client.PostAsJsonAsync(Url, Reading, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_service_with_the_write_app_role_may_write()
    {
        await using var api = new IngestionApi();
        using var client = api.CreateClientWithAppRoles("Readings.Read", ReadingsAuthorization.WriteAppRole);

        using var response = await client.PostAsJsonAsync(Url, Reading, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("readings:read")]
    [InlineData("readings:writer")]                          // a longer scope is not the scope
    [InlineData("READINGS:WRITE")]                           // identifiers compare ordinally
    public async Task A_caller_without_the_write_scope_gets_403(string scopes)
    {
        await using var api = new IngestionApi();
        using var client = api.CreateClientWithScope(scopes);

        using var response = await client.PostAsJsonAsync(Url, Reading, Ct);

        await AssertProblemAsync(response, HttpStatusCode.Forbidden);
        Assert.Equal(0, api.Store.AppendCalls);
    }

    [Fact]
    public async Task A_service_with_another_app_role_gets_403()
    {
        await using var api = new IngestionApi();
        using var client = api.CreateClientWithAppRoles("Readings.Read");

        using var response = await client.PostAsJsonAsync(Url, Reading, Ct);

        await AssertProblemAsync(response, HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("scp", "readings:write", true)]
    [InlineData("scope", "readings:write", true)]           // dotnet user-jwts
    [InlineData("scope", "a readings:write b", true)]
    [InlineData("http://schemas.microsoft.com/identity/claims/scope", "readings:write", false)]   // mapped names are off
    [InlineData("roles", "readings:write", false)]
    public void Scopes_are_read_from_scp_and_scope_claims(string claimType, string value, bool expected)
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(claimType, value)], "Test"));

        Assert.Equal(expected, ScopeOrAppRoleRequirement.HasScope(user, ReadingsAuthorization.WriteScope));
    }
}
