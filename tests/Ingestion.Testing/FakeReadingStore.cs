using System.Collections.Concurrent;
using Ingestion.Api.Features.Ingest;

namespace Ingestion.Testing;

/// <summary>
/// Store double that honors the idempotency contract (<see cref="AppendOutcome.Of"/>) and can
/// simulate an outage, a hang, or a defect. <see cref="AppendCalls"/> counts attempts;
/// <see cref="Rows"/> holds what was persisted.
/// </summary>
public sealed class FakeReadingStore : IReadingStore
{
    private readonly ConcurrentDictionary<(string SensorId, DateTimeOffset ObservedAt), (ClassifiedReading Reading, AuditEntry Audit)> _rows = new();
    private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _appendCalls;

    /// <summary>Every append reports <see cref="AppendOutcome.Unavailable"/> (AC-4).</summary>
    public bool IsUnavailable { get; set; }

    /// <summary>Every append waits until its token is cancelled, as a hung database would (AC-9).</summary>
    public bool Hangs { get; set; }

    /// <summary>When set, every append throws it: a defect, not an outage.</summary>
    public Exception? Fault { get; set; }

    /// <summary>Completes when the first append starts, so a test can move the clock while it waits.</summary>
    public Task Entered => _entered.Task;

    public int AppendCalls => Volatile.Read(ref _appendCalls);
    public IReadOnlyCollection<ClassifiedReading> Rows => [.. _rows.Values.Select(row => row.Reading)];

    /// <summary>The audit entries of inserted readings; each shares its reading's row (ADR 0008).</summary>
    public IReadOnlyCollection<AuditEntry> AuditTrail => [.. _rows.Values.Select(row => row.Audit)];

    public async Task<AppendOutcome> AppendAsync(ClassifiedReading reading, AuditEntry audit, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(reading);
        ArgumentNullException.ThrowIfNull(audit);
        Interlocked.Increment(ref _appendCalls);
        _entered.TrySetResult();

        if (Fault is not null)
            throw Fault;
        if (Hangs)
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        if (IsUnavailable)
            return new AppendOutcome.Unavailable();

        var stored = _rows.GetOrAdd((reading.SensorId, reading.ObservedAt), (reading, audit));
        return AppendOutcome.Of(reading, stored.Reading);
    }
}
