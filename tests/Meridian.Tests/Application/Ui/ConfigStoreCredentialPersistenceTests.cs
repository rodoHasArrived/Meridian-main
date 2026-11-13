using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Commands;
using Meridian.Application.Composition;
using Meridian.Application.Composition.Features;
using Meridian.Application.ProviderRouting;
using Meridian.Application.Services;
using Meridian.Application.UI;
using Meridian.Application.Wizard.Core;
using Meridian.Application.Wizard.Steps;
using Meridian.Core.Config;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;

namespace Meridian.Tests.Application.UI;

public sealed class ConfigStoreCredentialPersistenceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-core-credential-persistence", Guid.NewGuid().ToString("N"));
    private string ConfigPath => Path.Combine(_root, "appsettings.json");
    private static ProviderCredentialOwnershipPolicy Required => new(true);

    [Theory]
    [InlineData("save")]
    [InlineData("update")]
    [InlineData("required-update")]
    [InlineData("feature-overrides")]
    [InlineData("configuration-service")]
    [InlineData("wizard-static")]
    [InlineData("wizard-step")]
    public async Task ProductionWrites_RefuseLegacySecretsAndLeaveOriginalDocumentUnchanged(string operation)
    {
        Directory.CreateDirectory(_root);
        var legacy = new AppConfig(Alpaca: new(KeyId: "synthetic-legacy-key", SecretKey: "synthetic-legacy-secret"));
        await new ConfigStore(ConfigPath, new(false)).SaveAsync(legacy);
        var before = await File.ReadAllBytesAsync(ConfigPath);
        var store = new ConfigStore(ConfigPath, Required);
        store.LoadRequired().Alpaca!.KeyId.Should().Be("synthetic-legacy-key", "migration reads remain available");
        Func<Task> write = operation switch
        {
            "save" => () => store.SaveAsync(store.LoadRequired() with { Compress = true }),
            "update" => () => store.UpdateAsync(current => current with { Compress = true }),
            "required-update" => () => store.UpdateRequiredAsync(current => (current with { Compress = true }, true)),
            "feature-overrides" => () => store.SaveFeatureCapabilityOverridesAsync(new Dictionary<string, bool> { ["Synthetic"] = true }),
            "wizard-static" => () => Task.Run(() => ConfigurationWizard.WriteConfiguration(legacy, ConfigPath, ownershipPolicy: Required)),
            "wizard-step" => () => new SaveConfigurationStep(new StringWriter(), new StringReader("y\n"),
                path => new ConfigStore(path ?? ConfigPath, Required)).ExecuteAsync(new WizardContext { FinalConfig = legacy }, CancellationToken.None),
            _ => () => new ConfigurationService(ownershipPolicy: Required).SaveConfigAsync(legacy, ConfigPath)
        };

        var failure = await write.Should().ThrowAsync<ProviderConfigurationCredentialException>();
        failure.Which.Message.Should().Contain("Manage owned credentials").And.Contain("before production");
        failure.Which.ToString().Should().NotContain("synthetic-legacy-key").And.NotContain("synthetic-legacy-secret");
        (await File.ReadAllBytesAsync(ConfigPath)).Should().Equal(before);
    }

    [Theory]
    [InlineData("production")]
    [InlineData("packaged")]
    public async Task Composition_UsesFinalPostureForCoreStoreAndConfigurationService(string posture)
    {
        Directory.CreateDirectory(_root);
        await new ConfigStore(ConfigPath, new(false)).SaveAsync(new AppConfig());
        var before = await File.ReadAllBytesAsync(ConfigPath);
        var services = new ServiceCollection();
        services.AddSingleton(new CompositionConfiguration(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            ["MDC_PACKAGED_BUILD"] = posture == "packaged" ? "true" : "false"
        }).Build()));
        new ConfigurationFeatureRegistration().Register(services, new CompositionOptions { ConfigPath = ConfigPath });
        services.DeclareMeridianDeploymentPosture(posture == "production"
            ? MeridianDeploymentPosture.ProductionApi : MeridianDeploymentPosture.LocalWorkstation);
        using var provider = services.BuildServiceProvider();
        var secret = new AppConfig(Polygon: new(ApiKey: "synthetic-new-secret"));

        Func<Task> saveStore = () => provider.GetRequiredService<ConfigStore>().SaveAsync(secret);
        Func<Task> saveService = () => provider.GetRequiredService<ConfigurationService>().SaveConfigAsync(secret, ConfigPath);
        await saveStore.Should().ThrowAsync<ProviderConfigurationCredentialException>();
        await saveService.Should().ThrowAsync<ProviderConfigurationCredentialException>();
        (await File.ReadAllBytesAsync(ConfigPath)).Should().Equal(before);
    }

    [Fact]
    public async Task ProductionPurge_RemovesOnlyReviewedConfigCredentialsAndPreservesOwnedVaultState()
    {
        Directory.CreateDirectory(_root);
        var old = new AppConfig(Alpaca: new(KeyId: "synthetic-legacy-key"), ProviderConnections: new(
            Connections: [new("owned", "alpaca", "Owned", TenantId: "tenant-a", ExternalAccountId: "account-a", CredentialEnvironment: "paper")]));
        await new ConfigStore(ConfigPath, new(false)).SaveAsync(old);
        var vault = new Meridian.DataIntegration.Credentials.FileProviderCredentialStore(_root);
        var scope = new Meridian.DataIntegration.Credentials.ProviderCredentialScope("tenant-a", "owned", "account-a", "paper");
        await vault.SaveScopedAsync(new("alpaca", new Dictionary<string, string?> { ["KeyId"] = "owned-key", ["SecretKey"] = "owned-secret" }, "paper"), scope);

        var store = new ConfigStore(ConfigPath, Required);
        await store.UpdateAsync(current => current with { Alpaca = current.Alpaca! with { KeyId = "", SecretKey = "" } });
        await store.UpdateAsync(current => current with { Compress = true });
        store.LoadRequired().ProviderConnections.Should().BeEquivalentTo(old.ProviderConnections);
        (await vault.ReadScopedAsync("alpaca", scope))!.Get("KeyId").Should().Be("owned-key");
        (await File.ReadAllTextAsync(ConfigPath)).Should().NotContain("synthetic-legacy-key").And.NotContain("owned-secret");
    }

    [Fact]
    public async Task DevelopmentWrites_RetainCompatibilityAndSafeExtensionText()
    {
        var config = new AppConfig(Polygon: new(ApiKey: "synthetic-dev-key"))
        {
            AdditionalSections = new() { ["Notes"] = JsonSerializer.SerializeToElement(new { description = "ApiKey is an option name." }) }
        };
        var store = new ConfigStore(ConfigPath, new(false));
        await store.SaveAsync(config);
        await store.UpdateAsync(current => current with { Compress = true });
        store.LoadRequired().Polygon!.ApiKey.Should().Be("synthetic-dev-key");
    }

    [Theory]
    [InlineData("${ALPACA_KEY_ID}", "${ALPACA_SECRET_KEY}")]
    [InlineData("", "")]
    [InlineData(null, null)]
    public async Task ProductionWrites_PreserveDocumentedEnvironmentReferencesAndNonSecretIdentifiers(string? key, string? secret)
    {
        var candidate = new AppConfig(Alpaca: new(KeyId: key!, SecretKey: secret!))
        {
            AdditionalSections = new()
            {
                ["QBO_REALM_ID"] = JsonSerializer.SerializeToElement("retained-company"),
                ["XERO_TENANT_ID"] = JsonSerializer.SerializeToElement("retained-tenant"),
                ["Notes"] = JsonSerializer.SerializeToElement(new { ApiKey = "This is an option name in a note." })
            }
        };
        var store = new ConfigStore(ConfigPath, Required);

        await store.SaveAsync(candidate);
        await store.SaveFeatureCapabilityOverridesAsync(new Dictionary<string, bool> { ["Synthetic"] = true });

        store.LoadRequired().Alpaca!.KeyId.Should().Be(key);
        store.LoadRequired().AdditionalSections!["QBO_REALM_ID"].GetString().Should().Be("retained-company");
    }

    [Theory]
    [InlineData("{\"Alpaca\":{\"KeyId\":\"${ALPACA_KEY_ID:-synthetic-secret}\"}}")]
    [InlineData("{\"Alpaca\":{\"SecretKey\":\"${UNKNOWN_SECRET}\"}}")]
    [InlineData("{\"Backfill\":{\"Providers\":{},\"Providers\":{\"TwelveData\":{\"ApiKey\":\"synthetic-secret\"}}}}")]
    [InlineData("{\"ProviderModules\":{\"Modules\":{},\"Modules\":{\"alpaca\":{\"Settings\":{},\"Settings\":{\"SecretKey\":\"synthetic-secret\"}}}}}")]
    [InlineData("{\"DataSources\":{\"Sources\":[],\"Sources\":[{\"Polygon\":{\"ApiKey\":\"synthetic-secret\"}}]}}")]
    public void SerializedSnapshotValidation_RefusesEmbeddedDefaultsAndDuplicateKnownSections(string json)
    {
        Action validate = () => ProviderConfigurationCredentialGuard.ValidateJson(json, Required);
        validate.Should().Throw<ProviderConfigurationCredentialException>().Which.ToString().Should().NotContain("synthetic-secret");
    }

    [Fact]
    public void SerializedSnapshotValidation_AcceptsCommentsAndTrailingCommasButRefusesMalformedProductionCopies()
    {
        ProviderConfigurationCredentialGuard.ValidateJson("{/* retained comment */\"Alpaca\":{\"KeyId\":\"${ALPACA_KEY_ID}\",},}", Required);
        Action malformed = () => ProviderConfigurationCredentialGuard.ValidateJson("{\"Alpaca\":", Required);
        malformed.Should().Throw<JsonException>();
    }

    [Fact]
    public async Task ProductionPreset_RefusesToDuplicateAnUnsafeOriginalIntoAPlaintextBackup()
    {
        Directory.CreateDirectory(_root);
        await new ConfigStore(ConfigPath, new(false)).SaveAsync(new AppConfig(Alpaca: new(KeyId: "synthetic-legacy-key")));
        var before = await File.ReadAllBytesAsync(ConfigPath);
        var command = new ConfigPresetCommand(new AutoConfigurationService(), Logger.None, Required);

        Func<Task> apply = () => command.ExecuteAsync(["--preset", "researcher", "--output", ConfigPath]);

        await apply.Should().ThrowAsync<ProviderConfigurationCredentialException>();
        (await File.ReadAllBytesAsync(ConfigPath)).Should().Equal(before);
        Directory.GetFiles(_root, "*.backup*").Should().BeEmpty();
    }

    [Theory]
    [InlineData("Alpaca", false)]
    [InlineData("Alpaca", true)]
    [InlineData("Polygon", false)]
    [InlineData("Polygon", true)]
    public async Task ProductionInteractiveValidation_RefusesLiteralOrEnvironmentCredentialsBeforeNetwork(string provider, bool reference)
    {
        var output = new StringWriter();
        var calls = 0;
        var step = new ValidateCredentialsStep(output, new StringReader(""), Required, (_, _) =>
        {
            calls++;
            throw new InvalidOperationException("The unowned validator must not run.");
        });
        var dataSource = provider == "Alpaca"
            ? new DataSourceSelection { DataSource = DataSourceKind.Alpaca, Alpaca = new(KeyId: reference ? "${ALPACA_KEY_ID}" : "synthetic-key", SecretKey: reference ? "${ALPACA_SECRET_KEY}" : "synthetic-secret") }
            : new DataSourceSelection { DataSource = DataSourceKind.Polygon, Polygon = new(ApiKey: reference ? "${POLYGON_API_KEY}" : "synthetic-secret") };

        Func<Task> validate = () => step.ExecuteAsync(new WizardContext { DataSource = dataSource }, CancellationToken.None);

        await validate.Should().ThrowAsync<ProviderConfigurationCredentialException>();
        calls.Should().Be(0);
        output.ToString().Should().NotContain("synthetic-secret").And.NotContain("Validating credentials with provider API");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProductionInteractiveReview_RefusesCredentialPreviewBeforePublishingFinalConfig(bool backfill)
    {
        var output = new StringWriter();
        var step = new ReviewConfigurationStep(output, new StringReader("\n"), Required);
        var context = new WizardContext
        {
            DataSource = new DataSourceSelection { DataSource = DataSourceKind.Alpaca, Alpaca = backfill ? null : new(KeyId: "synthetic-secret") },
            Backfill = backfill ? new(Providers: new(Tiingo: new(ApiToken: "synthetic-secret"))) : null
        };

        Func<Task> review = () => step.ExecuteAsync(context, CancellationToken.None);

        await review.Should().ThrowAsync<ProviderConfigurationCredentialException>();
        context.FinalConfig.Should().BeNull();
        output.ToString().Should().NotContain("synthetic-secret").And.NotContain("```json");
    }

    [Fact]
    public async Task DevelopmentInteractiveSetup_PreservesValidationAndReviewCompatibility()
    {
        var output = new StringWriter();
        var calls = 0;
        var context = new WizardContext
        {
            DataSource = new DataSourceSelection { DataSource = DataSourceKind.Alpaca, Alpaca = new(KeyId: "synthetic-key", SecretKey: "synthetic-secret") }
        };
        var validator = new ValidateCredentialsStep(output, new StringReader(""), new(false), (_, _) =>
        {
            calls++;
            return Task.FromResult<CredentialValidationService.ValidationResult?>(new("Alpaca", true, "Synthetic validation", null, TimeSpan.Zero));
        });
        var reviewer = new ReviewConfigurationStep(output, new StringReader("\n"), new(false));

        (await validator.ExecuteAsync(context, CancellationToken.None)).Status.Should().Be(WizardStepStatus.Success);
        (await reviewer.ExecuteAsync(context, CancellationToken.None)).Status.Should().Be(WizardStepStatus.Success);

        calls.Should().Be(1);
        context.FinalConfig.Should().NotBeNull();
        output.ToString().Should().Contain("synthetic-secret");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
