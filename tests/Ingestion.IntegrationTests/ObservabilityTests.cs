using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;
using Ingestion.Api.Features.Ingest;
using Ingestion.Api.Security;
using Ingestion.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Ingestion.IntegrationTests.Problems;

namespace Ingestion.IntegrationTests;

/// <summary>ADR 0005: anonymous probes that reveal only a status, and metrics for every answer.</summary>
public sealed class ObservabilityTests
{
    private static readonly Uri Url = new("/sensors/TMP-07/readings", UriKind.Relative);

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Probes_answer_anonymously_with_a_status_word_only(string path)
    {
        await using var api = new IngestionApi();
        using var client = api.CreateClient();

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Accepted_and_rejected_readings_are_counted_by_classification_and_code()
    {
        await using var api = new IngestionApi();
        using var client = api.CreateClientWithScope(ReadingsAuthorization.WriteScope);
        var meters = api.Services.GetRequiredService<IMeterFactory>();
        var measurements = new ConcurrentQueue<(string Instrument, string Tag)>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == IngestTelemetry.MeterName && ReferenceEquals(instrument.Meter.Scope, meters))
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) =>
            measurements.Enqueue((instrument.Name, string.Join(",", tags.ToArray().Select(t => $"{t.Key}={t.Value}")))));
        listener.Start();

        using var accepted = await client.PostAsJsonAsync(Url, new { value = 20.0, observedAt = api.Clock.GetUtcNow() }, Ct);
        using var stale = await client.PostAsJsonAsync(Url, new { value = 20.0, observedAt = api.Clock.GetUtcNow().AddMinutes(-10) }, Ct);

        Assert.Contains(("ingestion.readings.accepted", "classification=Nominal"), measurements);
        Assert.Contains(("ingestion.readings.rejected", "code=StaleReading"), measurements);
    }
}
