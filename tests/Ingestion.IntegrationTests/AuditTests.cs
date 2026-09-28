using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Ingestion.Api.Features.Ingest;
using Ingestion.Api.Security;
using Ingestion.Testing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Ingestion.IntegrationTests.Problems;

namespace Ingestion.IntegrationTests;

/// <summary>ADR 0008 and AC-13: one audit record per accepted reading, IDs only, none for anything else.</summary>
public sealed class AuditTests
{
    private static readonly Uri Url = new("/sensors/TMP-07/readings", UriKind.Relative);
    private static readonly DateTimeOffset Now = IngestionApi.DefaultStart;
    private const string TraceId = "4bf92f3577b34da6a3ce929d0e0e4736";

    [Fact]
    public async Task An_accepted_reading_writes_one_audit_record_naming_the_caller()          // AC-13
    {
        await using var api = new IngestionApi();
        using var client = api.CreateClientWithScope(ReadingsAuthorization.WriteScope);
        client.DefaultRequestHeaders.Add("traceparent", $"00-{TraceId}-00f067aa0ba902b7-01");
        var observedAt = Now.AddSeconds(-10);

        using var response = await client.PostAsJsonAsync(Url, new { value = 20.0, observedAt }, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var audit = Assert.Single(api.Store.AuditTrail);
        Assert.Equal(AuditEntry.ReadingAccepted, audit.Action);
        Assert.Equal(TestAuthHandler.SubjectId, audit.SubjectId);
        Assert.Equal(TestAuthHandler.ClientAppId, audit.ClientAppId);
        Assert.Equal(TestAuthHandler.TenantId, audit.TenantId);
        Assert.Equal("TMP-07", audit.SensorId);
        Assert.Equal(observedAt, audit.ObservedAt);
        Assert.Equal(api.Clock.GetUtcNow(), audit.RecordedAt);                 // server time, not the reading's
        Assert.Equal(TraceId, audit.CorrelationId);
    }

    [Fact]
    public async Task A_service_caller_is_named_by_its_ids()                                  // AC-13
    {
        await using var api = new IngestionApi();
        using var client = api.CreateClientWithAppRoles(ReadingsAuthorization.WriteAppRole);

        using var response = await client.PostAsJsonAsync(Url, new { value = 20.0, observedAt = Now }, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var audit = Assert.Single(api.Store.AuditTrail);
        Assert.Equal((TestAuthHandler.SubjectId, TestAuthHandler.ClientAppId), (audit.SubjectId, audit.ClientAppId));
    }

    [Fact]
    public async Task Repeats_conflicts_and_rejections_write_no_audit_record()               // AC-13
    {
        await using var api = new IngestionApi();
        using var client = api.CreateClientWithScope(ReadingsAuthorization.WriteScope);

        using var accepted = await client.PostAsJsonAsync(Url, new { value = 20.0, observedAt = Now }, Ct);
        using var repeat = await client.PostAsJsonAsync(Url, new { value = 20.0, observedAt = Now }, Ct);
        using var conflict = await client.PostAsJsonAsync(Url, new { value = 21.0, observedAt = Now }, Ct);
        using var stale = await client.PostAsJsonAsync(Url, new { value = 20.0, observedAt = Now.AddMinutes(-10) }, Ct);
        using var invalid = await client.PostRawAsync(Url, """{"value":20.0}""");

        Assert.Equal(
            [HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.Conflict, HttpStatusCode.UnprocessableEntity, HttpStatusCode.BadRequest],
            new[] { accepted, repeat, conflict, stale, invalid }.Select(r => r.StatusCode));
        Assert.Single(api.Store.AuditTrail);
    }

    [Fact]
    public async Task An_outage_writes_no_audit_record()                                     // AC-13
    {
        await using var api = new IngestionApi();
        api.Store.IsUnavailable = true;
        using var client = api.CreateClientWithScope(ReadingsAuthorization.WriteScope);

        using var response = await client.PostAsJsonAsync(Url, new { value = 20.0, observedAt = Now }, Ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Empty(api.Store.AuditTrail);
    }

    [Fact]
    public async Task A_caller_the_audit_trail_cannot_name_is_refused()                     // ADR 0008
    {
        await using var api = new IngestionApi();
        var authorization = api.Services.GetRequiredService<IAuthorizationService>();
        var withSubject = Caller(new Claim("scp", ReadingsAuthorization.WriteScope), new Claim("sub", "someone"));
        var withoutSubject = Caller(new Claim("scp", ReadingsAuthorization.WriteScope));

        Assert.True((await authorization.AuthorizeAsync(withSubject, ReadingsAuthorization.WritePolicy)).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(withoutSubject, ReadingsAuthorization.WritePolicy)).Succeeded);
    }

    [Fact]
    public void The_audit_id_is_derived_from_the_readings_key()                             // ADR 0008: dedupe on redelivery
    {
        var at = new DateTimeOffset(2026, 9, 27, 14, 0, 0, TimeSpan.Zero);
        var original = Entry("TMP-07", at, subject: "a", recordedAt: at);

        Assert.Equal(original.Id, Entry("TMP-07", at, subject: "b", recordedAt: at.AddSeconds(5)).Id);
        Assert.Equal(original.Id, Entry("TMP-07", at.ToOffset(TimeSpan.FromHours(-5)), subject: "a", recordedAt: at).Id);
        Assert.NotEqual(original.Id, Entry("TMP-08", at, subject: "a", recordedAt: at).Id);
        Assert.NotEqual(original.Id, Entry("TMP-07", at.AddTicks(1), subject: "a", recordedAt: at).Id);
    }

    private static ClaimsPrincipal Caller(params Claim[] claims) => new(new ClaimsIdentity(claims, "Test"));

    private static AuditEntry Entry(string sensorId, DateTimeOffset observedAt, string subject, DateTimeOffset recordedAt) =>
        AuditEntry.ForAcceptedReading(Caller(new Claim("sub", subject)), sensorId, observedAt, recordedAt, TraceId);
}
