using Ingestion.Api.Features.Ingest;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Ingestion.Api.Infrastructure;

/// <summary>
/// Chooses the reading store from <c>Storage:Provider</c> (ADR 0004). No durable store exists yet, so
/// a missing setting stops startup instead of silently keeping acknowledged readings in memory.
/// </summary>
public static class ReadingStorage
{
    public const string ProviderKey = "Storage:Provider";
    public const string InMemory = "InMemory";

    /// <summary>Health checks with this tag decide readiness (ADR 0005); each store registers its own.</summary>
    public const string ReadyTag = "ready";

    public static IServiceCollection AddReadingStorage(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return configuration[ProviderKey] switch
        {
            InMemory => services
                .AddSingleton<IReadingStore, InMemoryReadingStore>()
                .AddHealthChecks().AddCheck("reading-store", () => HealthCheckResult.Healthy(), [ReadyTag]).Services,
            null or "" => throw new InvalidOperationException(
                $"{ProviderKey} is not set. No durable reading store exists yet; set it to '{InMemory}' only " +
                "where losing readings on restart is acceptable (Development, tests, staging)."),
            var unknown => throw new InvalidOperationException(
                $"{ProviderKey} '{unknown}' is not a known reading store. Known: '{InMemory}'."),
        };
    }
}
