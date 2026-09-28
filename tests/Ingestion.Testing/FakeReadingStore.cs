using System.Collections.Concurrent;
using Ingestion.Api.Features.Ingest;

namespace Ingestion.Testing;

/// <summary>
/// Store double that honors the idempotency contract and can simulate an outage.
/// <see cref="AppendCalls"/> counts attempts; <see cref="Rows"/> holds what was persisted.
/// </summary>
public sealed class FakeReadingStore : IReadingStore
{
    private readonly ConcurrentDictionary<(string SensorId, DateTimeOffset ObservedAt), ClassifiedReading> _rows = new();
    private int _appendCalls;

    public bool IsUnavailable { get; set; }
    public int AppendCalls => Volatile.Read(ref _appendCalls);
    public IReadOnlyCollection<ClassifiedReading> Rows => [.. _rows.Values];

    public Task AppendAsync(ClassifiedReading reading, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(reading);
        Interlocked.Increment(ref _appendCalls);

        if (IsUnavailable)
            throw new StoreUnavailableException("Simulated outage.", new TimeoutException());

        _rows.TryAdd((reading.SensorId, reading.ObservedAt), reading);
        return Task.CompletedTask;
    }
}
