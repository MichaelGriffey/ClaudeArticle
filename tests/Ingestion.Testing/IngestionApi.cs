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
        // A bearer configuration that passes the startup check; requests use TestAuthHandler instead.
        [AuthorityKey] = "https://login.test.invalid/tenant/v2.0",
        [AudienceKey] = "api://ingestion-tests",
    };

    public const string AuthorityKey = "Authentication:Schemes:Bearer:Authority";
    public const string AudienceKey = "Authentication:Schemes:Bearer:ValidAudiences:0";

    /// <summary>A client whose requests carry the given delegated scopes (space-separated, like Entra's scp).</summary>
    public HttpClient CreateClientWithScope(string scopes)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.ScopeHeader, scopes);
        return client;
    }

    /// <summary>A client whose requests carry the given app roles, as a service's token would.</summary>
    public HttpClient CreateClientWithAppRoles(params string[] roles)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.AppRolesHeader, string.Join(',', roles));
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
