using System.Security.Claims;
using Ingestion.Api.Features.Ingest;
using Ingestion.Api.Infrastructure;
using Ingestion.Domain;
using Xunit;
using static Ingestion.IntegrationTests.Problems;

namespace Ingestion.IntegrationTests;

/// <summary>
/// The adapter against the IReadingStore contract (ADR 0004, ADR 0008): one write per key, outcomes as
/// values, and the audit entry stored with the reading it describes.
/// </summary>
public sealed class InMemoryReadingStoreTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 27, 14, 0, 0, TimeSpan.Zero);

    private static ClassifiedReading Reading(double value, DateTimeOffset observedAt) =>
        new("TMP-07", value, observedAt, value > 125 ? Classification.High : Classification.Nominal);

    private static AuditEntry Audit(ClassifiedReading reading, string subject = "caller") =>
        AuditEntry.ForAcceptedReading(
            new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", subject)], "Test")),
            reading.SensorId, reading.ObservedAt, T0, "trace");

    private static Task<AppendOutcome> AppendAsync(InMemoryReadingStore store, ClassifiedReading reading, string subject = "caller") =>
        store.AppendAsync(reading, Audit(reading, subject), Ct);

    [Fact]
    public async Task The_first_write_inserts_and_a_repeat_is_a_duplicate_with_the_stored_classification()   // AC-3
    {
        var store = new InMemoryReadingStore();

        var first = await AppendAsync(store, Reading(130, T0));
        var repeat = await AppendAsync(store, Reading(130, T0));

        Assert.IsType<AppendOutcome.Inserted>(first);
        Assert.Equal(new AppendOutcome.Duplicate(Classification.High), repeat);
    }

    [Fact]
    public async Task A_different_value_for_the_same_key_is_a_conflict()                     // AC-8
    {
        var store = new InMemoryReadingStore();

        await AppendAsync(store, Reading(20, T0));
        var conflict = await AppendAsync(store, Reading(200, T0));

        Assert.IsType<AppendOutcome.Conflict>(conflict);
    }

    [Fact]
    public async Task The_same_instant_in_another_offset_is_the_same_key()
    {
        var store = new InMemoryReadingStore();

        await AppendAsync(store, Reading(20, T0));
        var repeat = await AppendAsync(store, Reading(20, T0.ToOffset(TimeSpan.FromHours(-5))));

        Assert.IsType<AppendOutcome.Duplicate>(repeat);
    }

    [Fact]
    public async Task Only_the_inserting_write_leaves_an_audit_entry()                        // AC-13
    {
        var store = new InMemoryReadingStore();

        await AppendAsync(store, Reading(20, T0), subject: "first");
        await AppendAsync(store, Reading(20, T0), subject: "repeat");
        await AppendAsync(store, Reading(21, T0), subject: "conflict");

        Assert.Equal("first", Assert.Single(store.AuditTrail).SubjectId);
    }

    [Fact]
    public async Task Concurrent_writes_of_one_key_insert_and_audit_exactly_once()
    {
        var store = new InMemoryReadingStore();

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 64)
            .Select(i => Task.Run(() => AppendAsync(store, Reading(20, T0), subject: $"caller-{i}"), Ct)));

        Assert.Single(outcomes, o => o is AppendOutcome.Inserted);
        Assert.Equal(63, outcomes.Count(o => o is AppendOutcome.Duplicate));
        Assert.Single(store.AuditTrail);
    }
}
