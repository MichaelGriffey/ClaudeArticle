using System.Text.Json.Serialization;
using Ingestion.Api.Features.Ingest;
using Ingestion.Api.Infrastructure;
using Ingestion.Api.OpenApi;
using Ingestion.Api.Security;
using Ingestion.Domain;
using Microsoft.OpenApi;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// Keep the web defaults (AllowReadingFromString already admits "NaN" and "Infinity");
// only add string enums. Assigning NumberHandling here would silently replace them.
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));  // "Nominal", not 1

builder.Services.AddProblemDetails();
builder.Services.AddSingleton(TimeProvider.System);

// Requirement values come from the story (AC-5); operational values from configuration (ADR 0007).
builder.Services.AddSingleton(
    FreshnessPolicy.Create(FreshnessRequirements.MaxAge, FreshnessRequirements.MaxSkew)
        .OrThrowAtStartup("freshness policy"));
builder.Services.AddOptions<IngestionOptions>()
    .BindConfiguration(IngestionOptions.SectionName, o => o.ErrorOnUnknownConfiguration = true)
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Writes need the readings:write scope (people) or the Readings.Write app role (services), from
// bearer tokens such as Microsoft Entra ID's. Fails closed: see ADR 0003.
builder.Services.AddReadingsAuthorization(builder.Environment);

// Adapters. The registry is read and checked from configuration at startup. The store is chosen by
// Storage:Provider; no durable one exists yet, so production refuses to start (ADR 0004).
var sensors = InMemorySensorRegistry.FromConfiguration(builder.Configuration.GetSection("Sensors"));
builder.Services.AddSingleton<ISensorRegistry>(sensors);
builder.Services.AddReadingStorage(builder.Configuration);   // also registers the store's readiness check

// Observability (ADR 0005): logs and metrics from the slice, ASP.NET Core metrics and traces.
// Exported over OTLP only where an endpoint is configured, so development and tests export nothing.
builder.Services.AddSingleton<IngestTelemetry>();
var openTelemetry = builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("ingestion-api"))
    .WithMetrics(m => m.AddAspNetCoreInstrumentation().AddMeter(IngestTelemetry.MeterName))
    .WithTracing(t => t.AddAspNetCoreInstrumentation())
    .WithLogging();
if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
    openTelemetry.UseOtlpExporter();

// OpenAPI document generated from endpoint metadata; bearer security documented per secured operation.
builder.Services.AddOpenApi(o =>
{
    o.AddDocumentTransformer((document, _, _) =>
    {
        document.Info = new OpenApiInfo
        {
            Title = "Ingestion API",
            Version = "v1",
            Description = "Classifies sensor readings against calibrated limits (story AB#1234).",
        };
        return Task.CompletedTask;
    });
    o.AddOperationTransformer<BearerSecurityOperationTransformer>();

    // observedAt is text on the wire (AC-7) but documented as a date-time, so Swagger UI proposes one.
    o.AddSchemaTransformer((schema, context, _) =>
    {
        if (context.JsonPropertyInfo is { Name: "observedAt" } property
            && property.DeclaringType == typeof(IngestReadingRequest))
        {
            schema.Format = "date-time";
        }
        return Task.CompletedTask;
    });
});

var app = builder.Build();

// Every error is problem details (ADR 0002). These wrap authentication so 401 and 403 get a body too.
app.UseExceptionHandler();   // unexpected exceptions: 500 without exception text, logged on the server
app.UseStatusCodePages();    // empty 4xx bodies (401, 403, 415, unknown routes) become problem details

if (app.Environment.IsDevelopment())
{
    // Developer surface only: production exposes neither the document nor the UI.
    app.MapOpenApi();                                                  // /openapi/v1.json
    app.UseSwaggerUI(o =>
    {
        o.SwaggerEndpoint("/openapi/v1.json", "Ingestion API v1");  // served at /swagger
        o.DocumentTitle = "Ingestion API";
        o.EnablePersistAuthorization();
        o.DisplayRequestDuration();
    });
    app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();
}

app.UseAuthentication();
app.UseAuthorization();

// Platform probes (ADR 0005): anonymous by that ADR, and they answer with a status word only.
app.MapHealthChecks("/health/live", new() { Predicate = _ => false });             // the process is up
app.MapHealthChecks("/health/ready", new() { Predicate = c => c.Tags.Contains(ReadingStorage.ReadyTag) });

app.MapIngestReadings();
await app.RunAsync();
