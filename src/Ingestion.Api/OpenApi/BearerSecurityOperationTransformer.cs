using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Ingestion.Api.OpenApi;

/// <summary>
/// Describes bearer authentication in the OpenAPI document, but only on operations that
/// actually require authorization. Adds the scheme, the requirement, and the 401/403 responses,
/// so Swagger UI shows the Authorize button and every slice documents its security the same way.
/// </summary>
public sealed class BearerSecurityOperationTransformer : IOpenApiOperationTransformer
{
    public const string SchemeId = "Bearer";

    public Task TransformAsync(
        OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);

        var metadata = context.Description.ActionDescriptor.EndpointMetadata;
        var requiresAuthorization = metadata.OfType<IAuthorizeData>().Any() && !metadata.OfType<IAllowAnonymous>().Any();
        if (!requiresAuthorization || context.Document is not { } document)
            return Task.CompletedTask;

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes.TryAdd(SchemeId, new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Bearer token with the required scope. For local development: dotnet user-jwts create --scope \"readings:write\"",
        });

        // The reference must carry the document, or the requirement is dropped when serialized.
        operation.Security ??= [];
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(SchemeId, document)] = [],
        });

        operation.Responses ??= [];
        operation.Responses.TryAdd("401", new OpenApiResponse { Description = "Missing or invalid bearer token." });
        operation.Responses.TryAdd("403", new OpenApiResponse { Description = "Token does not carry the required scope." });

        return Task.CompletedTask;
    }
}
