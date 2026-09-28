using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Ingestion.Api.Security;
using Ingestion.Testing;

namespace Ingestion.AcceptanceTests.Support;

/// <summary>
/// Drives the API for one scenario. With INGESTION_BASE_URL set it targets a deployed
/// environment (bearer token from INGESTION_ACCESS_TOKEN); otherwise it hosts the API in memory.
/// Reqnroll creates one instance per scenario and disposes it afterwards.
/// </summary>
public sealed class IngestionDriver : IDisposable
{
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

    /// <summary>The in-memory store. Scenarios that need it are tagged @in-process.</summary>
    public FakeReadingStore Store =>
        _host?.Store ?? throw new InvalidOperationException("This step needs the in-process host (tag the scenario @in-process).");

    /// <summary>Posts a reading. <paramref name="rawValue"/> is sent as a JSON number when finite, else as a string.</summary>
    public async Task PostReadingAsync(string sensorId, string rawValue, DateTimeOffset observedAt, CancellationToken ct)
    {
        var number = double.Parse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture);
        var valueJson = double.IsFinite(number)
            ? number.ToString("R", CultureInfo.InvariantCulture)
            : JsonSerializer.Serialize(rawValue);
        var json = $$"""{"value":{{valueJson}},"observedAt":"{{observedAt.ToString("O", CultureInfo.InvariantCulture)}}"}""";

        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var url = new Uri($"/sensors/{Uri.EscapeDataString(sensorId)}/readings", UriKind.Relative);
        using var response = await _client.PostAsync(url, content, ct);

        LastObservedAt = observedAt;
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
}
