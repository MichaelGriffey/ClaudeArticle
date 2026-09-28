using Ingestion.Api.Features.Ingest;

namespace Ingestion.Api.Infrastructure;

/// <summary>
/// Chooses the reading store from <c>Storage:Provider</c> (ADR 0004). No durable store exists yet, so
/// a missing setting stops startup instead of silently keeping acknowledged readings in memory.
/// </summary>
public static class ReadingStorage
{
    public const string ProviderKey = "Storage:Provider";
    public const string InMemory = "InMemory";

    public static IServiceCollection AddReadingStorage(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return configuration[ProviderKey] switch
        {
            InMemory => services.AddSingleton<IReadingStore, InMemoryReadingStore>(),
            null or "" => throw new InvalidOperationException(
                $"{ProviderKey} is not set. No durable reading store exists yet; set it to '{InMemory}' only " +
                "where losing readings on restart is acceptable (Development, tests, staging)."),
            var unknown => throw new InvalidOperationException(
                $"{ProviderKey} '{unknown}' is not a known reading store. Known: '{InMemory}'."),
        };
    }
}
