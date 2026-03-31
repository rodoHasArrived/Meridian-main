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

    [Fact]
    public void GetProviderCredentialStatuses_TreatsAliasEnvironmentVariablesAsConfigured()
    {
        const string primaryEnvVar = "DEMO_ALIAS_PRIMARY";
        const string legacyEnvVar = "DEMO_ALIAS_LEGACY";
        var entry = new Meridian.Contracts.Api.ProviderCatalogEntry
        {
            ProviderId = "demo-alias-provider",
            DisplayName = "Demo Alias Provider",
            Description = "Shared catalog entry",
            ProviderType = ProviderTypeKind.Backfill,
            RequiresCredentials = true,
            CredentialFields = new[]
            {
                new CredentialFieldInfo(
                    "ApiKey",
                    primaryEnvVar,
                    "Demo API Key",
                    true,
                    EnvironmentVariableAliases: new[] { legacyEnvVar })
            },
            Capabilities = new CapabilityInfo()
        };

        try
        {
            ProviderCatalog.InitializeFromRegistry(
                () => new[] { entry },
                id => id == entry.ProviderId ? entry : null);

            Environment.SetEnvironmentVariable(primaryEnvVar, null);
            Environment.SetEnvironmentVariable(legacyEnvVar, "legacy-configured");

            var catalog = SettingsConfigurationService.Instance.GetProviderCatalog();
            var provider = catalog.Should().ContainSingle().Subject;
            provider.RequiredEnvVars.Should().Equal(primaryEnvVar, legacyEnvVar);

            var status = SettingsConfigurationService.Instance
                .GetProviderCredentialStatuses()
                .Single(item => item.ProviderId == entry.ProviderId);

            status.State.Should().Be(CredentialState.Configured);
            status.MissingEnvVars.Should().BeEmpty();
        }
        finally
        {
            Environment.SetEnvironmentVariable(primaryEnvVar, null);
            Environment.SetEnvironmentVariable(legacyEnvVar, null);
            ProviderCatalog.RuntimeCatalogProvider = null;
            ProviderCatalog.RuntimeCatalogEntryProvider = null;
        }
    }

    [Theory]
    [InlineData("nasdaq", ProviderTier.LimitedFree)]
    [InlineData("ibkr", ProviderTier.FreeWithAccount)]
    public void GetProviderCatalog_MapsRuntimeProviderIdsToExpectedTier(string providerId, ProviderTier expectedTier)
    {
        var entry = new Meridian.Contracts.Api.ProviderCatalogEntry
        {
            ProviderId = providerId,
            DisplayName = $"Provider {providerId}",
            Description = "Runtime catalog entry",
            ProviderType = ProviderTypeKind.Backfill,
            RequiresCredentials = false,
            CredentialFields = Array.Empty<CredentialFieldInfo>(),
            Capabilities = new CapabilityInfo()
        };

        try
        {
            ProviderCatalog.InitializeFromRegistry(
                () => new[] { entry },
                id => id == entry.ProviderId ? entry : null);

            var provider = SettingsConfigurationService.Instance.GetProviderCatalog().Single();
            provider.Tier.Should().Be(expectedTier);
        }
        finally
        {
            ProviderCatalog.RuntimeCatalogProvider = null;
            ProviderCatalog.RuntimeCatalogEntryProvider = null;
        }
    }
}
