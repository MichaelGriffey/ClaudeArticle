namespace Ingestion.Domain;

public enum Classification { Low, Nominal, High }

public abstract record ReadingError(string Code)
{
    public sealed record NonFiniteValue() : ReadingError("NonFiniteValue");
    public sealed record InvalidLimits(double Lower, double Upper) : ReadingError("InvalidLimits");
    public sealed record InvalidPolicy(TimeSpan MaxAge, TimeSpan MaxSkew) : ReadingError("InvalidPolicy");
    public sealed record FutureTimestamp(TimeSpan Skew) : ReadingError("FutureTimestamp");
    public sealed record StaleReading(TimeSpan Age) : ReadingError("StaleReading");
}

/// <summary>Calibrated limits. The only way to get one is valid: finite and ordered.</summary>
public sealed record Limits
{
    private Limits(double lower, double upper) => (Lower, Upper) = (lower, upper);

    public double Lower { get; }
    public double Upper { get; }

    public static Result<Limits, ReadingError> Create(double lower, double upper) =>
        double.IsFinite(lower) && double.IsFinite(upper) && lower <= upper
            ? new Limits(lower, upper)
            : new ReadingError.InvalidLimits(lower, upper);
}

/// <summary>How old a reading may be, and how far ahead of our clock it may claim to be.</summary>
public sealed record FreshnessPolicy
{
    private FreshnessPolicy(TimeSpan maxAge, TimeSpan maxSkew) => (MaxAge, MaxSkew) = (maxAge, maxSkew);

    public TimeSpan MaxAge { get; }
    public TimeSpan MaxSkew { get; }

    public static Result<FreshnessPolicy, ReadingError> Create(TimeSpan maxAge, TimeSpan maxSkew) =>
        maxAge > TimeSpan.Zero && maxSkew >= TimeSpan.Zero
            ? new FreshnessPolicy(maxAge, maxSkew)
            : new ReadingError.InvalidPolicy(maxAge, maxSkew);
}

public static class RangeClassifier
{
    // Pure: same inputs, same output. No clock, no I/O, no expected-failure exceptions.
    public static Result<Classification, ReadingError> Classify(
        double value, Limits limits, DateTimeOffset observedAt,
        DateTimeOffset now, FreshnessPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(limits);   // a null here is a defect, not an input
        ArgumentNullException.ThrowIfNull(policy);

        if (!double.IsFinite(value))
            return new ReadingError.NonFiniteValue();                  // AC-2

        var age = now - observedAt;
        if (age < -policy.MaxSkew) return new ReadingError.FutureTimestamp(-age);  // AC-5
        if (age > policy.MaxAge) return new ReadingError.StaleReading(age);      // AC-5

        // Limits are inclusive (AC-1).
        return value < limits.Lower ? Classification.Low
             : value > limits.Upper ? Classification.High
             : Classification.Nominal;
    }
}
