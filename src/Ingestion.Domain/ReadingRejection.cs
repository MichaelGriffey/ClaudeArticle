namespace Ingestion.Domain;

/// <summary>
/// Why a reading was not classified. The private constructor admits only the nested cases, so the
/// shell maps exactly these three.
/// </summary>
public abstract record ReadingRejection
{
    private ReadingRejection(string code) => Code = code;

    /// <summary>Stable identifier, safe to return to clients and to use in logs and metrics.</summary>
    public string Code { get; }

    /// <summary>NaN or an infinity (AC-2).</summary>
    public sealed record NonFiniteValue() : ReadingRejection(nameof(NonFiniteValue));

    /// <summary>Observed further ahead of server time than the policy allows (AC-5).</summary>
    public sealed record FutureTimestamp(TimeSpan Skew, TimeSpan MaxSkew) : ReadingRejection(nameof(FutureTimestamp));

    /// <summary>Observed longer ago than the policy allows (AC-5).</summary>
    public sealed record StaleReading(TimeSpan Age, TimeSpan MaxAge) : ReadingRejection(nameof(StaleReading));
}
