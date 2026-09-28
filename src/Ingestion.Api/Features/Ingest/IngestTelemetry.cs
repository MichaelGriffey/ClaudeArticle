using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Ingestion.Api.Features.Ingest;

/// <summary>
/// Logs and metrics for the ingest slice (ADR 0005). Everything is derived from the response the
/// handler returns, so the handler stays free of logging. Logs carry the sensor ID and an error
/// code, never a reading's value or a token; the trace ID comes from the logging scope.
/// </summary>
public sealed partial class IngestTelemetry
{
    public const string MeterName = "Ingestion.Api";

    private readonly ILogger<IngestTelemetry> _logger;
    private readonly Counter<long> _accepted;
    private readonly Counter<long> _rejected;
    private readonly Histogram<double> _appendDuration;

    public IngestTelemetry(ILogger<IngestTelemetry> logger, IMeterFactory meters)
    {
        ArgumentNullException.ThrowIfNull(meters);
        _logger = logger;

        var meter = meters.Create(MeterName);
        _accepted = meter.CreateCounter<long>(
            "ingestion.readings.accepted", "{reading}", "Readings answered with 200, by classification.");
        _rejected = meter.CreateCounter<long>(
            "ingestion.readings.rejected", "{reading}", "Readings answered with an error, by code.");
        _appendDuration = meter.CreateHistogram<double>(
            "ingestion.store.append.duration", "s", "Time the store took to answer one append, by outcome.");
    }

    /// <summary>Counts and, for errors, logs the response. Levels: 5xx Error, 404 and 409 Warning, other rejections Debug.</summary>
    public void Record(string sensorId, IResult result)
    {
        switch (result)
        {
            case Ok<IngestReadingResponse> { Value: { } accepted }:
                _accepted.Add(1, new KeyValuePair<string, object?>("classification", accepted.Classification.ToString()));
                break;

            case IValueHttpResult { Value: ProblemDetails problem }:
                var code = problem.Extensions.TryGetValue("code", out var value) && value is string text ? text : "Unknown";
                _rejected.Add(1, new KeyValuePair<string, object?>("code", code));
                var level = LevelFor(problem.Status);
                LogRejected(level, sensorId, code, problem.Status);
                break;
        }
    }

    public void StoreAnswered(AppendOutcome outcome, TimeSpan elapsed)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        _appendDuration.Record(
            elapsed.TotalSeconds, new KeyValuePair<string, object?>("outcome", outcome.GetType().Name));
    }

    private static LogLevel LevelFor(int? status) => status switch
    {
        >= 500 => LogLevel.Error,
        StatusCodes.Status404NotFound or StatusCodes.Status409Conflict => LogLevel.Warning,
        _ => LogLevel.Debug,
    };

    [LoggerMessage(EventId = 1, Message = "Reading for sensor {SensorId} answered {Status} {Code}")]
    private partial void LogRejected(LogLevel level, string sensorId, string code, int? status);
}
