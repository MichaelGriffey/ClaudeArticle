using System.Diagnostics;
using System.Globalization;
using Ingestion.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Ingestion.Api.Features.Ingest;

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
           .ProducesProblem(StatusCodes.Status409Conflict)
           .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
           .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        return app;
    }

    /// <summary>The handler's collaborators, resolved from the container.</summary>
    internal readonly record struct Services(
        [FromServices] ISensorRegistry Registry,
        [FromServices] IReadingStore Store,
        [FromServices] TimeProvider Clock,
        [FromServices] FreshnessPolicy Policy,
        [FromServices] IOptions<IngestionOptions> Options);

    // Imperative shell: validate, load, call the pure core, persist, translate. No rules here.
    private static async Task<Results<Ok<IngestReadingResponse>, ValidationProblem, ProblemHttpResult>> HandleAsync(
        [FromRoute] string sensorId,
        [FromBody] IngestReadingRequest request,
        [AsParameters] Services services,
        HttpResponse response,
        CancellationToken ct)
    {
        if (!request.Validate().TryGetValue(out var reading, out var invalid))
            return IngestProblems.InvalidRequest(invalid);                                    // AC-6, AC-7

        var options = services.Options.Value;

        // One budget for every dependency call, measured on the injected clock (AC-9).
        using var budget = new CancellationTokenSource(options.DependencyBudget, services.Clock);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct, budget.Token);
        try
        {
            var sensor = await services.Registry.FindAsync(sensorId, deadline.Token);
            if (sensor is null)
                return IngestProblems.SensorNotFound();                                       // AC-12

            var outcome = RangeClassifier.Classify(reading, sensor.Limits, services.Policy, services.Clock.GetUtcNow());
            if (!outcome.TryGetValue(out var classification, out var rejection))
                return IngestProblems.Rejected(rejection);                                    // AC-2, AC-5, AC-11

            // The registry's ID, not the route's: a case-insensitive registry must not split the idempotency key.
            var appended = await services.Store.AppendAsync(
                new ClassifiedReading(sensor.Id, reading.Value, reading.ObservedAt, classification), deadline.Token);

            return appended switch
            {
                AppendOutcome.Inserted => TypedResults.Ok(new IngestReadingResponse(classification)),
                AppendOutcome.Duplicate duplicate =>
                    TypedResults.Ok(new IngestReadingResponse(duplicate.StoredClassification)),   // AC-3
                AppendOutcome.Conflict => IngestProblems.ConflictingReading(),                  // AC-8
                AppendOutcome.Unavailable => IngestProblems.StoreUnavailable(response, options.RetryAfter),  // AC-4
                _ => throw new UnreachableException($"Unmapped append outcome '{appended}'."),
            };
        }
        catch (OperationCanceledException) when (budget.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            return IngestProblems.DependencyTimeout(response, options.RetryAfter);            // AC-9
        }
    }

    private static string Describe(FreshnessPolicy policy) =>
        "Limits are inclusive (AC-1). Non-finite values are rejected (AC-2); send them as the strings " +
        "\"NaN\", \"Infinity\", or \"-Infinity\". Repeating a reading (same sensor, timestamp, and value) is " +
        "idempotent (AC-3); the same sensor and timestamp with a different value is a conflict (AC-8). " +
        $"Readings more than {Humanize(policy.MaxAge)} old or {Humanize(policy.MaxSkew)} ahead of server " +
        "time are rejected (AC-5). observedAt must carry 'Z' or a UTC offset (AC-7).";

    private static string Humanize(TimeSpan span) =>
        span.Ticks % TimeSpan.TicksPerMinute == 0 ? Count(span.TotalMinutes, "minute") : Count(span.TotalSeconds, "second");

    private static string Count(double amount, string unit) =>
        string.Create(CultureInfo.InvariantCulture, $"{amount:0.###} {unit}{(amount == 1 ? "" : "s")}");
}
