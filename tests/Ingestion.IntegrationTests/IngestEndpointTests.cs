using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Ingestion.Api.Features.Ingest;
using Ingestion.Testing;
using Xunit;
using static Ingestion.IntegrationTests.Problems;

namespace Ingestion.IntegrationTests;

/// <summary>The imperative shell against the real pipeline: validation, JSON, status codes, persistence.</summary>
public sealed class IngestEndpointTests
{
    private static readonly Uri Url = new("/sensors/TMP-07/readings", UriKind.Relative);
    private static readonly DateTimeOffset Now = IngestionApi.DefaultStart;
    private static readonly string NowIso = Now.ToString("O", CultureInfo.InvariantCulture);

    [Fact]
    public async Task A_reading_at_the_upper_limit_is_classified_nominal_and_persisted()   // AC-1
    {
        await using var api = new IngestionApi();
        using var client = api.CreateClientWithScope(IngestEndpoint.WritePolicy);

        using var response = await client.PostAsJsonAsync(Url, new { value = 125.0, observedAt = Now }, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal("Nominal", body.GetProperty("classification").GetString());
        Assert.Single(api.Store.Rows);
    }

    [Theory]
    [InlineData("\"NaN\"")]
    [InlineData("\"-Infinity\"")]
    public async Task Quoted_non_finite_values_reach_the_domain_and_get_422(string jsonValue)   // AC-2
    {
        await using var api = new IngestionApi();
        using var client = api.CreateClientWithScope(IngestEndpoint.WritePolicy);

        using var response = await client.PostRawAsync(Url, $$"""{"value":{{jsonValue}},"observedAt":"{{NowIso}}"}""");

        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "NonFiniteValue");
        Assert.Equal(0, api.Store.AppendCalls);
    }

    [Fact]
    public async Task A_duplicate_reading_is_idempotent()                                      // AC-3
    {
        await using var api = new IngestionApi();
        using var client = api.CreateClientWithScope(IngestEndpoint.WritePolicy);
        var reading = new { value = 130.0, observedAt = Now };

        using var first = await client.PostAsJsonAsync(Url, reading, Ct);
        using var second = await client.PostAsJsonAsync(Url, reading, Ct);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal("High", (await ReadJsonAsync(second)).GetProperty("classification").GetString());
        Assert.Single(api.Store.Rows);
    }

    [Fact]
    public async Task A_different_value_for_a_stored_timestamp_gets_409_and_changes_nothing()  // AC-8
    {
        await using var api = new IngestionApi();
        using var client = api.CreateClientWithScope(IngestEndpoint.WritePolicy);

        using var first = await client.PostAsJsonAsync(Url, new { value = 20.0, observedAt = Now }, Ct);
        using var second = await client.PostAsJsonAsync(Url, new { value = 200.0, observedAt = Now }, Ct);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        await AssertProblemAsync(second, HttpStatusCode.Conflict, "ConflictingReading");
        var stored = Assert.Single(api.Store.Rows);
        Assert.Equal(20.0, stored.Value);
    }

    [Fact]
    public async Task A_store_that_does_not_answer_within_the_budget_gets_503_DependencyTimeout()   // AC-9
    {
        await using var api = new IngestionApi();
        api.Store.Hangs = true;
        using var client = api.CreateClientWithScope(IngestEndpoint.WritePolicy);

        var pending = client.PostAsJsonAsync(Url, new { value = 20.0, observedAt = Now }, Ct);
        await api.Store.Entered.WaitAsync(Ct);
        api.Clock.Advance(TimeSpan.FromSeconds(2));                                  // the configured budget
        using var response = await pending;

        await AssertProblemAsync(response, HttpStatusCode.ServiceUnavailable, "DependencyTimeout");
        Assert.Equal(TimeSpan.FromSeconds(5), response.Headers.RetryAfter?.Delta);
        Assert.Empty(api.Store.Rows);
    }

    [Fact]
    public async Task An_older_reading_inside_the_window_is_accepted_after_a_newer_one()      // AC-10
    {
        await using var api = new IngestionApi();
        using var client = api.CreateClientWithScope(IngestEndpoint.WritePolicy);

        using var newer = await client.PostAsJsonAsync(Url, new { value = 20.0, observedAt = Now }, Ct);
        using var older = await client.PostAsJsonAsync(Url, new { value = 21.0, observedAt = Now.AddSeconds(-10) }, Ct);

        Assert.Equal(HttpStatusCode.OK, newer.StatusCode);
        Assert.Equal(HttpStatusCode.OK, older.StatusCode);
        Assert.Equal(2, api.Store.Rows.Count);
    }

    [Fact]
    public async Task An_unavailable_store_returns_503_with_retry_after_and_persists_nothing()  // AC-4
    {
        await using var api = new IngestionApi();
        api.Store.IsUnavailable = true;
        using var client = api.CreateClientWithScope(IngestEndpoint.WritePolicy);

        using var response = await client.PostAsJsonAsync(Url, new { value = 20.0, observedAt = Now }, Ct);

        await AssertProblemAsync(response, HttpStatusCode.ServiceUnavailable, "EventStoreUnavailable");
        Assert.Equal(TimeSpan.FromSeconds(5), response.Headers.RetryAfter?.Delta);
        Assert.Empty(api.Store.Rows);
    }

    [Fact]
    public async Task A_stale_reading_gets_422_with_its_age_and_the_limit()                   // AC-5
    {
        await using var api = new IngestionApi();
        using var client = api.CreateClientWithScope(IngestEndpoint.WritePolicy);

        using var response = await client.PostAsJsonAsync(Url, new { value = 20.0, observedAt = Now.AddSeconds(-301) }, Ct);

        var problem = await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "StaleReading");
        Assert.Equal(301, problem.GetProperty("ageSeconds").GetDouble());
        Assert.Equal(300, problem.GetProperty("maxAgeSeconds").GetDouble());
        Assert.Equal("The reading is 301 s old; the limit is 300 s.", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task A_future_reading_gets_422_with_its_skew_and_the_limit()                 // AC-5
    {
        await using var api = new IngestionApi();
        using var client = api.CreateClientWithScope(IngestEndpoint.WritePolicy);

        using var response = await client.PostAsJsonAsync(Url, new { value = 20.0, observedAt = Now.AddSeconds(3) }, Ct);

        var problem = await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "FutureTimestamp");
        Assert.Equal(3, problem.GetProperty("skewSeconds").GetDouble());
        Assert.Equal(2, problem.GetProperty("maxSkewSeconds").GetDouble());
    }

    [Theory]
    [InlineData("""{"observedAt":"2026-09-27T14:00:00Z"}""", new[] { "value" })]
    [InlineData("""{"value":null,"observedAt":"2026-09-27T14:00:00Z"}""", new[] { "value" })]
    [InlineData("""{"value":20.0}""", new[] { "observedAt" })]
    [InlineData("""{"value":20.0,"observedAt":null}""", new[] { "observedAt" })]
    [InlineData("""{}""", new[] { "value", "observedAt" })]
    public async Task Missing_fields_get_400_naming_each_field(string json, string[] fields)  // AC-6
    {
        await using var api = new IngestionApi();
        using var client = api.CreateClientWithScope(IngestEndpoint.WritePolicy);

        using var response = await client.PostRawAsync(Url, json);

        var problem = await AssertProblemAsync(response, HttpStatusCode.BadRequest, "InvalidRequest");
        Assert.Equal(
            fields.Order(StringComparer.Ordinal),
            problem.GetProperty("errors").EnumerateObject().Select(e => e.Name).Order(StringComparer.Ordinal));
        Assert.Equal(0, api.Store.AppendCalls);
    }

    [Theory]
    [InlineData("2026-09-27T14:00:00")]                        // no offset: ambiguous
    [InlineData("2026-09-27 14:00:00Z")]                       // not ISO 8601
    [InlineData("2026-02-30T14:00:00Z")]                       // no such date
    [InlineData("27/09/2026 14:00:00 +00:00")]
    public async Task A_timestamp_without_an_explicit_offset_gets_400(string observedAt)      // AC-7
    {
        await using var api = new IngestionApi();
        using var client = api.CreateClientWithScope(IngestEndpoint.WritePolicy);

        using var response = await client.PostRawAsync(Url, $$"""{"value":20.0,"observedAt":"{{observedAt}}"}""");

        var problem = await AssertProblemAsync(response, HttpStatusCode.BadRequest, "InvalidRequest");
        Assert.True(problem.GetProperty("errors").TryGetProperty("observedAt", out _));
        Assert.Equal(0, api.Store.AppendCalls);
    }

    [Theory]
    [InlineData("2026-09-27T14:00:00Z")]
    [InlineData("2026-09-27T09:00:00-05:00")]                  // the same instant, another offset
    [InlineData("2026-09-27T14:00:00.1234567+00:00")]
    public async Task A_timestamp_with_an_explicit_offset_is_accepted(string observedAt)      // AC-7
    {
        await using var api = new IngestionApi();
        using var client = api.CreateClientWithScope(IngestEndpoint.WritePolicy);

        using var response = await client.PostRawAsync(Url, $$"""{"value":20.0,"observedAt":"{{observedAt}}"}""");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task An_unknown_sensor_gets_404_and_persists_nothing()                        // AC-12
    {
        await using var api = new IngestionApi();
        using var client = api.CreateClientWithScope(IngestEndpoint.WritePolicy);

        using var response = await client.PostAsJsonAsync(
            new Uri("/sensors/NOPE-01/readings", UriKind.Relative), new { value = 20.0, observedAt = Now }, Ct);

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "SensorNotFound");
        Assert.Equal(0, api.Store.AppendCalls);
    }
}
