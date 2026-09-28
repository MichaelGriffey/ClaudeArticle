using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Ingestion.Api.Features.Ingest;
using Ingestion.Testing;
using Xunit;

namespace Ingestion.IntegrationTests;

/// <summary>The imperative shell against the real pipeline: routing, auth, JSON, status codes.</summary>
public sealed class IngestEndpointTests
{
    private static readonly Uri Url = new("/sensors/TMP-07/readings", UriKind.Relative);
    private static readonly DateTimeOffset Now = IngestionApi.DefaultStart;
    private static readonly string NowIso = Now.ToString("O", CultureInfo.InvariantCulture);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Anonymous_callers_get_401()
    {
        await using var api = new IngestionApi();
        using var client = api.CreateClient();

        using var response = await client.PostAsJsonAsync(Url, new { value = 20.0, observedAt = Now }, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, api.Store.AppendCalls);
    }

    [Fact]
    public async Task Callers_without_the_write_scope_get_403()
    {
        await using var api = new IngestionApi();
        using var client = api.CreateClientWithScope("readings:read");

        using var response = await client.PostAsJsonAsync(Url, new { value = 20.0, observedAt = Now }, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_reading_at_the_upper_limit_is_classified_nominal_and_persisted()   // AC-1
    {
        await using var api = new IngestionApi();
        using var client = api.CreateClientWithScope(IngestEndpoint.WritePolicy);

        using var response = await client.PostAsJsonAsync(Url, new { value = 125.0, observedAt = Now }, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal("Nominal", body.RootElement.GetProperty("classification").GetString());
        Assert.Single(api.Store.Rows);
    }

    [Theory]
    [InlineData("\"NaN\"")]
    [InlineData("\"-Infinity\"")]
    public async Task Quoted_non_finite_values_reach_the_domain_and_get_422(string jsonValue)   // AC-2
    {
        await using var api = new IngestionApi();
        using var client = api.CreateClientWithScope(IngestEndpoint.WritePolicy);

        using var response = await PostRawAsync(client, $$"""{"value":{{jsonValue}},"observedAt":"{{NowIso}}"}""");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("NonFiniteValue", await ProblemTitleAsync(response));
        Assert.Equal(0, api.Store.AppendCalls);
    }

    [Fact]
    public async Task A_bare_NaN_token_is_invalid_json_and_gets_400()
    {
        await using var api = new IngestionApi();
        using var client = api.CreateClientWithScope(IngestEndpoint.WritePolicy);

        using var response = await PostRawAsync(client, $$"""{"value":NaN,"observedAt":"{{NowIso}}"}""");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_duplicate_reading_is_idempotent()                                      // AC-3
    {
        await using var api = new IngestionApi();
        using var client = api.CreateClientWithScope(IngestEndpoint.WritePolicy);
        var reading = new { value = 20.0, observedAt = Now };

        using var first = await client.PostAsJsonAsync(Url, reading, Ct);
        using var second = await client.PostAsJsonAsync(Url, reading, Ct);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Single(api.Store.Rows);
    }

    [Fact]
    public async Task An_unavailable_store_returns_503_with_retry_after_and_persists_nothing()  // AC-4
    {
        await using var api = new IngestionApi();
        api.Store.IsUnavailable = true;
        using var client = api.CreateClientWithScope(IngestEndpoint.WritePolicy);

        using var response = await client.PostAsJsonAsync(Url, new { value = 20.0, observedAt = Now }, Ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(5), response.Headers.RetryAfter?.Delta);
        Assert.Empty(api.Store.Rows);
    }

    [Theory]
    [InlineData(-301, "StaleReading")]
    [InlineData(3, "FutureTimestamp")]
    public async Task Readings_outside_the_freshness_window_get_422(int offsetSeconds, string expectedError)   // AC-5
    {
        await using var api = new IngestionApi();
        using var client = api.CreateClientWithScope(IngestEndpoint.WritePolicy);

        using var response = await client.PostAsJsonAsync(
            Url, new { value = 20.0, observedAt = Now.AddSeconds(offsetSeconds) }, Ct);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(expectedError, await ProblemTitleAsync(response));
    }

    [Fact]
    public async Task An_unknown_sensor_gets_404()
    {
        await using var api = new IngestionApi();
        using var client = api.CreateClientWithScope(IngestEndpoint.WritePolicy);

        using var response = await client.PostAsJsonAsync(
            new Uri("/sensors/NOPE-01/readings", UriKind.Relative), new { value = 20.0, observedAt = Now }, Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> PostRawAsync(HttpClient client, string json)
    {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        return await client.PostAsync(Url, content, Ct);
    }

    private static async Task<string?> ProblemTitleAsync(HttpResponseMessage response)
    {
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return problem.RootElement.GetProperty("title").GetString();
    }
}
