using System.Collections.Concurrent;
using Ingestion.Api.Features.Ingest;

namespace Ingestion.Testing;

/// <summary>
/// Store double that honors the idempotency contract and can simulate an outage or a defect.
/// <see cref="AppendCalls"/> counts attempts; <see cref="Rows"/> holds what was persisted.
/// </summary>
public sealed class FakeReadingStore : IReadingStore
{
    private readonly ConcurrentDictionary<(string SensorId, DateTimeOffset ObservedAt), ClassifiedReading> _rows = new();
    private int _appendCalls;

    public bool IsUnavailable { get; set; }

    /// <summary>When set, every append throws it: an unexpected failure, not an outage.</summary>
    public Exception? Fault { get; set; }

    public int AppendCalls => Volatile.Read(ref _appendCalls);
    public IReadOnlyCollection<ClassifiedReading> Rows => [.. _rows.Values];

    public Task AppendAsync(ClassifiedReading reading, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(reading);
        Interlocked.Increment(ref _appendCalls);

        if (Fault is not null)
            throw Fault;
        if (IsUnavailable)
            throw new StoreUnavailableException("Simulated outage.", new TimeoutException());

        _rows.TryAdd((reading.SensorId, reading.ObservedAt), reading);
        return Task.CompletedTask;
    }
}
