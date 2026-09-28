using FsCheck.Xunit;
using Ingestion.Domain;
using Xunit;
using Xunit.Sdk;

namespace Ingestion.UnitTests;

public sealed class RangeClassifierTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 27, 14, 0, 0, TimeSpan.Zero);
    private static readonly Limits Tmp07 = Limits.Create(-40.0, 125.0).ShouldSucceed();
    private static readonly FreshnessPolicy Policy =
        FreshnessPolicy.Create(TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(2)).ShouldSucceed();

    private static Result<Classification, ReadingError> Classify(double value) =>
        RangeClassifier.Classify(value, Tmp07, T0, T0, Policy);

    [Theory]
    [InlineData(-40.0, Classification.Nominal)]        // AC-1: lower limit is inclusive
    [InlineData(125.0, Classification.Nominal)]        // AC-1: upper limit is inclusive
    [InlineData(-40.000001, Classification.Low)]
    [InlineData(125.000001, Classification.High)]
    [InlineData(-0.0, Classification.Nominal)]
    public void Limits_are_inclusive(double value, Classification expected) =>
        Assert.Equal(expected, Classify(value).ShouldSucceed());

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Non_finite_values_are_rejected(double value) =>                  // AC-2
        Assert.IsType<ReadingError.NonFiniteValue>(Classify(value).ShouldFail());

    [Theory]
    [InlineData(-301, typeof(ReadingError.StaleReading))]      // 5 min 1 s old
    [InlineData(-300, null)]                                   // exactly 5 min old: accepted
    [InlineData(2, null)]                                      // exactly 2 s ahead: accepted
    [InlineData(3, typeof(ReadingError.FutureTimestamp))]      // 3 s ahead
    public void Freshness_window_is_inclusive(int offsetSeconds, Type? expectedError)   // AC-5
    {
        var result = RangeClassifier.Classify(20.0, Tmp07, T0.AddSeconds(offsetSeconds), T0, Policy);
        if (expectedError is null) Assert.True(result.IsSuccess);
        else Assert.IsType(expectedError, result.ShouldFail());
    }

    [Property]
    public bool Non_finite_values_are_never_classified(double value) =>
        double.IsFinite(value) || Classify(value).IsError;

    [Property]
    public bool Classification_is_monotonic_in_value(double a, double b)
    {
        if (!double.IsFinite(a) || !double.IsFinite(b)) return true;
        var (low, high) = a <= b ? (a, b) : (b, a);
        return Classify(low).ShouldSucceed() <= Classify(high).ShouldSucceed();
    }
}

internal static class ResultAssertions
{
    public static T ShouldSucceed<T, TError>(this Result<T, TError> result)
        where T : notnull where TError : notnull =>
        result.Match(value => value, error => throw new XunitException($"Expected success, got {error}"));

    public static TError ShouldFail<T, TError>(this Result<T, TError> result)
        where T : notnull where TError : notnull =>
        result.Match(value => throw new XunitException($"Expected failure, got {value}"), error => error);
}
