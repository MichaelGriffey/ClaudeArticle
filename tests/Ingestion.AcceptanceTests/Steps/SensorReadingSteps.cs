using System.Globalization;
using System.Net;
using Ingestion.AcceptanceTests.Support;
using Ingestion.Testing;
using Reqnroll;
using Xunit;

namespace Ingestion.AcceptanceTests.Steps;

[Binding]
public sealed class SensorReadingSteps(IngestionDriver driver)
{
    private const string Sensor = "TMP-07";
    private int _appendsBefore;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Background: the deployed or in-memory configuration must match the story.
    [Given(@"^sensor ""(.*)"" has limits (.*) to (.*) degrees C$")]
    public void GivenSensorLimits(string sensorId, double lower, double upper)
    {
        Assert.Equal(Sensor, sensorId);
        Assert.Equal((-40.0, 125.0), (lower, upper));
    }

    [Given(@"^readings may be at most (\d+) minutes old and (\d+) seconds ahead of server time$")]
    public void GivenFreshnessWindow(int maxAgeMinutes, int maxSkewSeconds) =>
        Assert.Equal((5, 2), (maxAgeMinutes, maxSkewSeconds));

    [Given(@"^a reading for ""(.*)"" was accepted$")]
    public async Task GivenAReadingWasAccepted(string sensorId)
    {
        await driver.PostReadingAsync(sensorId, "20.0", driver.SafeNow, Ct);
        Assert.Equal(HttpStatusCode.OK, driver.LastStatus);
        _appendsBefore = driver.IsInProcess ? driver.Store.Rows.Count : 0;
    }

    [Given(@"^the event store is not reachable$")]
    public void GivenTheStoreIsUnavailable() => driver.Store.IsUnavailable = true;

    [Given(@"^the event store does not answer$")]
    public void GivenTheStoreHangs() => driver.Store.Hangs = true;

    [When(@"^a reading of (.*) is received$")]
    public async Task WhenAReadingIsReceived(string value)
    {
        _appendsBefore = driver.IsInProcess ? driver.Store.Rows.Count : 0;
        await driver.PostReadingAsync(Sensor, value, driver.SafeNow, Ct);
    }

    [When(@"^a valid reading is received$")]
    public Task WhenAValidReadingIsReceived() => driver.PostReadingAsync(Sensor, "20.0", driver.SafeNow, Ct);

    [When(@"^a valid reading is received and the dependency budget runs out$")]
    public async Task WhenTheDependencyBudgetRunsOut()
    {
        var pending = driver.PostReadingAsync(Sensor, "20.0", driver.SafeNow, Ct);
        await driver.Store.Entered.WaitAsync(Ct);
        driver.ExhaustDependencyBudget();
        await pending;
    }

    [When(@"^a reading with the same sensor and timestamp arrives again$")]
    public Task WhenTheSameReadingArrivesAgain() =>
        driver.PostReadingAsync(Sensor, "20.0", driver.LastObservedAt, Ct);

    [When(@"^a different value with the same sensor and timestamp arrives$")]
    public Task WhenADifferentValueArrives() =>
        driver.PostReadingAsync(Sensor, "21.0", driver.LastObservedAt, Ct);

    [When(@"^a reading observed (\d+) minutes? (\d+) seconds? ago is received$")]
    public Task WhenAStaleReadingIsReceived(int minutes, int seconds) =>
        driver.PostReadingAsync(Sensor, "20.0", driver.Now - new TimeSpan(0, minutes, seconds), Ct);

    [When(@"^a reading observed (\d+) seconds? in the future is received$")]
    public Task WhenAFutureReadingIsReceived(int seconds) =>
        driver.PostReadingAsync(Sensor, "20.0", driver.Now.AddSeconds(seconds), Ct);

    [When(@"^a reading observed (\d+) minutes? before it is received$")]
    public Task WhenAnOlderReadingIsReceived(int minutes) =>
        driver.PostReadingAsync(Sensor, "20.0", driver.LastObservedAt.AddMinutes(-minutes), Ct);

    [When(@"^a (\S+) reading observed (\d+) minutes? ago is received$")]
    public Task WhenAReadingObservedAgoIsReceived(string value, int minutes) =>
        driver.PostReadingAsync(Sensor, value, driver.Now.AddMinutes(-minutes), Ct);

    [When(@"^a reading without a value is received$")]
    public Task WhenAReadingWithoutAValueIsReceived() =>
        driver.PostAsync(Sensor, $$"""{"observedAt":"{{IngestionDriver.Iso(driver.SafeNow)}}"}""", Ct);

    [When(@"^a reading stamped without a UTC offset is received$")]
    public Task WhenAReadingWithoutAnOffsetIsReceived()
    {
        var local = driver.SafeNow.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
        return driver.PostAsync(Sensor, $$"""{"value":20.0,"observedAt":"{{local}}"}""", Ct);
    }

    [When(@"^a reading for sensor ""(.*)"" is received$")]
    public Task WhenAReadingForSensorIsReceived(string sensorId) =>
        driver.PostReadingAsync(sensorId, "20.0", driver.SafeNow, Ct);

    [Then(@"^the reading is classified ""(.*)""$")]
    public void ThenTheReadingIsClassified(string classification)
    {
        Assert.Equal(HttpStatusCode.OK, driver.LastStatus);
        Assert.Equal(classification, driver.LastBody.GetProperty("classification").GetString());
    }

    [Then(@"^the reading is rejected with error ""(.*)""$")]
    public void ThenTheReadingIsRejected(string error)
    {
        Assert.Equal(HttpStatusCode.UnprocessableEntity, driver.LastStatus);
        Assert.Equal(error, driver.LastBody.GetProperty("title").GetString());
    }

    [Then(@"^the reading is rejected with status (\d+) and code ""(.*)""$")]
    public void ThenTheReadingIsRejectedWith(int status, string code)
    {
        Assert.Equal((HttpStatusCode)status, driver.LastStatus);
        Assert.Equal(code, driver.LastBody.GetProperty("code").GetString());
    }

    [Then(@"^the request is rejected as invalid, naming ""(.*)""$")]
    public void ThenTheRequestIsInvalid(string field)
    {
        Assert.Equal(HttpStatusCode.BadRequest, driver.LastStatus);
        Assert.Equal("InvalidRequest", driver.LastBody.GetProperty("code").GetString());
        Assert.True(driver.LastBody.GetProperty("errors").TryGetProperty(field, out _), $"No error names '{field}'.");
    }

    [Then(@"^no classification event is published$")]
    [Then(@"^no second event is published$")]
    public void ThenNothingNewIsPersisted() => Assert.Equal(_appendsBefore, driver.Store.Rows.Count);

    [Then(@"^the API returns (\d+) with a Retry-After header$")]
    public void ThenTheApiReturnsWithRetryAfter(int status)
    {
        Assert.Equal((HttpStatusCode)status, driver.LastStatus);
        Assert.NotNull(driver.LastRetryAfter);
    }

    [Then(@"^the error code is ""(.*)""$")]
    public void ThenTheErrorCodeIs(string code) => Assert.Equal(code, driver.LastBody.GetProperty("code").GetString());

    [Then(@"^the reading is not partially persisted$")]
    public void ThenNothingIsPersisted() => Assert.Empty(driver.Store.Rows);

    [Then(@"^exactly one audit record names the caller and the reading$")]
    public void ThenOneAuditRecordNamesTheCaller()
    {
        var audit = Assert.Single(driver.Store.AuditTrail);
        Assert.Equal(TestAuthHandler.SubjectId, audit.SubjectId);
        Assert.Equal((Sensor, driver.LastObservedAt), (audit.SensorId, audit.ObservedAt));
        Assert.False(string.IsNullOrEmpty(audit.CorrelationId));
    }
}
