using Ingestion.Api.Features.Ingest;
using Ingestion.Api.Infrastructure;
using Ingestion.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace Ingestion.Testing;

/// <summary>
/// The real API, hosted in memory with deterministic time, a controllable store,
/// and header-based test authentication. One instance per test keeps tests isolated.
/// </summary>
/// <param name="start">The fake clock's starting instant.</param>
/// <param name="environment">Hosting environment; "Development" enables the OpenAPI document and Swagger UI.</param>
public sealed class IngestionApi(DateTimeOffset start, string environment = "Testing") : WebApplicationFactory<Program>
{
    public static readonly DateTimeOffset DefaultStart = new(2026, 9, 27, 14, 0, 0, TimeSpan.Zero);

    private readonly string _environment = environment;

    public IngestionApi() : this(DefaultStart) { }

    public FakeTimeProvider Clock { get; } = new FakeTimeProvider(start);
    public FakeReadingStore Store { get; } = new();

    /// <summary>
    /// Configuration applied before the app starts, as an environment would supply it. Change entries
    /// before the first request; a null value removes the setting.
    /// </summary>
    public Dictionary<string, string?> Settings { get; } = new(StringComparer.Ordinal)
    {
        [ReadingStorage.ProviderKey] = ReadingStorage.InMemory,
    };

    /// <summary>A client whose requests authenticate with the given scope.</summary>
    public HttpClient CreateClientWithScope(string scope)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.ScopeHeader, scope);
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.UseEnvironment(_environment);
        foreach (var (key, value) in Settings)
            builder.UseSetting(key, value);

        builder.ConfigureTestServices(services =>
        {
            services.Replace(ServiceDescriptor.Singleton<TimeProvider>(Clock));
            services.Replace(ServiceDescriptor.Singleton<IReadingStore>(Store));
            services.Replace(ServiceDescriptor.Singleton<ISensorRegistry>(new InMemorySensorRegistry(
            [
                new Sensor("TMP-07", Limits.Create(-40.0, 125.0).OrThrowAtStartup("test limits")),
            ])));

            services.AddAuthentication(o =>
                {
                    o.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                    o.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
        });
    }
}
