using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Ingestion.Api.Features.Ingest;
using Ingestion.Api.Security;
using Ingestion.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Ingestion.AcceptanceTests.Support;

/// <summary>
/// Drives the API for one scenario. With INGESTION_BASE_URL set it targets a deployed
/// environment (bearer token from INGESTION_ACCESS_TOKEN); otherwise it hosts the API in memory.
/// Reqnroll creates one instance per scenario and disposes it afterwards.
/// </summary>
public sealed class IngestionDriver : IDisposable
{
    /// <summary>
    /// How far in the past a reading is stamped when its time is not under test: well inside the
    /// freshness window whatever the agent's clock skew and the network delay (ADR 0006).
    /// </summary>
    private static readonly TimeSpan SafeAge = TimeSpan.FromSeconds(30);

    private readonly IngestionApi? _host;
    private readonly HttpClient _client;

    public IngestionDriver()
    {
        var baseUrl = Environment.GetEnvironmentVariable("INGESTION_BASE_URL");
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            _host = new IngestionApi();
            _client = _host.CreateClientWithScope(ReadingsAuthorization.WriteScope);
            return;
        }

        _client = new HttpClient { BaseAddress = new Uri(baseUrl) };
        var token = Environment.GetEnvironmentVariable("INGESTION_ACCESS_TOKEN");
        if (!string.IsNullOrWhiteSpace(token))
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public HttpStatusCode LastStatus { get; private set; }
    public JsonElement LastBody { get; private set; }
    public TimeSpan? LastRetryAfter { get; private set; }
    public DateTimeOffset LastObservedAt { get; private set; }

    public bool IsInProcess => _host is not null;
    public DateTimeOffset Now => _host?.Clock.GetUtcNow() ?? DateTimeOffset.UtcNow;

    /// <summary>A timestamp for scenarios that do not test time.</summary>
    public DateTimeOffset SafeNow => Now - SafeAge;

    /// <summary>The in-memory store. Scenarios that need it are tagged @in-process.</summary>
    public FakeReadingStore Store => InProcessHost.Store;

    /// <summary>Moves the in-process clock by the configured dependency budget (AC-9).</summary>
    public void ExhaustDependencyBudget()
    {
        var host = InProcessHost;
        host.Clock.Advance(host.Services.GetRequiredService<IOptions<IngestionOptions>>().Value.DependencyBudget);
    }

    public static string Iso(DateTimeOffset instant) => instant.ToString("O", CultureInfo.InvariantCulture);

    /// <summary>Posts a reading. <paramref name="rawValue"/> is sent as a JSON number when finite, else as a string.</summary>
    public Task PostReadingAsync(string sensorId, string rawValue, DateTimeOffset observedAt, CancellationToken ct)
    {
        var number = double.Parse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture);
        var valueJson = double.IsFinite(number)
            ? number.ToString("R", CultureInfo.InvariantCulture)
            : JsonSerializer.Serialize(rawValue);

        LastObservedAt = observedAt;
        return PostAsync(sensorId, $$"""{"value":{{valueJson}},"observedAt":"{{Iso(observedAt)}}"}""", ct);
    }

    /// <summary>Posts a body exactly as given, for requests that never reach the domain (AC-6, AC-7).</summary>
    public async Task PostAsync(string sensorId, string json, CancellationToken ct)
    {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var url = new Uri($"/sensors/{Uri.EscapeDataString(sensorId)}/readings", UriKind.Relative);
        using var response = await _client.PostAsync(url, content, ct);

        LastStatus = response.StatusCode;
        LastRetryAfter = response.Headers.RetryAfter?.Delta;
        var body = await response.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(body))
        {
            LastBody = default;
            return;
        }

        using var document = JsonDocument.Parse(body);
        LastBody = document.RootElement.Clone();
    }

    public void Dispose()
    {
        _client.Dispose();
        _host?.Dispose();
    }

    private IngestionApi InProcessHost =>
        _host ?? throw new InvalidOperationException("This step needs the in-process host (tag the scenario @in-process).");
}
