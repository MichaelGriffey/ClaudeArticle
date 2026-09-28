using FsCheck.Xunit;
using Ingestion.Domain;
using Xunit;
using Xunit.Sdk;

namespace Ingestion.UnitTests;

public sealed class RangeClassifierTests
{
    // The story's numbers, restated rather than read from FreshnessRequirements (ADR 0007).
    private static readonly DateTimeOffset T0 = new(2026, 9, 27, 14, 0, 0, TimeSpan.Zero);
    private static readonly Limits Tmp07 = Limits.Create(-40.0, 125.0).ShouldSucceed();
    private static readonly FreshnessPolicy Policy =
        FreshnessPolicy.Create(TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(2)).ShouldSucceed();

    private static Result<Classification, ReadingRejection> Classify(double value, int offsetSeconds = 0) =>
        RangeClassifier.Classify(new Reading(value, T0.AddSeconds(offsetSeconds)), Tmp07, Policy, now: T0);

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
        Assert.IsType<ReadingRejection.NonFiniteValue>(Classify(value).ShouldFail());

    [Theory]
    [InlineData(-301, typeof(ReadingRejection.StaleReading))]      // 5 min 1 s old
    [InlineData(-300, null)]                                       // exactly 5 min old: accepted
    [InlineData(2, null)]                                          // exactly 2 s ahead: accepted
    [InlineData(3, typeof(ReadingRejection.FutureTimestamp))]      // 3 s ahead
    public void Freshness_window_is_inclusive(int offsetSeconds, Type? expectedRejection)   // AC-5
    {
        var result = Classify(20.0, offsetSeconds);
        if (expectedRejection is null) Assert.True(result.IsSuccess);
        else Assert.IsType(expectedRejection, result.ShouldFail());
    }

    [Fact]
    public void A_stale_rejection_reports_the_age_and_the_limit()               // AC-5
    {
        var rejection = Assert.IsType<ReadingRejection.StaleReading>(Classify(20.0, -301).ShouldFail());
        Assert.Equal(TimeSpan.FromSeconds(301), rejection.Age);
        Assert.Equal(TimeSpan.FromMinutes(5), rejection.MaxAge);
    }

    [Fact]
    public void A_future_rejection_reports_the_skew_and_the_limit()             // AC-5
    {
        var rejection = Assert.IsType<ReadingRejection.FutureTimestamp>(Classify(20.0, 3).ShouldFail());
        Assert.Equal(TimeSpan.FromSeconds(3), rejection.Skew);
        Assert.Equal(TimeSpan.FromSeconds(2), rejection.MaxSkew);
    }

    [Theory]
    [InlineData(double.NaN, -301)]                                 // stale and non-finite
    [InlineData(double.PositiveInfinity, 3)]                       // future and non-finite
    public void Value_checks_come_before_time_checks(double value, int offsetSeconds) =>   // AC-11
        Assert.IsType<ReadingRejection.NonFiniteValue>(Classify(value, offsetSeconds).ShouldFail());

    [Fact]
    public void Freshness_requirements_match_the_story() =>                      // AC-5
        Assert.Equal(
            (TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(2)),
            (FreshnessRequirements.MaxAge, FreshnessRequirements.MaxSkew));

    [Fact]
    public void Rejection_codes_are_stable()                                     // ADR 0002: part of the contract
    {
        Assert.Equal("NonFiniteValue", new ReadingRejection.NonFiniteValue().Code);
        Assert.Equal("FutureTimestamp", new ReadingRejection.FutureTimestamp(TimeSpan.Zero, TimeSpan.Zero).Code);
        Assert.Equal("StaleReading", new ReadingRejection.StaleReading(TimeSpan.Zero, TimeSpan.Zero).Code);
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
