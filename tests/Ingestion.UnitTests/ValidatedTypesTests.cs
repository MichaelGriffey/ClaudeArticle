using Ingestion.Domain;
using Xunit;
using Xunit.Sdk;

namespace Ingestion.UnitTests;

/// <summary>
/// Guards the constructors of the validated types and the Result contract. Without these,
/// mutation testing would find the guards in Create and Match unverified.
/// </summary>
public sealed class ValidatedTypesTests
{
    [Theory]
    [InlineData(double.NaN, 1.0)]
    [InlineData(0.0, double.NaN)]
    [InlineData(double.NegativeInfinity, 1.0)]
    [InlineData(0.0, double.PositiveInfinity)]
    [InlineData(2.0, 1.0)]                                     // unordered
    public void Limits_reject_non_finite_or_unordered_bounds(double lower, double upper) =>
        Assert.IsType<ConfigurationError.InvalidLimits>(Limits.Create(lower, upper).ShouldFail());

    [Theory]
    [InlineData(-40.0, 125.0)]
    [InlineData(5.0, 5.0)]                                     // a single allowed value is valid
    public void Limits_accept_finite_ordered_bounds(double lower, double upper)
    {
        var limits = Limits.Create(lower, upper).ShouldSucceed();
        Assert.Equal((lower, upper), (limits.Lower, limits.Upper));
    }

    [Theory]
    [InlineData(0, 0)]                                         // max age must be positive
    [InlineData(-1, 0)]
    [InlineData(300, -1)]                                      // skew cannot be negative
    public void Freshness_policy_rejects_invalid_windows(int maxAgeSeconds, int maxSkewSeconds) =>
        Assert.IsType<ConfigurationError.InvalidPolicy>(
            FreshnessPolicy.Create(TimeSpan.FromSeconds(maxAgeSeconds), TimeSpan.FromSeconds(maxSkewSeconds)).ShouldFail());

    [Fact]
    public void Configuration_error_codes_are_stable()
    {
        Assert.Equal("InvalidLimits", new ConfigurationError.InvalidLimits(0, 0).Code);
        Assert.Equal("InvalidPolicy", new ConfigurationError.InvalidPolicy(TimeSpan.Zero, TimeSpan.Zero).Code);
    }

    [Theory]
    [InlineData(300, 0)]                                       // zero skew is allowed
    [InlineData(1, 2)]
    public void Freshness_policy_accepts_valid_windows(int maxAgeSeconds, int maxSkewSeconds)
    {
        var policy = FreshnessPolicy.Create(TimeSpan.FromSeconds(maxAgeSeconds), TimeSpan.FromSeconds(maxSkewSeconds)).ShouldSucceed();
        Assert.Equal(TimeSpan.FromSeconds(maxAgeSeconds), policy.MaxAge);
        Assert.Equal(TimeSpan.FromSeconds(maxSkewSeconds), policy.MaxSkew);
    }

    [Fact]
    public void Result_exposes_exactly_one_outcome()
    {
        Result<int, string> ok = 42;
        Result<int, string> failed = "boom";

        Assert.True(ok.TryGetValue(out var value, out _));
        Assert.Equal(42, value);
        Assert.False(ok.IsError);

        Assert.False(failed.TryGetValue(out _, out var error));
        Assert.Equal("boom", error);
        Assert.True(failed.IsError);
    }

    [Fact]
    public void Match_rejects_null_handlers()
    {
        Result<int, string> ok = 1;
        Assert.Throws<ArgumentNullException>(() => ok.Match<int>(null!, e => 0));
        Assert.Throws<ArgumentNullException>(() => ok.Match(v => v, null!));
    }

    [Fact]
    public void Classify_rejects_null_arguments_as_defects()
    {
        var limits = Limits.Create(0, 1).ShouldSucceed();
        var policy = FreshnessPolicy.Create(TimeSpan.FromMinutes(1), TimeSpan.Zero).ShouldSucceed();
        var t = DateTimeOffset.UnixEpoch;
        var reading = new Reading(0, t);

        Assert.Throws<ArgumentNullException>(() => RangeClassifier.Classify(reading, null!, policy, t));
        Assert.Throws<ArgumentNullException>(() => RangeClassifier.Classify(reading, limits, null!, t));
    }

    [Fact]
    public void ShouldSucceed_and_ShouldFail_report_the_unexpected_outcome()
    {
        Result<int, string> ok = 1;
        Result<int, string> failed = "boom";
        Assert.Throws<XunitException>(() => ok.ShouldFail());
        Assert.Throws<XunitException>(() => failed.ShouldSucceed());
    }
}
