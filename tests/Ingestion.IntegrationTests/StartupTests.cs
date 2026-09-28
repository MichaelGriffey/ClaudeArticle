using Ingestion.Api.Infrastructure;
using Ingestion.Testing;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ingestion.IntegrationTests;

/// <summary>Configuration the service cannot run with stops startup instead of failing requests later.</summary>
public sealed class StartupTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void The_service_refuses_to_start_without_a_storage_provider(string? provider)    // ADR 0004
    {
        using var api = new IngestionApi();
        api.Settings[ReadingStorage.ProviderKey] = provider;

        var error = Assert.Throws<InvalidOperationException>(() => api.CreateClient());
        Assert.Contains(ReadingStorage.ProviderKey, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_service_refuses_to_start_with_an_unknown_storage_provider()
    {
        using var api = new IngestionApi();
        api.Settings[ReadingStorage.ProviderKey] = "Postgres";

        var error = Assert.Throws<InvalidOperationException>(() => api.CreateClient());
        Assert.Contains("'Postgres' is not a known reading store", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Ingestion:DependencyBudget", "00:00:00")]
    [InlineData("Ingestion:DependencyBudget", "00:05:00")]
    [InlineData("Ingestion:RetryAfter", "00:00:00")]
    public void The_service_refuses_to_start_with_out_of_range_ingestion_settings(string key, string value)
    {
        using var api = new IngestionApi();
        api.Settings[key] = value;

        Assert.Throws<OptionsValidationException>(() => api.CreateClient());
    }

    [Theory]
    [InlineData(IngestionApi.AuthorityKey)]
    [InlineData(IngestionApi.AudienceKey)]
    public void Outside_development_the_service_refuses_to_start_without_bearer_settings(string missing)   // ADR 0003
    {
        using var api = new IngestionApi();
        api.Settings[missing] = null;

        var error = Assert.Throws<OptionsValidationException>(() => api.CreateClient());
        Assert.Contains("Authentication:Schemes:Bearer", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void In_development_the_service_starts_without_an_authority()
    {
        using var api = new IngestionApi(IngestionApi.DefaultStart, "Development");
        api.Settings[IngestionApi.AuthorityKey] = null;
        api.Settings[IngestionApi.AudienceKey] = null;

        using var client = api.CreateClient();                   // throws if startup fails
    }

    [Fact]
    public void The_service_refuses_to_start_with_a_misspelled_ingestion_setting()
    {
        using var api = new IngestionApi();
        api.Settings["Ingestion:RetryAftr"] = "00:00:05";

        Assert.Throws<InvalidOperationException>(() => api.CreateClient());
    }
}
