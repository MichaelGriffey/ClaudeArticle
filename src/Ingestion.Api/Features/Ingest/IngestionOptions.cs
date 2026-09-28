using System.ComponentModel.DataAnnotations;

namespace Ingestion.Api.Features.Ingest;

/// <summary>
/// Operational settings for the ingest slice, bound from the "Ingestion" section and validated at
/// startup (ADR 0004, ADR 0007). The values live in appsettings.json only; a missing value fails the
/// range check instead of falling back to a second copy in code.
/// </summary>
public sealed class IngestionOptions
{
    public const string SectionName = "Ingestion";

    /// <summary>Retry-After on 503 responses; sent as whole seconds, rounded up (AC-4, AC-9).</summary>
    [Range(typeof(TimeSpan), "00:00:01", "00:10:00")]
    public TimeSpan RetryAfter { get; set; }

    /// <summary>Time the registry and store calls may take together for one request (AC-9).</summary>
    [Range(typeof(TimeSpan), "00:00:00.050", "00:01:00")]
    public TimeSpan DependencyBudget { get; set; }
}
