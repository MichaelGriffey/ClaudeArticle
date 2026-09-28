using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace Ingestion.Api.Security;

/// <summary>Who may write readings, and how bearer tokens are validated (ADR 0003).</summary>
public static class ReadingsAuthorization
{
    public const string WritePolicy = "readings:write";

    /// <summary>Delegated scope for people and tools acting for them.</summary>
    public const string WriteScope = "readings:write";

    /// <summary>App role for services such as device gateways (client credentials).</summary>
    public const string WriteAppRole = "Readings.Write";

    public static IServiceCollection AddReadingsAuthorization(this IServiceCollection services, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        services.AddAuthorizationBuilder()
            .AddPolicy(WritePolicy, p => p
                .RequireAuthenticatedUser()
                .AddRequirements(new ScopeOrAppRoleRequirement(WriteScope, WriteAppRole)));

        // Authority, audiences, and (for local development) signing keys bind from
        // Authentication:Schemes:Bearer. Claim names stay as issued (scp, roles) for the requirement.
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o => o.MapInboundClaims = false);

        // Outside Development, a configuration that can never validate a token stops startup instead
        // of starting and answering every caller with 401.
        if (!environment.IsDevelopment())
        {
            services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
                .Validate(CanValidateTokens,
                    "Authentication:Schemes:Bearer needs an Authority (or signing keys) and at least one audience.")
                .ValidateOnStart();
        }

        return services;
    }

    private static bool CanValidateTokens(JwtBearerOptions options)
    {
        var parameters = options.TokenValidationParameters;
        var hasIssuer = !string.IsNullOrWhiteSpace(options.Authority)
            || parameters.IssuerSigningKey is not null
            || parameters.IssuerSigningKeys?.Any() == true;
        // Blank entries do not count: an empty environment variable still creates the key.
        var hasAudience = !string.IsNullOrWhiteSpace(options.Audience)
            || !string.IsNullOrWhiteSpace(parameters.ValidAudience)
            || parameters.ValidAudiences?.Any(a => !string.IsNullOrWhiteSpace(a)) == true;
        return hasIssuer && hasAudience;
    }
}
