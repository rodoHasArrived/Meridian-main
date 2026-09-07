using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Ui.Services.Services;
using Meridian.Ui.Services;
using System.Net;
using System.Text;

namespace Meridian.Ui.Tests.Services;

public sealed class SettingsConfigurationServiceTests
{
    [Fact]
    public async Task OwnedConnections_KeepDistinctAccountsAndExcludeIncompleteOrAmbiguousOwnership()
    {
        const string body = """
            [
              {"connectionId":"paper-a","providerFamilyId":"alpaca","tenantId":"tenant-a","externalAccountId":"account-a","credentialEnvironment":"paper"},
              {"connectionId":"live-b","providerFamilyId":"alpaca","tenantId":"tenant-a","externalAccountId":"account-b","credentialEnvironment":"live"},
              {"connectionId":"legacy","providerFamilyId":"alpaca","externalAccountId":"account-c","credentialEnvironment":"paper"},
              {"connectionId":"missing-account","providerFamilyId":"alpaca","tenantId":"tenant-a","credentialEnvironment":"paper"},
              {"connectionId":"duplicate","providerFamilyId":"alpaca","tenantId":"tenant-a","externalAccountId":"account-d","credentialEnvironment":"paper"},
              {"connectionId":"DUPLICATE","providerFamilyId":"alpaca","tenantId":"tenant-a","externalAccountId":"account-e","credentialEnvironment":"live"}
            ]
            """;
        using var handler = new StatusHandler(HttpStatusCode.OK, body);
        using var api = new ApiClientService(new StatusClientFactory(handler));
        var rows = await new SettingsConfigurationService(api).GetOwnedCredentialConnectionsAsync();
        rows.Select(row => row.ConnectionId).Should().Equal("paper-a", "live-b");
        rows.Select(row => row.ExternalAccountId).Should().Equal("account-a", "account-b");
        rows.Select(row => row.CredentialEnvironment).Should().Equal("paper", "live");
        handler.Path.Should().Be(UiApiRoutes.ProviderRoutingConnections);
    }

    [Theory]
    [InlineData(403, "[]")]
    [InlineData(200, "null")]
    public async Task OwnedConnections_FailedDiscoveryDoesNotReturnEditableRows(int status, string body)
    {
        using var handler = new StatusHandler((HttpStatusCode)status, body);
        using var api = new ApiClientService(new StatusClientFactory(handler));
        Func<Task> action = async () => await new SettingsConfigurationService(api).GetOwnedCredentialConnectionsAsync();
        await action.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task CredentialStatus_UsesExplicitConnectionSelection()
    {
        using var handler = new StatusHandler(HttpStatusCode.OK, "[{\"providerId\":\"alpaca\",\"credentialState\":1}]");
        using var api = new ApiClientService(new StatusClientFactory(handler));
        var rows = await new SettingsConfigurationService(api).GetProviderCredentialStatusesAsync(connectionId: "paper / A");
        rows.Single(row => row.ProviderId == "alpaca").State.Should().Be(CredentialState.Missing);
        handler.Path.Should().Be(UiApiRoutes.ProviderConnections);
        handler.Query.Should().Be("?connectionId=paper%20%2F%20A");
    }

    [Theory]
    [InlineData(200, "alpaca", true, 2, true, true)]
    [InlineData(403, "alpaca", true, 2, true, false)]
    [InlineData(200, "polygon", true, 2, true, false)]
    [InlineData(200, "alpaca", false, 2, true, false)]
    [InlineData(200, "alpaca", true, 1, true, false)]
    [InlineData(200, "alpaca", true, 2, false, false)]
    public async Task CredentialVerification_RequiresMatchingServerEvidence(int status, string provider, bool success, int state, bool dated, bool expected)
    {
        var timestamp = dated ? "\"2026-09-06T12:00:00Z\"" : "null";
        var body = $"{{\"providerId\":\"{provider}\",\"success\":{success.ToString().ToLowerInvariant()},\"verificationState\":{state},\"lastVerifiedAt\":{timestamp}}}";
        using var handler = new StatusHandler((HttpStatusCode)status, body);
        using var api = new ApiClientService(new StatusClientFactory(handler));
        var verified = await new SettingsConfigurationService(api).VerifyProviderCredentialsAsync("alpaca", "connection A");
        verified.Should().Be(expected);
        handler.Method.Should().Be("POST");
        handler.Path.Should().Be("/api/providers/alpaca/verify");
        handler.Query.Should().Be("?connectionId=connection%20A");
    }

    [Theory]
    [InlineData(false, 3, "PUT")]
    [InlineData(true, 1, "DELETE")]
    public async Task CredentialMutation_UsesAuthenticatedCanonicalRoute(bool remove, int state, string method)
    {
        using var handler = new StatusHandler(HttpStatusCode.OK, $"{{\"providerId\":\"alpaca\",\"credentialState\":{state}}}");
        using var api = new ApiClientService(new StatusClientFactory(handler));
        var service = new SettingsConfigurationService(api);
        if (remove)
            await service.RemoveProviderCredentialsAsync("alpaca", "account / A");
        else
            await service.SaveProviderCredentialsAsync("alpaca", new Dictionary<string, string?> { ["KeyId"] = "test-key", ["SecretKey"] = "test-secret" }, "account / A");
        handler.Path.Should().Be("/api/providers/alpaca/credentials");
        handler.Method.Should().Be(method);
        handler.Query.Should().Be("?connectionId=account%20%2F%20A");
        if (!remove)
        {
            handler.RequestBody.Should().Contain("KeyId").And.Contain("SecretKey");
            handler.RequestBody.Should().NotContain("ALPACA_KEY_ID");
        }
    }

    [Theory]
    [InlineData(false, 403, "{}")]
    [InlineData(true, 403, "{}")]
    [InlineData(false, 200, "null")]
    [InlineData(true, 200, "null")]
    [InlineData(false, 200, "{\"providerId\":\"polygon\",\"credentialState\":3}")]
    [InlineData(true, 200, "{\"providerId\":\"polygon\",\"credentialState\":1}")]
    [InlineData(false, 200, "{\"providerId\":\"alpaca\",\"credentialState\":2}")]
    [InlineData(true, 200, "{\"providerId\":\"alpaca\",\"credentialState\":3}")]
    public async Task CredentialMutation_RequiresAcknowledgedMatchingResult(bool remove, int status, string body)
    {
        using var handler = new StatusHandler((HttpStatusCode)status, body);
        using var api = new ApiClientService(new StatusClientFactory(handler));
        var service = new SettingsConfigurationService(api);
        Func<Task> action = () => remove ? service.RemoveProviderCredentialsAsync("alpaca") :
            service.SaveProviderCredentialsAsync("alpaca", new Dictionary<string, string?> { ["SecretKey"] = "private-test-value" });
        var error = await action.Should().ThrowAsync<InvalidOperationException>();
        error.Which.Message.Should().NotContain("private-test-value").And.Contain("not confirmed");
    }

    [Theory]
    [InlineData(3, CredentialState.Configured)]
    [InlineData(4, CredentialState.Configured)]
    [InlineData(2, CredentialState.Partial)]
    [InlineData(1, CredentialState.Missing)]
    [InlineData(5, CredentialState.Missing)]
    public async Task ServerCredentialStatus_UsesReturnedState(int serverState, CredentialState expected)
    {
        using var handler = new StatusHandler(HttpStatusCode.OK, $"[{{\"providerId\":\"alpaca\",\"credentialState\":{serverState}}}]");
        using var api = new ApiClientService(new StatusClientFactory(handler));
        var service = new SettingsConfigurationService(api);
        var statuses = await service.GetProviderCredentialStatusesAsync();
        statuses.Single(s => s.ProviderId == "alpaca").State.Should().Be(expected);
        handler.Path.Should().Be("/api/providers/connections");
    }

    [Theory]
    [InlineData(403, "[]")]
    [InlineData(200, "[]")]
    [InlineData(200, "[{\"providerId\":\"alpaca\",\"credentialState\":3},{\"providerId\":\"alpaca\",\"credentialState\":3}]")]
    public async Task ServerCredentialStatus_UnavailableOrAmbiguousNeverFallsBackToEnvironment(int status, string body)
    {
        var oldKey = Environment.GetEnvironmentVariable("ALPACA_KEY_ID");
        var oldSecret = Environment.GetEnvironmentVariable("ALPACA_SECRET_KEY");
        try
        {
            Environment.SetEnvironmentVariable("ALPACA_KEY_ID", "other-account-key");
            Environment.SetEnvironmentVariable("ALPACA_SECRET_KEY", "other-account-secret");
            using var handler = new StatusHandler((HttpStatusCode)status, body);
            using var api = new ApiClientService(new StatusClientFactory(handler));
            var statuses = await new SettingsConfigurationService(api).GetProviderCredentialStatusesAsync();
            var alpaca = statuses.Single(s => s.ProviderId == "alpaca");
            alpaca.State.Should().Be(CredentialState.Unavailable);
            alpaca.StatusMessage.Should().Contain("unavailable");
        }
        finally
        {
            Environment.SetEnvironmentVariable("ALPACA_KEY_ID", oldKey);
            Environment.SetEnvironmentVariable("ALPACA_SECRET_KEY", oldSecret);
        }
    }

    private sealed class StatusClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class StatusHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public string? Path { get; private set; }
        public string? Query { get; private set; }
        public string? Method { get; private set; }
        public string? RequestBody { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Path = request.RequestUri!.AbsolutePath;
            Query = request.RequestUri.Query;
            Method = request.Method.Method;
            RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    [Fact]
    public void GetProfiles_UsesCanonicalStrategyLabelForRetainedResearchProfile()
    {
        var profile = SettingsConfigurationService.Instance.GetProfiles()
            .Should()
            .ContainSingle(candidate => candidate.Id == "research")
            .Subject;

        profile.Name.Should().Be("Strategy");
        profile.Name.Should().NotContain("Research");
        profile.Description.Should().Contain("strategy analysis");
    }

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
            provider.SupportsOptions.Should().BeFalse();
            provider.SupportsBrokerage.Should().BeFalse();
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
    [InlineData("robinhood", ProviderTier.FreeWithAccount)]
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

    [Fact]
    public void GetProviderCatalog_MapsOptionsAndBrokerageCapabilities()
    {
        var entry = new Meridian.Contracts.Api.ProviderCatalogEntry
        {
            ProviderId = "robinhood-demo",
            DisplayName = "Robinhood Demo",
            Description = "Options and brokerage",
            ProviderType = ProviderTypeKind.Streaming,
            RequiresCredentials = true,
            CredentialFields = new[]
            {
                new CredentialFieldInfo("AccessToken", "ROBINHOOD_ACCESS_TOKEN", "Access Token", true)
            },
            Capabilities = new CapabilityInfo
            {
                SupportsOptionsChain = true,
                SupportsBrokerage = true
            }
        };

        try
        {
            ProviderCatalog.InitializeFromRegistry(
                () => new[] { entry },
                id => id == entry.ProviderId ? entry : null);

            var provider = SettingsConfigurationService.Instance.GetProviderCatalog().Single();
            provider.SupportsOptions.Should().BeTrue();
            provider.SupportsBrokerage.Should().BeTrue();
        }
        finally
        {
            ProviderCatalog.RuntimeCatalogProvider = null;
            ProviderCatalog.RuntimeCatalogEntryProvider = null;
        }
    }

    [Fact]
    public void GetProviderCatalog_StaticFallbackIncludesRobinhoodCapabilities()
    {
        ProviderCatalog.RuntimeCatalogProvider = null;
        ProviderCatalog.RuntimeCatalogEntryProvider = null;

        var provider = SettingsConfigurationService.Instance.GetProviderCatalog()
            .Single(item => item.Id == "robinhood");

        provider.SupportsOptions.Should().BeTrue();
        provider.SupportsBrokerage.Should().BeTrue();
        provider.RequiredEnvVars.Should().Contain("ROBINHOOD_ACCESS_TOKEN");
    }

    [Fact]
    public void GetShellDensityMode_DefaultsToStandardWhenPreferencesFileIsMissing()
    {
        var preferencesPath = Path.Combine(
            Path.GetTempPath(),
            "meridian-settings-tests",
            $"{Guid.NewGuid():N}.desktop-shell-preferences.json");

        try
        {
            SettingsConfigurationService.SetDesktopPreferencesFilePathOverrideForTests(preferencesPath);

            SettingsConfigurationService.Instance.GetShellDensityMode().Should().Be(ShellDensityMode.Standard);
        }
        finally
        {
            SettingsConfigurationService.SetDesktopPreferencesFilePathOverrideForTests(null);
            if (File.Exists(preferencesPath))
            {
                File.Delete(preferencesPath);
            }
        }
    }

    [Fact]
    public void SetShellDensityMode_PersistsAndRoundTripsDesktopPreferences()
    {
        var preferencesPath = Path.Combine(
            Path.GetTempPath(),
            "meridian-settings-tests",
            $"{Guid.NewGuid():N}.desktop-shell-preferences.json");

        try
        {
            SettingsConfigurationService.SetDesktopPreferencesFilePathOverrideForTests(preferencesPath);
            var service = SettingsConfigurationService.Instance;

            service.SetShellDensityMode(ShellDensityMode.Compact);

            File.Exists(preferencesPath).Should().BeTrue();
            File.ReadAllText(preferencesPath).Should().Contain("shellDensityMode");

            SettingsConfigurationService.SetDesktopPreferencesFilePathOverrideForTests(preferencesPath);
            SettingsConfigurationService.Instance.GetShellDensityMode().Should().Be(ShellDensityMode.Compact);
        }
        finally
        {
            SettingsConfigurationService.SetDesktopPreferencesFilePathOverrideForTests(null);
            if (File.Exists(preferencesPath))
            {
                File.Delete(preferencesPath);
            }
        }
    }

    [Theory]
    [InlineData(true, ShellDensityMode.Compact)]
    [InlineData(false, ShellDensityMode.Standard)]
    public void GetShellDensityMode_MigratesLegacyCompactModeFlag(bool legacyValue, ShellDensityMode expectedDensity)
    {
        var preferencesPath = Path.Combine(
            Path.GetTempPath(),
            "meridian-settings-tests",
            $"{Guid.NewGuid():N}.desktop-shell-preferences.json");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(preferencesPath)!);
            File.WriteAllText(preferencesPath, $$"""{"isCompactMode":{{legacyValue.ToString().ToLowerInvariant()}}}""");
            SettingsConfigurationService.SetDesktopPreferencesFilePathOverrideForTests(preferencesPath);

            SettingsConfigurationService.Instance.GetShellDensityMode().Should().Be(expectedDensity);
        }
        finally
        {
            SettingsConfigurationService.SetDesktopPreferencesFilePathOverrideForTests(null);
            if (File.Exists(preferencesPath))
            {
                File.Delete(preferencesPath);
            }
        }
    }
}
