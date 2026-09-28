using System.Net;
using System.Text.Json;
using Ingestion.Testing;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Ingestion.IntegrationTests;

/// <summary>The developer surface: OpenAPI document and Swagger UI exist in Development only.</summary>
public sealed class OpenApiTests
{
    private static readonly Uri DocumentUrl = new("/openapi/v1.json", UriKind.Relative);
    private static readonly Uri SwaggerUrl = new("/swagger/index.html", UriKind.Relative);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_document_describes_the_ingest_endpoint_and_its_bearer_security()
    {
        await using var api = new IngestionApi(IngestionApi.DefaultStart, "Development");
        using var client = api.CreateClient();

        using var response = await client.GetAsync(DocumentUrl, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        var root = document.RootElement;

        var scheme = root.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer");
        Assert.Equal("http", scheme.GetProperty("type").GetString());
        Assert.Equal("bearer", scheme.GetProperty("scheme").GetString());

        var post = root.GetProperty("paths").GetProperty("/sensors/{sensorId}/readings").GetProperty("post");
        Assert.Equal("IngestReading", post.GetProperty("operationId").GetString());
        Assert.True(post.GetProperty("security")[0].TryGetProperty("Bearer", out _));

        var responses = post.GetProperty("responses");
        foreach (var status in new[] { "200", "400", "401", "403", "404", "422", "503" })
            Assert.True(responses.TryGetProperty(status, out _), $"Response {status} is not documented.");

        // observedAt is text on the wire (AC-7) but documented as a required date-time.
        var request = root.GetProperty("components").GetProperty("schemas").GetProperty("IngestReadingRequest");
        Assert.Equal("date-time", request.GetProperty("properties").GetProperty("observedAt").GetProperty("format").GetString());
        Assert.Equal(["value", "observedAt"], request.GetProperty("required").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task The_root_redirects_to_swagger_ui_in_development()
    {
        await using var api = new IngestionApi(IngestionApi.DefaultStart, "Development");
        using var client = api.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var root = await client.GetAsync(new Uri("/", UriKind.Relative), Ct);
        using var ui = await client.GetAsync(SwaggerUrl, Ct);

        Assert.Equal(HttpStatusCode.Redirect, root.StatusCode);
        Assert.Equal("/swagger", root.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.OK, ui.StatusCode);
    }

    [Fact]
    public async Task Outside_development_neither_the_document_nor_the_ui_is_exposed()
    {
        await using var api = new IngestionApi();
        using var client = api.CreateClient();

        using var document = await client.GetAsync(DocumentUrl, Ct);
        using var ui = await client.GetAsync(SwaggerUrl, Ct);

        Assert.Equal(HttpStatusCode.NotFound, document.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, ui.StatusCode);
    }
}
