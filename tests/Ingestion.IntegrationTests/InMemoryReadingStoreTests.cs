using Ingestion.Api.Features.Ingest;
using Ingestion.Api.Infrastructure;
using Ingestion.Domain;
using Xunit;
using static Ingestion.IntegrationTests.Problems;

namespace Ingestion.IntegrationTests;

/// <summary>The adapter against the IReadingStore contract (ADR 0004): one write per key, outcomes as values.</summary>
public sealed class InMemoryReadingStoreTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 27, 14, 0, 0, TimeSpan.Zero);

    private static ClassifiedReading Reading(double value, DateTimeOffset observedAt) =>
        new("TMP-07", value, observedAt, value > 125 ? Classification.High : Classification.Nominal);

    [Fact]
    public async Task The_first_write_inserts_and_a_repeat_is_a_duplicate_with_the_stored_classification()   // AC-3
    {
        var store = new InMemoryReadingStore();

        var first = await store.AppendAsync(Reading(130, T0), Ct);
        var repeat = await store.AppendAsync(Reading(130, T0), Ct);

        Assert.IsType<AppendOutcome.Inserted>(first);
        Assert.Equal(new AppendOutcome.Duplicate(Classification.High), repeat);
    }

    [Fact]
    public async Task A_different_value_for_the_same_key_is_a_conflict()                     // AC-8
    {
        var store = new InMemoryReadingStore();

        await store.AppendAsync(Reading(20, T0), Ct);
        var conflict = await store.AppendAsync(Reading(200, T0), Ct);

        Assert.IsType<AppendOutcome.Conflict>(conflict);
    }

    [Fact]
    public async Task The_same_instant_in_another_offset_is_the_same_key()
    {
        var store = new InMemoryReadingStore();

        await store.AppendAsync(Reading(20, T0), Ct);
        var repeat = await store.AppendAsync(Reading(20, T0.ToOffset(TimeSpan.FromHours(-5))), Ct);

        Assert.IsType<AppendOutcome.Duplicate>(repeat);
    }

    [Fact]
    public async Task Concurrent_writes_of_one_key_insert_exactly_once()
    {
        var store = new InMemoryReadingStore();

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 64)
            .Select(_ => Task.Run(() => store.AppendAsync(Reading(20, T0), Ct), Ct)));

        Assert.Single(outcomes, o => o is AppendOutcome.Inserted);
        Assert.Equal(63, outcomes.Count(o => o is AppendOutcome.Duplicate));
    }
}
