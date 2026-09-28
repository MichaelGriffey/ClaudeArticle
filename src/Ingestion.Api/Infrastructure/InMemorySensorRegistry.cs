using Ingestion.Api.Features.Ingest;
using Ingestion.Domain;

namespace Ingestion.Api.Infrastructure;

/// <summary>Configuration shape for one sensor, validated into a <see cref="Sensor"/> at startup.</summary>
public sealed class SensorOptions
{
    public string Id { get; init; } = "";
    public double Lower { get; init; }
    public double Upper { get; init; }
}

/// <summary>Sensor registry for development and tests. Immutable after construction.</summary>
public sealed class InMemorySensorRegistry : ISensorRegistry
{
    private readonly Dictionary<string, Sensor> _sensors;

    public InMemorySensorRegistry(IEnumerable<Sensor> sensors)
    {
        ArgumentNullException.ThrowIfNull(sensors);
        _sensors = sensors.ToDictionary(s => s.Id, StringComparer.Ordinal);
    }

    public Task<Sensor?> FindAsync(string sensorId, CancellationToken ct) =>
        Task.FromResult(_sensors.GetValueOrDefault(sensorId));

    /// <summary>Builds the registry from configuration and fails fast on any invalid entry.</summary>
    public static InMemorySensorRegistry FromConfiguration(IConfiguration section)
    {
        ArgumentNullException.ThrowIfNull(section);
        var options = section.Get<List<SensorOptions>>() ?? [];

        var sensors = options.Select(o =>
        {
            if (string.IsNullOrWhiteSpace(o.Id))
                throw new InvalidOperationException("Every configured sensor needs an Id.");

            var limits = Limits.Create(o.Lower, o.Upper).Match(
                l => l,
                e => throw new InvalidOperationException($"Sensor '{o.Id}' has invalid limits: {e}"));
            return new Sensor(o.Id, limits);
        });

        return new InMemorySensorRegistry(sensors);
    }
}
