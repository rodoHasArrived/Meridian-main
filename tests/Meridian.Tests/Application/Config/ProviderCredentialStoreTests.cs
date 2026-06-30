using FluentAssertions;
using Meridian.Core.Config;
using Meridian.Application.Config.Credentials;
using Meridian.DataIntegration.Credentials;
using Meridian.Contracts.Configuration;
using Meridian.ProviderSdk;
using Xunit;

namespace Meridian.Tests.Application.Config;

public sealed class ProviderCredentialStoreTests : IDisposable
{
    private readonly string _root;

    public ProviderCredentialStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "meridian-tests", "provider-credentials", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task SaveAndRead_RoundTripsCredentialThroughEncryptedLocalStore()
    {
        var store = new FileProviderCredentialStore(_root);

        await store.SaveAsync(new ProviderCredentialSaveRequest(
            "alpaca",
            new Dictionary<string, string?>
            {
                ["KeyId"] = "paper-key-id",
                ["SecretKey"] = "paper-secret"
            },
            Environment: "paper",
            Actor: "test-operator"));

        var read = await store.ReadForProviderAsync("alpaca");
        var status = await store.GetStatusAsync("alpaca");

        read.Should().NotBeNull();
        read!.Source.Should().Be(ProviderCredentialSourceDto.LocalEncryptedStore);
        read.Get("KeyId").Should().Be("paper-key-id");
        read.Get("SecretKey").Should().Be("paper-secret");
        status.CredentialState.Should().Be(ProviderCredentialStateDto.Configured);
        status.CredentialSource.Should().Be(ProviderCredentialSourceDto.LocalEncryptedStore);
        status.MaskedKeyPreview.Should().NotContain("paper-key-id");
        status.AuditMetadata.Should().ContainKey("lastRotatedAt");
        status.AuditMetadata.Should().ContainKey("rotationDueAt");
        status.AuditMetadata.Should().Contain("verificationRequired", "true");
    }

    [Fact]
    public async Task RecordVerificationAsync_SuccessClearsVerificationRequiredMetadata()
    {
        var store = new FileProviderCredentialStore(_root);
        await store.SaveAsync(new ProviderCredentialSaveRequest(
            "alpaca",
            new Dictionary<string, string?>
            {
                ["KeyId"] = "paper-key-id",
                ["SecretKey"] = "paper-secret"
            },
            Environment: "paper",
            Actor: "test-operator"));

        await store.RecordVerificationAsync(new ProviderCredentialVerificationUpdate(
            ProviderId: "alpaca",
            Success: true,
            ErrorMessage: null,
            ExternalAccountId: "paper-account-1",
            Actor: "test-operator"));

        var status = await store.GetStatusAsync("alpaca");
        status.CredentialState.Should().Be(ProviderCredentialStateDto.Verified);
        status.AuditMetadata.Should().Contain("verificationRequired", "false");
        status.AuditMetadata.Should().Contain("lastVerifiedBy", "test-operator");
    }

    [Theory]
    [InlineData("https://paper-api.alpaca.markets/v2", "paper")]
    [InlineData("https://api.alpaca.markets/v2", "live")]
    public async Task SaveAsync_NormalizesAlpacaTradingApiEndpointToEnvironment(
        string endpoint,
        string expectedEnvironment)
    {
        var store = new FileProviderCredentialStore(_root);

        await store.SaveAsync(new ProviderCredentialSaveRequest(
            "alpaca",
            new Dictionary<string, string?>
            {
                ["KeyId"] = "endpoint-key-id",
                ["SecretKey"] = "endpoint-secret"
            },
            Environment: endpoint,
            Actor: "test-operator"));

        var status = await store.GetStatusAsync("alpaca");

        status.Environment.Should().Be(expectedEnvironment);
        AlpacaCredentialEnvironment.NormalizeTradingEnvironment(endpoint).Should().Be(expectedEnvironment);
    }

    [Theory]
    [InlineData(null, "sandbox")]
    [InlineData("", "sandbox")]
    [InlineData("production", "production")]
    [InlineData("development", "development")]
    [InlineData("invalid", "sandbox")]
    public async Task SaveAsync_NormalizesPlaidEnvironmentAndSetupRoute(
        string? environment,
        string expectedEnvironment)
    {
        var store = new FileProviderCredentialStore(_root);

        await store.SaveAsync(new ProviderCredentialSaveRequest(
            "plaid-api",
            new Dictionary<string, string?>
            {
                ["ClientId"] = "plaid-client-id",
                ["Secret"] = "plaid-secret"
            },
            Environment: environment,
            Actor: "test-operator"));

        var descriptor = ProviderCredentialCatalog.Find("plaid-api");
        var status = await store.GetStatusAsync("plaid");

        descriptor.Should().NotBeNull();
        descriptor!.ProviderId.Should().Be("plaid");
        descriptor.ResolvedActionHref.Should().Be("/settings#plaid-provider-setup");
        status.Environment.Should().Be(expectedEnvironment);
        status.CredentialState.Should().Be(ProviderCredentialStateDto.Configured);
    }

    [Fact]
    public async Task SaveAsync_QuickBooksStoresReadOnlyCompanyOAuthConfig()
    {
        var store = new FileProviderCredentialStore(_root);

        await store.SaveAsync(new ProviderCredentialSaveRequest(
            "qbo",
            new Dictionary<string, string?>
            {
                ["ClientId"] = "qbo-client-id",
                ["ClientSecret"] = "qbo-client-secret",
                ["RefreshToken"] = "qbo-refresh-token",
                ["RealmId"] = "9130359087654321",
                ["CompanyName"] = "Meridian-Dev"
            },
            Environment: "live",
            Actor: "test-operator"));

        var descriptor = ProviderCredentialCatalog.Find("quickbooks-online");
        var status = await store.GetStatusAsync("quickbooks");
        var read = await store.ReadForProviderAsync("quickbooks");
        var vaultText = await File.ReadAllTextAsync(store.VaultPath);

        descriptor.Should().NotBeNull();
        descriptor!.RequiresCredentials.Should().BeTrue();
        descriptor.ResolvedActionHref.Should().Be("/settings#provider-quickbooks-connection");
        status.Environment.Should().Be("production");
        status.CredentialState.Should().Be(ProviderCredentialStateDto.Configured);
        status.MissingFields.Should().BeEmpty();
        read.Should().NotBeNull();
        read!.Get("RealmId").Should().Be("9130359087654321");
        read.Get("CompanyName").Should().Be("Meridian-Dev");
        vaultText.Should().NotContain("qbo-client-secret");
        vaultText.Should().NotContain("qbo-refresh-token");
    }

    [Fact]
    public async Task SaveAsync_TwelveDataAliasStoresCredentialMetadata()
    {
        var store = new FileProviderCredentialStore(_root);

        await store.SaveAsync(new ProviderCredentialSaveRequest(
            "twelve-data",
            new Dictionary<string, string?>
            {
                ["ApiKey"] = "twelve-data-key"
            },
            Environment: null,
            Actor: "test-operator"));

        var descriptor = ProviderCredentialCatalog.Find("twelve_data");
        var status = await store.GetStatusAsync("twelvedata");
        var read = await store.ReadForProviderAsync("twelvedata");
        var vaultText = await File.ReadAllTextAsync(store.VaultPath);

        descriptor.Should().NotBeNull();
        descriptor!.ProviderId.Should().Be("twelvedata");
        descriptor.AffectedWorkflows.Should().Contain("Symbol search");
        descriptor.RequiredFields.Should().ContainSingle(field =>
            field.Name == "ApiKey" && field.EnvironmentNames.Contains("TWELVEDATA_API_KEY"));
        status.CredentialState.Should().Be(ProviderCredentialStateDto.Configured);
        read.Should().NotBeNull();
        read!.Get("ApiKey").Should().Be("twelve-data-key");
        vaultText.Should().NotContain("twelve-data-key");
    }

    [Fact]
    public void DefaultProviderSetupHandlers_IncludesTwelveDataReadOnlyCredentialHandler()
    {
        var handler = DefaultProviderSetupHandlers.Create().Single(h => h.CanHandle("twelve-data"));

        handler.Descriptor.ProviderId.Should().Be("twelvedata");
        handler.Descriptor.DefaultRoutingMode.Should().Be(ProviderConnectionMode.ReadOnly);
        handler.Descriptor.EnableBindingsImmediately.Should().BeTrue();
        handler.Descriptor.AcceptedCredentialFields.Should().ContainSingle(field =>
            field.Name == "ApiKey" && field.Placeholder == "TWELVEDATA_API_KEY");

        var validation = handler.Validate(new ProviderSetupContext(
            ProviderIdOrAlias: "twelvedata-api",
            DisplayName: "Twelve Data",
            Capabilities: ["data"],
            Environment: null,
            ApiKey: "setup-twelve-key"));

        validation.Success.Should().BeTrue();
        validation.Credentials.Should().Contain("ApiKey", "setup-twelve-key");
    }

    [Theory]
    [InlineData("finnhub", "finnhub", "FINNHUB_API_KEY")]
    [InlineData("tiingo", "tiingo", "TIINGO_API_TOKEN")]
    [InlineData("alpha-vantage", "alphavantage", "ALPHA_VANTAGE_API_KEY")]
    [InlineData("nasdaq", "nasdaqdatalink", "NASDAQ_DATA_LINK_API_KEY")]
    [InlineData("twelve-data", "twelvedata", "TWELVEDATA_API_KEY")]
    [InlineData("open-figi", "openfigi", "OPENFIGI_API_KEY")]
    [InlineData("stooq", "stooq", null)]
    public void DefaultProviderSetupHandlers_IncludesSecondaryReadOnlyDataProviders(
        string alias,
        string expectedProviderId,
        string? expectedApiKeyEnvironment)
    {
        var handler = DefaultProviderSetupHandlers.Create().Single(h => h.CanHandle(alias));

        handler.Descriptor.ProviderId.Should().Be(expectedProviderId);
        handler.Descriptor.DefaultRoutingMode.Should().Be(ProviderConnectionMode.ReadOnly);
        handler.Descriptor.EnableBindingsImmediately.Should().BeTrue();

        if (expectedApiKeyEnvironment is null)
        {
            handler.Descriptor.AcceptedCredentialFields.Should().BeEmpty();
        }
        else
        {
            handler.Descriptor.AcceptedCredentialFields.Should().ContainSingle(field =>
                field.Name == "ApiKey" && field.Placeholder == expectedApiKeyEnvironment);
        }
    }

    [Fact]
    public async Task SaveAsync_DoesNotPersistPlaintextSecretsInVaultOrAudit()
    {
        var store = new FileProviderCredentialStore(_root);

        await store.SaveAsync(new ProviderCredentialSaveRequest(
            "alpaca",
            new Dictionary<string, string?>
            {
                ["KeyId"] = "plain-key",
                ["SecretKey"] = "plain-secret"
            },
            Environment: "paper",
            Actor: "test-operator"));

        var vaultText = await File.ReadAllTextAsync(store.VaultPath);
        var auditText = await File.ReadAllTextAsync(Path.Combine(_root, ".mdc", "provider-credentials.audit.jsonl"));

        vaultText.Should().NotContain("plain-key");
        vaultText.Should().NotContain("plain-secret");
        auditText.Should().NotContain("plain-key");
        auditText.Should().NotContain("plain-secret");
        auditText.Should().Contain("\"action\":\"save\"");
    }

    [Fact]
    public async Task SaveAsync_UnknownCredentialFields_AreRejectedAndDoNotUpdateVaultOrAudit()
    {
        var store = new FileProviderCredentialStore(_root);
        await store.SaveAsync(new ProviderCredentialSaveRequest(
            "alpaca",
            new Dictionary<string, string?>
            {
                ["KeyId"] = "known-key",
                ["SecretKey"] = "known-secret"
            },
            Environment: "paper",
            Actor: "test-operator"));

        var act = () => store.SaveAsync(new ProviderCredentialSaveRequest(
            "alpaca",
            new Dictionary<string, string?>
            {
                ["KeyId"] = "replacement-key",
                ["AccessToken"] = "unknown-secret"
            },
            Environment: "paper",
            Actor: "test-operator"));

        var exception = await act.Should().ThrowAsync<ProviderCredentialValidationException>();
        exception.Which.UnknownFields.Should().ContainSingle().Which.Should().Be("AccessToken");
        var read = await store.ReadForProviderAsync("alpaca");
        read.Should().NotBeNull();
        read!.Get("KeyId").Should().Be("known-key");
        read.Get("AccessToken").Should().BeNull();

        var vaultText = await File.ReadAllTextAsync(store.VaultPath);
        var auditText = await File.ReadAllTextAsync(Path.Combine(_root, ".mdc", "provider-credentials.audit.jsonl"));
        vaultText.Should().NotContain("unknown-secret");
        auditText.Should().NotContain("AccessToken");
        auditText.Should().NotContain("unknown-secret");
    }

    [Fact]
    public async Task SaveAsync_PlaidItemAccessTokenField_IsProviderManagedAndStoredEncrypted()
    {
        var store = new FileProviderCredentialStore(_root);
        await store.SaveAsync(new ProviderCredentialSaveRequest(
            "plaid",
            new Dictionary<string, string?>
            {
                ["ClientId"] = "plaid-client",
                ["Secret"] = "plaid-secret"
            },
            Environment: "sandbox",
            Actor: "test-operator"));

        await store.SaveAsync(new ProviderCredentialSaveRequest(
            "plaid",
            new Dictionary<string, string?>
            {
                ["AccessToken:item-1"] = "access-token-1"
            },
            Environment: "sandbox",
            Actor: "test-operator"));

        var read = await store.ReadForProviderAsync("plaid");
        read.Should().NotBeNull();
        read!.Get("ClientId").Should().Be("plaid-client");
        read.Get("Secret").Should().Be("plaid-secret");
        read.Get("AccessToken:item-1").Should().Be("access-token-1");

        var vaultText = await File.ReadAllTextAsync(store.VaultPath);
        vaultText.Should().NotContain("access-token-1");
    }

    [Fact]
    public async Task SaveAsync_KnownCredentialFields_AreCaseInsensitiveAndStoredCanonically()
    {
        var store = new FileProviderCredentialStore(_root);

        await store.SaveAsync(new ProviderCredentialSaveRequest(
            "alpaca",
            new Dictionary<string, string?>
            {
                ["keyid"] = "case-key",
                ["SECRETKEY"] = "case-secret"
            },
            Environment: "paper",
            Actor: "test-operator"));

        var read = await store.ReadForProviderAsync("alpaca");
        var status = await store.GetStatusAsync("alpaca");

        read.Should().NotBeNull();
        read!.Credentials.Keys.Should().BeEquivalentTo(["KeyId", "SecretKey"]);
        read.Get("KeyId").Should().Be("case-key");
        read.Get("SecretKey").Should().Be("case-secret");
        status.PresentFields.Should().BeEquivalentTo(["KeyId", "SecretKey"]);
    }

    [Fact]
    public async Task SaveAsync_BlankKnownCredentialField_RemovesExistingValue()
    {
        var store = new FileProviderCredentialStore(_root);
        await store.SaveAsync(new ProviderCredentialSaveRequest(
            "alpaca",
            new Dictionary<string, string?>
            {
                ["KeyId"] = "remove-key",
                ["SecretKey"] = "remove-secret"
            },
            Environment: "paper",
            Actor: "test-operator"));

        await store.SaveAsync(new ProviderCredentialSaveRequest(
            "alpaca",
            new Dictionary<string, string?>
            {
                ["secretkey"] = "   "
            },
            Environment: "paper",
            Actor: "test-operator"));

        var read = await store.ReadForProviderAsync("alpaca");
        var status = await store.GetStatusAsync("alpaca");

        read.Should().NotBeNull();
        read!.Get("KeyId").Should().Be("remove-key");
        read.Get("SecretKey").Should().BeNull();
        status.CredentialState.Should().Be(ProviderCredentialStateDto.Partial);
        status.MissingFields.Should().ContainSingle().Which.Should().Be("SecretKey");
    }


    [Fact]
    public async Task ReadForProviderAsync_UsesEnvironmentAsReadOnlyLegacyFallback()
    {
        using var env = new EnvironmentScope("POLYGON_API_KEY", "legacy-polygon-key");
        using var dotnet = new EnvironmentScope("DOTNET_ENVIRONMENT", "Test");
        using var aspNetCore = new EnvironmentScope("ASPNETCORE_ENVIRONMENT", "Test");
        using var packaged = new EnvironmentScope("MDC_PACKAGED_BUILD", null);
        using var customer = new EnvironmentScope("MERIDIAN_CUSTOMER_BUILD", null);
        using var overrideFallback = new EnvironmentScope("MDC_PROVIDER_ALLOW_ENV_FALLBACK", null);
        var store = new FileProviderCredentialStore(_root);

        var read = await store.ReadForProviderAsync("polygon");
        var status = await store.GetStatusAsync("polygon");
        await store.DeleteAsync("polygon", "test-operator");

        read.Should().NotBeNull();
        read!.Source.Should().Be(ProviderCredentialSourceDto.Environment);
        read.Get("ApiKey").Should().Be("legacy-polygon-key");
        status.CredentialSource.Should().Be(ProviderCredentialSourceDto.Environment);
        Environment.GetEnvironmentVariable("POLYGON_API_KEY").Should().Be("legacy-polygon-key");
    }

    [Fact]
    public async Task ReadForProviderAsync_DoesNotUseEnvironmentFallbackInProductionOrPackagedBuilds()
    {
        using var env = new EnvironmentScope("POLYGON_API_KEY", "legacy-polygon-key");
        using var dotnet = new EnvironmentScope("DOTNET_ENVIRONMENT", "Production");
        using var aspNetCore = new EnvironmentScope("ASPNETCORE_ENVIRONMENT", "Production");
        using var packaged = new EnvironmentScope("MDC_PACKAGED_BUILD", "true");
        using var customer = new EnvironmentScope("MERIDIAN_CUSTOMER_BUILD", null);
        using var overrideFallback = new EnvironmentScope("MDC_PROVIDER_ALLOW_ENV_FALLBACK", null);
        var store = new FileProviderCredentialStore(_root);

        var read = await store.ReadForProviderAsync("polygon");
        var status = await store.GetStatusAsync("polygon");

        read.Should().BeNull();
        status.CredentialState.Should().Be(ProviderCredentialStateDto.Missing);
        status.CredentialSource.Should().Be(ProviderCredentialSourceDto.None);
    }

    [Fact]
    public async Task ReadForProviderAsync_AllowsExplicitEnvironmentFallbackOverrideForMigration()
    {
        using var env = new EnvironmentScope("POLYGON_API_KEY", "legacy-polygon-key");
        using var dotnet = new EnvironmentScope("DOTNET_ENVIRONMENT", "Production");
        using var aspNetCore = new EnvironmentScope("ASPNETCORE_ENVIRONMENT", "Production");
        using var packaged = new EnvironmentScope("MDC_PACKAGED_BUILD", "true");
        using var overrideFallback = new EnvironmentScope("MDC_PROVIDER_ALLOW_ENV_FALLBACK", "true");
        var store = new FileProviderCredentialStore(_root);

        var read = await store.ReadForProviderAsync("polygon");
        var status = await store.GetStatusAsync("polygon");

        read.Should().NotBeNull();
        read!.Source.Should().Be(ProviderCredentialSourceDto.Environment);
        status.AuditMetadata.Should().Contain("migrationRequired", "store-provider-secrets-in-vault");
    }

    [Fact]
    public async Task DeleteAsync_RemovesLocalCredentialAndKeepsAuditMetadata()
    {
        var store = new FileProviderCredentialStore(_root);
        await store.SaveAsync(new ProviderCredentialSaveRequest(
            "finnhub",
            new Dictionary<string, string?> { ["ApiKey"] = "finnhub-secret" },
            Actor: "test-operator"));

        await store.DeleteAsync("finnhub", "test-operator");

        var status = await store.GetStatusAsync("finnhub");
        var auditText = await File.ReadAllTextAsync(Path.Combine(_root, ".mdc", "provider-credentials.audit.jsonl"));

        status.CredentialState.Should().Be(ProviderCredentialStateDto.Missing);
        status.CredentialSource.Should().Be(ProviderCredentialSourceDto.None);
        auditText.Should().Contain("\"action\":\"delete\"");
        auditText.Should().Contain("\"actor\":\"test-operator\"");
        auditText.Should().NotContain("finnhub-secret");
    }

    private sealed class EnvironmentScope : IDisposable
    {
        private readonly string _name;
        private readonly string? _previous;

        public EnvironmentScope(string name, string? value)
        {
            _name = name;
            _previous = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(_name, _previous);
        }
    }
}
