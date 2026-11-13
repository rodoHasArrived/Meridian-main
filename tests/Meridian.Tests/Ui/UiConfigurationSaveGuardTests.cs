using System.Text.Json;
using FluentAssertions;
using Meridian.Core.Config;
using Meridian.Ui.Shared.Services;
using AppConfigStore = Meridian.Application.UI.ConfigStore;

namespace Meridian.Tests.Ui;

public sealed class UiConfigurationSaveGuardTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-ui-save-" + Guid.NewGuid().ToString("N"));

    private async Task<ConfigStore> CreateStore(AppConfig? config = null)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "appsettings.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(config ?? new AppConfig(), AppConfigJsonOptions.Write));
        return new ConfigStore(path);
    }

    [Fact]
    public async Task RefusesTenantPosturePlaintextCredentialsAndLiveBrokerageWithoutWriting()
    {
        var store = await CreateStore();
        var baseline = await File.ReadAllTextAsync(store.ConfigPath);
        Func<Task>[] mutations = [
            () => store.UpdateAsync(config => config with { TenantScopeEnforcement = "deployment-boundary" }),
            () => store.UpdateAsync(config => config with { Alpaca = new AlpacaOptions("secret-key-123", "secret-secret-456") }),
            () => store.UpdateAsync(config => config with { Backfill = new BackfillConfig(Providers:
                new BackfillProvidersConfig(Tiingo: new TiingoConfig(ApiToken: "plaintext-tiingo-token"))) }),
            () => store.UpdateAsync(config => config with { IB = new IBOptions(UsePaperTrading: false) })
        ];
        foreach (var mutate in mutations)
        {
            await Assert.ThrowsAsync<UiConfigurationValidationException>(mutate);
            (await File.ReadAllTextAsync(store.ConfigPath)).Should().Be(baseline);
        }
    }

    [Fact]
    public async Task InvalidSettingsLeaveTheAuthoritativeDocumentAndImpactStateUntouched()
    {
        var store = await CreateStore();
        var baseline = await File.ReadAllTextAsync(store.ConfigPath);
        await Assert.ThrowsAsync<UiConfigurationValidationException>(() =>
            store.UpdateAsync(config => config with { Symbols = [new SymbolConfig("")] }));
        (await File.ReadAllTextAsync(store.ConfigPath)).Should().Be(baseline);
        store.GetChangeStatus().Changes.Should().BeEmpty();
    }

    [Fact]
    public async Task AdjustmentChangesRequireExplicitArchiveAndSeriesAcknowledgment()
    {
        var store = await CreateStore(new AppConfig(Backfill: new BackfillConfig()));
        await Assert.ThrowsAsync<UiConfigurationValidationException>(() => store.UpdateAsync(config =>
            config with { Backfill = config.Backfill! with { PreferAdjustedPrices = false } }));
        await store.UpdateWithConfirmationAsync(config =>
            (config with { Backfill = config.Backfill! with { PreferAdjustedPrices = false } }, true), confirmDataSemantics: true);
        store.Load().Backfill!.PreferAdjustedPrices.Should().BeFalse();
        store.GetChangeStatus().RequiresRestart.Should().BeTrue();
    }

    [Fact]
    public async Task AddingBackfillDefaultsDoesNotRequireDataSemanticsAcknowledgment()
    {
        var store = await CreateStore();
        await store.UpdateAsync(config => config with { Backfill = new BackfillConfig() });
        store.Load().Backfill.Should().NotBeNull();
    }

    [Theory]
    [InlineData("polygon")]
    [InlineData("polygon-starter")]
    [InlineData("tiingo")]
    [InlineData("twelvedata")]
    [InlineData("openfigi")]
    [InlineData("unknown-tier")]
    public async Task WholeDocumentUiSaveCannotBypassVendorTierCeilings(string invalidProvider)
    {
        var store = await CreateStore(new AppConfig(Backfill: new BackfillConfig()));
        var baseline = await File.ReadAllTextAsync(store.ConfigPath);
        var current = store.Load();
        var providers = invalidProvider switch
        {
            "polygon" => new BackfillProvidersConfig(Polygon: new(RateLimitPerMinute: 6)),
            "polygon-starter" => new BackfillProvidersConfig(Polygon: new(RateLimitPerMinute: 101, TierId: "starter")),
            "tiingo" => new BackfillProvidersConfig(Tiingo: new(RateLimitPerHour: 51)),
            "twelvedata" => new BackfillProvidersConfig(TwelveData: new(RateLimitPerMinute: 9)),
            "openfigi" => new BackfillProvidersConfig(OpenFigi: new(RateLimitPerMinute: 26)),
            _ => new BackfillProvidersConfig(Polygon: new(TierId: "untrusted-tier-text"))
        };
        var exception = await Assert.ThrowsAsync<UiConfigurationValidationException>(() =>
            store.SaveAsync(current with { Backfill = current.Backfill! with { Providers = providers } }));
        exception.Errors.Keys.Should().Contain(path => path.StartsWith("Backfill.Providers."));
        JsonSerializer.Serialize(exception.Errors).Should().NotContain("untrusted-tier-text");
        (await File.ReadAllTextAsync(store.ConfigPath)).Should().Be(baseline);
        store.GetChangeStatus().Changes.Should().BeEmpty();
    }

    [Fact]
    public async Task WholeDocumentUiSaveAllowsTighterVendorLimits()
    {
        var store = await CreateStore(new AppConfig(Backfill: new BackfillConfig()));
        var current = store.Load();
        await store.SaveAsync(current with { Backfill = current.Backfill! with
        {
            Providers = new BackfillProvidersConfig(Polygon: new(RateLimitPerMinute: 3))
        } });
        store.Load().Backfill!.Providers!.Polygon!.RateLimitPerMinute.Should().Be(3);
        store.GetChangeStatus().RequiresRestart.Should().BeTrue();
    }

    [Fact]
    public async Task WholeDocumentUiSaveCannotForgeProviderValidationAudit()
    {
        var store = await CreateStore(new AppConfig(Backfill: new BackfillConfig()));
        var current = store.Load();
        var next = current with { Backfill = current.Backfill! with { ValidationProfiles = new Dictionary<string, ProviderValidationProfile>
        {
            ["yahoo"] = new()
        }}};
        await Assert.ThrowsAsync<UiConfigurationValidationException>(() => store.SaveAsync(next));
        // Dedicated Application services retain their own typed audit/permission authority.
        await new AppConfigStore(store.ConfigPath).UpdateAsync(config => config with { Backfill = next.Backfill });
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("{broken")]
    [InlineData("[]")]
    [InlineData("{\"execution\":{},\"Execution\":{\"enabled\":true}}")]
    public void DesktopWholeDocumentGuard_RefusesUnreadableSourceBeforePersistence(string invalidSource)
    {
        Action validate = () => UiConfigurationSaveGuard.ValidateWholeDocumentJson(invalidSource, "{}");
        validate.Should().Throw<InvalidDataException>().WithMessage("*repair the source explicitly*");
    }

    [Fact]
    public void DesktopWholeDocumentGuard_RefusesUnmodeledLiveGatewayAndHostModuleChanges()
    {
        foreach (var candidate in new[]
        {
            "{\"execution\":{\"enabled\":true}}",
            "{\"gateways\":{\"live\":true}}",
            "{\"providerModules\":{\"families\":{\"alpaca\":{\"credentials\":{\"KeyId\":\"new-plaintext-secret\"}}}}}"
        })
        {
            Action validate = () => UiConfigurationSaveGuard.ValidateWholeDocumentJson("{}", candidate);
            var failure = validate.Should().Throw<UiConfigurationValidationException>().Which;
            JsonSerializer.Serialize(failure.Errors).Should().NotContain("new-plaintext-secret");
        }
    }

    [Fact]
    public void DesktopWholeDocumentGuard_AllowsUnchangedLegacySecretsAndPreferenceEdits()
    {
        var current = new AppConfig(Alpaca: new AlpacaOptions("retained-key-id", "retained-secret-key"));
        var currentJson = JsonSerializer.Serialize(current, AppConfigJsonOptions.Write);
        var candidateJson = JsonSerializer.Serialize(current with { Compress = true }, AppConfigJsonOptions.Write);
        Action validate = () => UiConfigurationSaveGuard.ValidateWholeDocumentJson(currentJson, candidateJson);
        validate.Should().NotThrow();
    }

    [Fact]
    public void DesktopWholeDocumentGuard_AllowsLegacyPropertyCasingWithoutTreatingRetainedSecretsAsNew()
    {
        const string current = "{\"DataSource\":\"Synthetic\",\"Alpaca\":{\"KeyId\":\"retained-key\",\"SecretKey\":\"retained-secret\"}}";
        const string candidate = "{\"dataSource\":\"Synthetic\",\"alpaca\":{\"keyId\":\"retained-key\",\"secretKey\":\"retained-secret\"},\"compress\":true}";
        Action validate = () => UiConfigurationSaveGuard.ValidateWholeDocumentJson(current, candidate);
        validate.Should().NotThrow();
    }

    [Fact]
    public void DesktopWholeDocumentGuard_RejectsTenantPostureAndOverCeilingVendorLimits()
    {
        var baseline = JsonSerializer.Serialize(new AppConfig(), AppConfigJsonOptions.Write);
        var unsafeConfigs = new[]
        {
            new AppConfig(TenantScopeEnforcement: "deployment-boundary"),
            new AppConfig(Backfill: new BackfillConfig(Providers: new BackfillProvidersConfig(Polygon: new(RateLimitPerMinute: 6))))
        };
        foreach (var candidate in unsafeConfigs)
        {
            Action validate = () => UiConfigurationSaveGuard.ValidateWholeDocumentJson(baseline,
                JsonSerializer.Serialize(candidate, AppConfigJsonOptions.Write));
            validate.Should().Throw<UiConfigurationValidationException>();
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
