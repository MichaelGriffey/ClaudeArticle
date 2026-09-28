using Ingestion.Domain;

namespace Ingestion.Api.Features.Ingest;

public sealed record Sensor(string Id, Limits Limits);

public sealed record ClassifiedReading(
    string SensorId, double Value, DateTimeOffset ObservedAt, Classification Classification);

public interface ISensorRegistry
{
    Task<Sensor?> FindAsync(string sensorId, CancellationToken ct);
}

public interface IReadingStore
{
    /// <summary>
    /// Writes the reading, its outbox event, and its <paramref name="audit"/> entry in one
    /// transaction (ADR 0001, ADR 0008), keyed on (SensorId, ObservedAt). The audit entry is written
    /// only when the reading is inserted. Expected outcomes are values, including an outage
    /// (ADR 0004); an exception means a defect. Resolve a write against the stored row with
    /// <see cref="AppendOutcome.Of"/> so every adapter applies the same idempotency rule.
    /// </summary>
    Task<AppendOutcome> AppendAsync(ClassifiedReading reading, AuditEntry audit, CancellationToken ct);
}

/// <summary>What the store did with a reading. Closed: the private constructor admits only the nested cases.</summary>
public abstract record AppendOutcome
{
    private AppendOutcome() { }

    /// <summary>The first reading for this sensor and timestamp.</summary>
    public sealed record Inserted : AppendOutcome;

    /// <summary>The same value was already stored for this sensor and timestamp (AC-3).</summary>
    public sealed record Duplicate(Classification StoredClassification) : AppendOutcome;

    /// <summary>A different value is stored for this sensor and timestamp; nothing changed (AC-8).</summary>
    public sealed record Conflict : AppendOutcome;

    /// <summary>The store could not be reached; nothing was written (AC-4).</summary>
    public sealed record Unavailable : AppendOutcome;

    /// <summary>
    /// The outcome of writing <paramref name="attempted"/> when <paramref name="stored"/> is the row
    /// that holds its key afterwards (the attempted row itself when the write won).
    /// </summary>
    public static AppendOutcome Of(ClassifiedReading attempted, ClassifiedReading stored)
    {
        ArgumentNullException.ThrowIfNull(attempted);
        ArgumentNullException.ThrowIfNull(stored);

        if (ReferenceEquals(attempted, stored))
            return new Inserted();
        return attempted.Value.Equals(stored.Value) ? new Duplicate(stored.Classification) : new Conflict();
    }
}
