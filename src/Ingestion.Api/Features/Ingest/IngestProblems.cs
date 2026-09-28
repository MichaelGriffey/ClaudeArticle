using System.Diagnostics;
using System.Globalization;
using Ingestion.Domain;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Ingestion.Api.Features.Ingest;

/// <summary>
/// Every error this slice decides, as RFC 9457 problem details (ADR 0002): a stable <c>code</c>, a
/// <c>type</c> URL per code, a human <c>detail</c>, and the numbers behind a rejection. <c>title</c>
/// repeats the code for clients written before <c>code</c> existed. Nothing here echoes request values.
/// </summary>
internal static class IngestProblems
{
    private const string TypeBase = "https://docs.contoso.com/ingestion/errors/";   // placeholder until the docs site exists

    public static ValidationProblem InvalidRequest(Dictionary<string, string[]> errors) =>
        TypedResults.ValidationProblem(
            errors,
            title: "InvalidRequest",
            type: TypeBase + "invalid-request",
            extensions: new Dictionary<string, object?> { ["code"] = "InvalidRequest" });

    public static ProblemHttpResult SensorNotFound() =>
        Problem(StatusCodes.Status404NotFound, "SensorNotFound", "sensor-not-found",
            "No sensor with this ID is registered.");

    public static ProblemHttpResult Rejected(ReadingRejection rejection) => rejection switch
    {
        ReadingRejection.NonFiniteValue => Problem(
            StatusCodes.Status422UnprocessableEntity, rejection.Code, "non-finite-value",
            "The value must be a finite number."),

        ReadingRejection.StaleReading stale => Problem(
            StatusCodes.Status422UnprocessableEntity, rejection.Code, "stale-reading",
            $"The reading is {Seconds(stale.Age)} s old; the limit is {Seconds(stale.MaxAge)} s.",
            ("ageSeconds", stale.Age.TotalSeconds), ("maxAgeSeconds", stale.MaxAge.TotalSeconds)),

        ReadingRejection.FutureTimestamp future => Problem(
            StatusCodes.Status422UnprocessableEntity, rejection.Code, "future-timestamp",
            $"The reading is {Seconds(future.Skew)} s ahead of server time; the limit is {Seconds(future.MaxSkew)} s.",
            ("skewSeconds", future.Skew.TotalSeconds), ("maxSkewSeconds", future.MaxSkew.TotalSeconds)),

        _ => throw new UnreachableException($"Unmapped rejection '{rejection.Code}'."),
    };

    public static ProblemHttpResult ConflictingReading() =>
        Problem(StatusCodes.Status409Conflict, "ConflictingReading", "conflicting-reading",
            "A different value is already stored for this sensor and timestamp.");

    public static ProblemHttpResult StoreUnavailable(HttpResponse response, TimeSpan retryAfter)
    {
        SetRetryAfter(response, retryAfter);
        return Problem(StatusCodes.Status503ServiceUnavailable, "EventStoreUnavailable", "event-store-unavailable",
            "The reading store is unavailable. Retry after the interval in the Retry-After header.");
    }

    public static ProblemHttpResult DependencyTimeout(HttpResponse response, TimeSpan retryAfter)
    {
        SetRetryAfter(response, retryAfter);
        return Problem(StatusCodes.Status503ServiceUnavailable, "DependencyTimeout", "dependency-timeout",
            "A dependency did not answer within the time budget. Retry after the interval in the Retry-After header.");
    }

    private static void SetRetryAfter(HttpResponse response, TimeSpan retryAfter) =>
        response.Headers.RetryAfter = Math.Ceiling(retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);

    private static ProblemHttpResult Problem(
        int status, string code, string slug, string detail, params (string Name, object Value)[] data)
    {
        var extensions = new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = code };
        foreach (var (name, value) in data)
            extensions[name] = value;

        return TypedResults.Problem(detail, statusCode: status, title: code, type: TypeBase + slug, extensions: extensions);
    }

    private static string Seconds(TimeSpan span) => span.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);
}
