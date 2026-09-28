using Ingestion.Api.Infrastructure;
using Microsoft.Extensions.Configuration;
using Xunit;
using static Ingestion.IntegrationTests.Problems;

namespace Ingestion.IntegrationTests;

/// <summary>The sensor registry refuses configuration it could not classify against.</summary>
public sealed class SensorConfigurationTests
{
    /// <summary>A "Sensors" section from "index:Key=value" entries.</summary>
    private static IConfiguration Sensors(params string[] entries) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(entries
                .Select(entry => entry.Split('=', 2))
                .Select(pair => KeyValuePair.Create("Sensors:" + pair[0], (string?)pair[1])))
            .Build()
            .GetSection("Sensors");

    [Fact]
    public async Task A_valid_entry_becomes_a_sensor()
    {
        var registry = InMemorySensorRegistry.FromConfiguration(Sensors("0:Id=TMP-07", "0:Lower=-40", "0:Upper=125"));

        var sensor = await registry.FindAsync("TMP-07", Ct);

        Assert.NotNull(sensor);
        Assert.Equal((-40.0, 125.0), (sensor.Limits.Lower, sensor.Limits.Upper));
    }

    [Theory]
    [InlineData("needs both Lower and Upper", "0:Id=TMP-07", "0:Lower=-40")]
    [InlineData("needs both Lower and Upper", "0:Id=TMP-07", "0:Upper=125")]
    [InlineData("has no Id", "0:Lower=-40", "0:Upper=125")]
    [InlineData("Invalid limits for sensor 'TMP-07'", "0:Id=TMP-07", "0:Lower=125", "0:Upper=-40")]
    [InlineData("configured more than once", "0:Id=TMP-07", "0:Lower=0", "0:Upper=1", "1:Id=TMP-07", "1:Lower=0", "1:Upper=1")]
    public void An_entry_the_service_cannot_classify_against_stops_startup(string expected, params string[] entries)
    {
        var error = Assert.Throws<InvalidOperationException>(() => InMemorySensorRegistry.FromConfiguration(Sensors(entries)));
        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_misspelled_key_stops_startup() =>
        Assert.Throws<InvalidOperationException>(() =>
            InMemorySensorRegistry.FromConfiguration(Sensors("0:Id=TMP-07", "0:Lower=-40", "0:Uper=125")));

    [Fact]
    public void No_sensors_stops_startup()
    {
        var error = Assert.Throws<InvalidOperationException>(() => InMemorySensorRegistry.FromConfiguration(Sensors()));
        Assert.Contains("No sensors are configured", error.Message, StringComparison.Ordinal);
    }
}
