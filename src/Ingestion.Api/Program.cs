using System.Text.Json.Serialization;
using Ingestion.Api.Features.Ingest;
using Ingestion.Api.Infrastructure;
using Ingestion.Api.OpenApi;
using Ingestion.Domain;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

// Keep the web defaults (AllowReadingFromString already admits "NaN" and "Infinity");
// only add string enums. Assigning NumberHandling here would silently replace them.
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));  // "Nominal", not 1

builder.Services.AddProblemDetails();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(
    FreshnessPolicy.Create(FreshnessRequirements.MaxAge, FreshnessRequirements.MaxSkew)
        .Match(p => p, e => throw new InvalidOperationException($"Invalid freshness policy: {e}")));

// Authentication (for example, Microsoft Entra ID bearer tokens) is registered per environment.
builder.Services.AddAuthorizationBuilder()
    // Authentication (for example, Microsoft Entra ID bearer tokens) is registered per environment.
    .AddPolicy(IngestEndpoint.WritePolicy, p => p
        .RequireAuthenticatedUser()
        .RequireClaim("scope", IngestEndpoint.WritePolicy
    ));

// Authority and audiences bind from configuration (Authentication:Schemes:Bearer).
// With no configuration every token is rejected: the service fails closed.
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();

// Development adapters. Production replaces these with a database-backed registry and an
// outbox-backed store; the endpoint and the core do not change.
var sensors = InMemorySensorRegistry.FromConfiguration(builder.Configuration.GetSection("Sensors"));
builder.Services.AddSingleton<ISensorRegistry>(sensors);
builder.Services.AddSingleton<IReadingStore, InMemoryReadingStore>();

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
});

var app = builder.Build();

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

app.MapIngestReadings();
await app.RunAsync();
