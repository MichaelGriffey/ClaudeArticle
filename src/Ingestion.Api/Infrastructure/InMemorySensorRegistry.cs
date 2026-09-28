using Ingestion.Api.Features.Ingest;
using Ingestion.Domain;

namespace Ingestion.Api.Infrastructure;

/// <summary>
/// Configuration shape for one sensor, validated into a <see cref="Sensor"/> at startup. The limits
/// are nullable so a missing limit is an error rather than a silent 0.
/// </summary>
public sealed class SensorOptions
{
    public string? Id { get; init; }
    public double? Lower { get; init; }
    public double? Upper { get; init; }
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

    /// <summary>
    /// Builds the registry from configuration and stops startup on any entry the service could not
    /// classify against: no sensors, a missing Id or limit, a misspelled key, invalid limits, or a
    /// duplicate Id.
    /// </summary>
    public static InMemorySensorRegistry FromConfiguration(IConfiguration section)
    {
        ArgumentNullException.ThrowIfNull(section);

        // Unknown keys fail binding, so a misspelled "Uper" cannot leave a limit unset.
        var entries = section.Get<List<SensorOptions>>(o => o.ErrorOnUnknownConfiguration = true) ?? [];
        if (entries.Count == 0)
            throw new InvalidOperationException("No sensors are configured under 'Sensors'; every reading would be rejected.");

        var sensors = entries.Select((entry, index) =>
        {
            if (string.IsNullOrWhiteSpace(entry.Id))
                throw new InvalidOperationException($"Sensors:{index} has no Id.");
            if (entry.Lower is not { } lower || entry.Upper is not { } upper)
                throw new InvalidOperationException($"Sensor '{entry.Id}' needs both Lower and Upper.");

            return new Sensor(entry.Id, Limits.Create(lower, upper).OrThrowAtStartup($"limits for sensor '{entry.Id}'"));
        }).ToList();

        var duplicate = sensors.GroupBy(s => s.Id, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException($"Sensor '{duplicate.Key}' is configured more than once.");

        return new InMemorySensorRegistry(sensors);
    }
}
