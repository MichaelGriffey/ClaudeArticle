using System.Diagnostics;
using Ingestion.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Ingestion.Api.Features.Ingest;

public sealed record IngestReadingRequest(double Value, DateTimeOffset ObservedAt);
public sealed record IngestReadingResponse(Classification Classification);
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
        app.MapPost("/sensors/{sensorId}/readings", HandleAsync)
           .RequireAuthorization(WritePolicy)                     // no anonymous writes
           .WithName("IngestReading")
           .WithTags("Readings")
           .WithSummary("Classify a sensor reading against its calibrated limits and store it.")
           .WithDescription(
               "Limits are inclusive (AC-1). Non-finite values are rejected (AC-2); send them as the strings " +
               "\"NaN\", \"Infinity\", or \"-Infinity\". Duplicates by sensor and timestamp are idempotent (AC-3). " +
               "Readings more than 5 minutes old or 2 seconds ahead of server time are rejected (AC-5).")
           .Produces<IngestReadingResponse>(StatusCodes.Status200OK)
           .ProducesProblem(StatusCodes.Status400BadRequest)
           .Produces(StatusCodes.Status404NotFound)
           .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
           .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        return app;
    }

    // Imperative shell: gather inputs, call the pure core, translate the outcome. No rules here.
    private static async Task<IResult> HandleAsync(
        [FromRoute] string sensorId,
        [FromBody] IngestReadingRequest request,
        [FromServices] ISensorRegistry registry,
        [FromServices] IReadingStore store,
        [FromServices] TimeProvider clock,
        [FromServices] FreshnessPolicy policy,
        HttpResponse response,
        CancellationToken ct)
    {
        var sensor = await registry.FindAsync(sensorId, ct);
        if (sensor is null)
            return TypedResults.NotFound();

        var outcome = RangeClassifier.Classify(
            request.Value, sensor.Limits, request.ObservedAt, clock.GetUtcNow(), policy);

        if (!outcome.TryGetValue(out var classification, out var error))
            return ToProblem(error);

        try
        {
            await store.AppendAsync(
                new ClassifiedReading(sensorId, request.Value, request.ObservedAt, classification), ct);
            return TypedResults.Ok(new IngestReadingResponse(classification));
        }
        catch (StoreUnavailableException)
        {
            response.Headers.RetryAfter = "5";                                        // AC-4
            return TypedResults.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable, title: "EventStoreUnavailable");
        }
    }

    private static ProblemHttpResult ToProblem(ReadingError error) => error switch
    {
        ReadingError.NonFiniteValue or ReadingError.FutureTimestamp or ReadingError.StaleReading =>
            TypedResults.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: error.Code),
        _ => throw new UnreachableException($"Classifier returned unmapped error '{error.Code}'."),
    };
}
