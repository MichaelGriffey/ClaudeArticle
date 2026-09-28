using System.Globalization;
using Ingestion.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Ingestion.Api.Features.Ingest;

public sealed record Sensor(string Id, Limits Limits);
public sealed record ClassifiedReading(
    string SensorId, double Value, DateTimeOffset ObservedAt, Classification Classification);

public interface ISensorRegistry
{
    Task<Sensor?> FindAsync(string sensorId, CancellationToken ct);
}

public interface IReadingStore
{
    /// <summary>Writes the reading and its outbox event in one transaction.
    /// Idempotent on (SensorId, ObservedAt): a duplicate is a no-op.</summary>
    /// <exception cref="StoreUnavailableException">The store cannot be reached.</exception>
    Task AppendAsync(ClassifiedReading reading, CancellationToken ct);
}

public sealed class StoreUnavailableException(string message, Exception innerException)
    : Exception(message, innerException);

public static class IngestEndpoint
{
    public const string WritePolicy = "readings:write";

    public static IEndpointRouteBuilder MapIngestReadings(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var policy = app.ServiceProvider.GetRequiredService<FreshnessPolicy>();

        // 200 and 400 are documented by the handler's return type; the rest are problem details (ADR 0002).
        app.MapPost("/sensors/{sensorId}/readings", HandleAsync)
           .RequireAuthorization(WritePolicy)                     // no anonymous writes
           .WithName("IngestReading")
           .WithTags("Readings")
           .WithSummary("Classify a sensor reading against its calibrated limits and store it.")
           .WithDescription(Describe(policy))
           .ProducesProblem(StatusCodes.Status404NotFound)
           .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
           .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        return app;
    }

    // Imperative shell: validate, load, call the pure core, persist, translate. No rules here.
    private static async Task<Results<Ok<IngestReadingResponse>, ValidationProblem, ProblemHttpResult>> HandleAsync(
        [FromRoute] string sensorId,
        [FromBody] IngestReadingRequest request,
        [FromServices] ISensorRegistry registry,
        [FromServices] IReadingStore store,
        [FromServices] TimeProvider clock,
        [FromServices] FreshnessPolicy policy,
        HttpResponse response,
        CancellationToken ct)
    {
        if (!request.Validate().TryGetValue(out var reading, out var invalid))
            return IngestProblems.InvalidRequest(invalid);                                    // AC-6, AC-7

        var sensor = await registry.FindAsync(sensorId, ct);
        if (sensor is null)
            return IngestProblems.SensorNotFound();                                           // AC-12

        var outcome = RangeClassifier.Classify(reading, sensor.Limits, policy, clock.GetUtcNow());
        if (!outcome.TryGetValue(out var classification, out var rejection))
            return IngestProblems.Rejected(rejection);                                        // AC-2, AC-5, AC-11

        try
        {
            // The registry's ID, not the route's: a case-insensitive registry must not split the idempotency key.
            await store.AppendAsync(
                new ClassifiedReading(sensor.Id, reading.Value, reading.ObservedAt, classification), ct);
            return TypedResults.Ok(new IngestReadingResponse(classification));
        }
        catch (StoreUnavailableException)
        {
            return IngestProblems.StoreUnavailable(response, TimeSpan.FromSeconds(5));       // AC-4
        }
    }

    private static string Describe(FreshnessPolicy policy) =>
        "Limits are inclusive (AC-1). Non-finite values are rejected (AC-2); send them as the strings " +
        "\"NaN\", \"Infinity\", or \"-Infinity\". Repeating a reading (same sensor, timestamp, and value) is " +
        $"idempotent (AC-3). Readings more than {Humanize(policy.MaxAge)} old or {Humanize(policy.MaxSkew)} " +
        "ahead of server time are rejected (AC-5). observedAt must carry 'Z' or a UTC offset (AC-7).";

    private static string Humanize(TimeSpan span) =>
        span.Ticks % TimeSpan.TicksPerMinute == 0 ? Count(span.TotalMinutes, "minute") : Count(span.TotalSeconds, "second");

    private static string Count(double amount, string unit) =>
        string.Create(CultureInfo.InvariantCulture, $"{amount:0.###} {unit}{(amount == 1 ? "" : "s")}");
}
