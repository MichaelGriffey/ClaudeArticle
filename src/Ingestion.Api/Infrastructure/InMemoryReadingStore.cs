using System.Collections.Concurrent;
using Ingestion.Api.Features.Ingest;

namespace Ingestion.Api.Infrastructure;

/// <summary>
/// Non-durable store for Development, tests, and staging until a database adapter exists: readings
/// are lost on restart, so it runs only where Storage:Provider opts in (ADR 0004). Honors the
/// <see cref="IReadingStore"/> contract: one atomic write per key, resolved by <see cref="AppendOutcome.Of"/>.
/// </summary>
public sealed class InMemoryReadingStore : IReadingStore
{
    private readonly ConcurrentDictionary<(string SensorId, DateTimeOffset ObservedAt), ClassifiedReading> _rows = new();

    public Task<AppendOutcome> AppendAsync(ClassifiedReading reading, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(reading);
        var stored = _rows.GetOrAdd((reading.SensorId, reading.ObservedAt), reading);
        return Task.FromResult(AppendOutcome.Of(reading, stored));
    }
}
