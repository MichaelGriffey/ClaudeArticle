using System.Net;
using System.Net.Http.Json;
using System.Text;
using Ingestion.Api.Security;
using Ingestion.Testing;
using Xunit;
using static Ingestion.IntegrationTests.Problems;

namespace Ingestion.IntegrationTests;

/// <summary>ADR 0002: every error response is RFC 9457 problem details, and none leaks exception text.</summary>
public sealed class ErrorContractTests
{
    private static readonly Uri Url = new("/sensors/TMP-07/readings", UriKind.Relative);
    private static readonly object Reading = new { value = 20.0, observedAt = IngestionApi.DefaultStart };

    [Fact]
    public async Task Anonymous_callers_get_a_401_problem()
    {
        await using var api = new IngestionApi();
        using var client = api.CreateClient();

        using var response = await client.PostAsJsonAsync(Url, Reading, Ct);

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized);
        Assert.Equal(0, api.Store.AppendCalls);
    }

    [Fact]
    public async Task Callers_without_the_write_scope_get_a_403_problem()
    {
        await using var api = new IngestionApi();
        using var client = api.CreateClientWithScope("readings:read");

        using var response = await client.PostAsJsonAsync(Url, Reading, Ct);

        await AssertProblemAsync(response, HttpStatusCode.Forbidden);
        Assert.Equal(0, api.Store.AppendCalls);
    }

    [Fact]
    public async Task Malformed_json_gets_a_400_problem()
    {
        await using var api = new IngestionApi();
        using var client = api.CreateClientWithScope(ReadingsAuthorization.WriteScope);

        using var response = await client.PostRawAsync(Url, """{"value":NaN,"observedAt":"2026-09-27T14:00:00Z"}""");

        await AssertProblemAsync(response, HttpStatusCode.BadRequest);
        Assert.Equal(0, api.Store.AppendCalls);
    }

    [Fact]
    public async Task A_body_that_is_not_json_gets_a_415_problem()
    {
        await using var api = new IngestionApi();
        using var client = api.CreateClientWithScope(ReadingsAuthorization.WriteScope);
        using var content = new StringContent("value=20", Encoding.UTF8, "text/plain");

        using var response = await client.PostAsync(Url, content, Ct);

        await AssertProblemAsync(response, HttpStatusCode.UnsupportedMediaType);
    }

    [Fact]
    public async Task An_unknown_route_gets_a_404_problem()
    {
        await using var api = new IngestionApi();
        using var client = api.CreateClientWithScope(ReadingsAuthorization.WriteScope);

        using var response = await client.GetAsync(new Uri("/no-such-route", UriKind.Relative), Ct);

        await AssertProblemAsync(response, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_unexpected_failure_gets_a_500_problem_without_exception_text()
    {
        await using var api = new IngestionApi();
        api.Store.Fault = new InvalidOperationException("Server=db.internal;Password=hunter2");
        using var client = api.CreateClientWithScope(ReadingsAuthorization.WriteScope);

        using var response = await client.PostAsJsonAsync(Url, Reading, Ct);

        await AssertProblemAsync(response, HttpStatusCode.InternalServerError);
        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.DoesNotContain("hunter2", body, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(InvalidOperationException), body, StringComparison.Ordinal);
    }
}
