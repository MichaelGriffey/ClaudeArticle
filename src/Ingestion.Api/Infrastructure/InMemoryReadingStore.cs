using System.Collections.Concurrent;
using Ingestion.Api.Features.Ingest;

namespace Ingestion.Api.Infrastructure;

/// <summary>
/// Development store. Honors the <see cref="IReadingStore"/> contract: one atomic write per reading,
/// idempotent on (SensorId, ObservedAt). Production uses a database table plus an outbox table.
/// </summary>
public sealed class InMemoryReadingStore : IReadingStore
{
    private readonly ConcurrentDictionary<(string SensorId, DateTimeOffset ObservedAt), ClassifiedReading> _rows = new();

    public int Count => _rows.Count;

    public Task AppendAsync(ClassifiedReading reading, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(reading);
        _rows.TryAdd((reading.SensorId, reading.ObservedAt), reading);
        return Task.CompletedTask;
    }
}
