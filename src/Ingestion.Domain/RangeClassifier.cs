namespace Ingestion.Domain;

/// <summary>Ordered from lowest to highest, so a higher reading never gets a lower classification.</summary>
public enum Classification { Low, Nominal, High }

/// <summary>A sensor value as observed. Not validated: deciding whether it is acceptable is the classifier's job.</summary>
public readonly record struct Reading(double Value, DateTimeOffset ObservedAt);

public static class RangeClassifier
{
    // Pure: same inputs, same output. No clock, no I/O, no expected-failure exceptions.
    public static Result<Classification, ReadingRejection> Classify(
        Reading reading, Limits limits, FreshnessPolicy policy, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(limits);   // a null here is a defect, not an input
        ArgumentNullException.ThrowIfNull(policy);

        // Value before time (AC-11).
        if (!double.IsFinite(reading.Value))
            return new ReadingRejection.NonFiniteValue();                                     // AC-2

        var age = now - reading.ObservedAt;
        if (age < -policy.MaxSkew) return new ReadingRejection.FutureTimestamp(-age, policy.MaxSkew);  // AC-5
        if (age > policy.MaxAge) return new ReadingRejection.StaleReading(age, policy.MaxAge);         // AC-5

        // Limits are inclusive (AC-1).
        return reading.Value < limits.Lower ? Classification.Low
             : reading.Value > limits.Upper ? Classification.High
             : Classification.Nominal;
    }
}
