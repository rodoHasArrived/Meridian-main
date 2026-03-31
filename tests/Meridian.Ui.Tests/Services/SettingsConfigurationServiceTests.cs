using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Ui.Services.Services;

namespace Meridian.Ui.Tests.Services;

public sealed class SettingsConfigurationServiceTests
{
    [Fact]
    public void GetProviderCatalog_MapsSharedCatalogCredentialFieldsIntoUiModel()
    {
        var entry = new Meridian.Contracts.Api.ProviderCatalogEntry
        {
            ProviderId = "demo-provider",
            DisplayName = "Demo Provider",
            Description = "Shared catalog entry",
            ProviderType = ProviderTypeKind.Streaming,
            RequiresCredentials = true,
            CredentialFields = new[]
            {
                new CredentialFieldInfo("ApiKey", "DEMO_API_KEY", "Demo API Key", true),
                new CredentialFieldInfo("Host", null, "Host", true)
            },
            RateLimit = new RateLimitInfo
            {
                MaxRequestsPerWindow = 120,
                WindowSeconds = 60,
                MinDelayMs = 500,
                Description = "120 requests/minute"
            },
            Capabilities = new CapabilityInfo
            {
                SupportsStreaming = true
            }
        };

        try
        {
            ProviderCatalog.InitializeFromRegistry(
                () => new[] { entry },
                id => id == entry.ProviderId ? entry : null);

            var catalog = SettingsConfigurationService.Instance.GetProviderCatalog();
            var provider = catalog.Should().ContainSingle().Subject;

            provider.Id.Should().Be("demo-provider");
            provider.CredentialFields.Should().ContainSingle();
            provider.RequiredEnvVars.Should().Equal("DEMO_API_KEY");
            provider.SupportsStreaming.Should().BeTrue();
            provider.SupportsHistorical.Should().BeFalse();
            provider.RateLimitPerMinute.Should().Be(120);
        }
        finally
        {
            ProviderCatalog.RuntimeCatalogProvider = null;
            ProviderCatalog.RuntimeCatalogEntryProvider = null;
        }
    }

    [Fact]
    public void GetProviderCredentialStatuses_UsesRequiredEnvironmentBackedCredentialFields()
    {
        const string envVar = "DEMO_STATUS_API_KEY";
        var entry = new Meridian.Contracts.Api.ProviderCatalogEntry
        {
            ProviderId = "demo-status-provider",
            DisplayName = "Demo Status Provider",
            Description = "Shared catalog entry",
            ProviderType = ProviderTypeKind.Backfill,
            RequiresCredentials = true,
            CredentialFields = new[]
            {
                new CredentialFieldInfo("ApiKey", envVar, "Demo API Key", true),
                new CredentialFieldInfo("Secret", "DEMO_STATUS_SECRET", "Demo Secret", false)
            },
            Capabilities = new CapabilityInfo()
        };

        try
        {
            ProviderCatalog.InitializeFromRegistry(
                () => new[] { entry },
                id => id == entry.ProviderId ? entry : null);

            Environment.SetEnvironmentVariable(envVar, null);
            var missingStatus = SettingsConfigurationService.Instance
                .GetProviderCredentialStatuses()
                .Single(status => status.ProviderId == entry.ProviderId);

            missingStatus.State.Should().Be(CredentialState.Missing);
            missingStatus.MissingEnvVars.Should().Equal(envVar);

            Environment.SetEnvironmentVariable(envVar, "configured");
            var configuredStatus = SettingsConfigurationService.Instance
                .GetProviderCredentialStatuses()
                .Single(status => status.ProviderId == entry.ProviderId);

            configuredStatus.State.Should().Be(CredentialState.Configured);
            configuredStatus.MissingEnvVars.Should().BeEmpty();
        }
        finally
        {
            Environment.SetEnvironmentVariable(envVar, null);
            ProviderCatalog.RuntimeCatalogProvider = null;
            ProviderCatalog.RuntimeCatalogEntryProvider = null;
        }
    }
}
