using System.Collections.Concurrent;
using Ingestion.Api.Features.Ingest;

namespace Ingestion.Api.Infrastructure;

/// <summary>
/// Non-durable store for Development, tests, and staging until a database adapter exists: readings
/// and their audit entries are lost on restart, so it runs only where Storage:Provider opts in
/// (ADR 0004). Honors the <see cref="IReadingStore"/> contract: one atomic write per key, resolved by
/// <see cref="AppendOutcome.Of"/>, with the audit entry in the same row as its reading (ADR 0008).
/// </summary>
public sealed class InMemoryReadingStore : IReadingStore
{
    private readonly ConcurrentDictionary<(string SensorId, DateTimeOffset ObservedAt), Row> _rows = new();

    /// <summary>The audit entries of every inserted reading, in no particular order.</summary>
    public IReadOnlyCollection<AuditEntry> AuditTrail => [.. _rows.Values.Select(row => row.Audit)];

    public Task<AppendOutcome> AppendAsync(ClassifiedReading reading, AuditEntry audit, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(reading);
        ArgumentNullException.ThrowIfNull(audit);

        var stored = _rows.GetOrAdd((reading.SensorId, reading.ObservedAt), new Row(reading, audit));
        return Task.FromResult(AppendOutcome.Of(reading, stored.Reading));
    }

    // One row holds a reading and its audit entry, as one database transaction writes both.
    private sealed record Row(ClassifiedReading Reading, AuditEntry Audit);
}
