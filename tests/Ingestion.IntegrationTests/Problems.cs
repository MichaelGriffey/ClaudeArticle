using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Ingestion.IntegrationTests;

/// <summary>Request and response helpers shared by the endpoint suites.</summary>
internal static class Problems
{
    public static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static async Task<HttpResponseMessage> PostRawAsync(this HttpClient client, Uri url, string json)
    {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        return await client.PostAsync(url, content, Ct);
    }

    public static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return document.RootElement.Clone();
    }

    /// <summary>
    /// Asserts an RFC 9457 body (ADR 0002). With <paramref name="code"/>, also asserts the fields the
    /// service adds to errors it decides: <c>code</c>, a <c>type</c> URL, and <c>title</c> equal to the code.
    /// </summary>
    public static async Task<JsonElement> AssertProblemAsync(
        HttpResponseMessage response, HttpStatusCode status, string? code = null)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var body = await ReadJsonAsync(response);
        Assert.Equal((int)status, body.GetProperty("status").GetInt32());
        Assert.True(body.TryGetProperty("traceId", out _), "Problem details carry a traceId.");

        if (code is not null)
        {
            Assert.Equal(code, body.GetProperty("code").GetString());
            Assert.Equal(code, body.GetProperty("title").GetString());
            Assert.StartsWith("https://docs.contoso.com/ingestion/errors/", body.GetProperty("type").GetString(), StringComparison.Ordinal);
        }

        return body;
    }
}
