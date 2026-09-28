namespace Ingestion.Domain;

/// <summary>Why a validated type could not be created: a configuration defect, never a response to a reading.</summary>
public abstract record ConfigurationError
{
    private ConfigurationError(string code) => Code = code;

    public string Code { get; }

    public sealed record InvalidLimits(double Lower, double Upper) : ConfigurationError(nameof(InvalidLimits));

    public sealed record InvalidPolicy(TimeSpan MaxAge, TimeSpan MaxSkew) : ConfigurationError(nameof(InvalidPolicy));
}

/// <summary>Calibrated limits. The only way to get one is valid: finite and ordered.</summary>
public sealed record Limits
{
    private Limits(double lower, double upper) => (Lower, Upper) = (lower, upper);

    public double Lower { get; }
    public double Upper { get; }

    public static Result<Limits, ConfigurationError> Create(double lower, double upper) =>
        double.IsFinite(lower) && double.IsFinite(upper) && lower <= upper
            ? new Limits(lower, upper)
            : new ConfigurationError.InvalidLimits(lower, upper);
}

/// <summary>How old a reading may be, and how far ahead of our clock it may claim to be.</summary>
public sealed record FreshnessPolicy
{
    private FreshnessPolicy(TimeSpan maxAge, TimeSpan maxSkew) => (MaxAge, MaxSkew) = (maxAge, maxSkew);

    public TimeSpan MaxAge { get; }
    public TimeSpan MaxSkew { get; }

    public static Result<FreshnessPolicy, ConfigurationError> Create(TimeSpan maxAge, TimeSpan maxSkew) =>
        maxAge > TimeSpan.Zero && maxSkew >= TimeSpan.Zero
            ? new FreshnessPolicy(maxAge, maxSkew)
            : new ConfigurationError.InvalidPolicy(maxAge, maxSkew);
}

/// <summary>
/// The freshness window the story requires (AB#1234, AC-5). These are requirements, not settings:
/// changing them means changing the story.
/// </summary>
public static class FreshnessRequirements
{
    public static TimeSpan MaxAge { get; } = TimeSpan.FromMinutes(5);
    public static TimeSpan MaxSkew { get; } = TimeSpan.FromSeconds(2);
}
