using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Configuration;
using Meridian.Contracts.Domain.Enums;
using Meridian.Ui.Services;
using Meridian.Ui.Services.Services;
using Meridian.Ui.Shared.Services;
using CoreConfigStore = Meridian.Application.UI.ConfigStore;

namespace Meridian.Ui.Tests.Services;

public sealed class GuardedSharedConfigServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-shared-config-" + Guid.NewGuid().ToString("N"));
    private string ConfigPath => Path.Combine(_root, "nested", "config", "appsettings.json");
    private ConfigService CreateService() => new(() => ConfigPath);

    private async Task Seed(string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        await File.WriteAllTextAsync(ConfigPath, json);
    }

    [Fact]
    public async Task FirstSaveValidatesAndCreatesMissingConfiguration()
    {
        var service = CreateService();
        await service.SaveConfigAsync(new AppConfigDto { DataSource = "Synthetic", Symbols = [new() { Symbol = "AAPL" }] });
        var saved = await service.LoadConfigAsync();
        saved.Should().NotBeNull();
        saved!.DataSource.Should().Be("Synthetic");
        saved.Symbols.Should().ContainSingle(symbol => symbol.Symbol == "AAPL");
        var host = JsonSerializer.Deserialize<Meridian.Core.Config.AppConfig>(
            await File.ReadAllTextAsync(ConfigPath), Meridian.Core.Config.AppConfigJsonOptions.Read)!;
        host.Symbols.Should().ContainSingle(symbol => symbol.Symbol == "AAPL" && symbol.InstrumentType == InstrumentType.Equity);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DtoWriterOmitsOptionalNullScalarsAndPreservesExplicitSymbolEnums(bool useSharedBaseOptions)
    {
        var draft = new AppConfigDto { DataSource = "Synthetic", Symbols = [new()
        {
            Symbol = "SPX", SecurityType = "OPT", InstrumentType = "IndexOption", OptionStyle = "European",
            Strike = 5000m, Right = "Call", LastTradeDateOrContractMonth = "20270115"
        }] };
        string json;
        if (useSharedBaseOptions)
            json = SharedOptionsProbe.Serialize(draft);
        else
        {
            await CreateService().SaveConfigAsync(draft);
            json = await File.ReadAllTextAsync(ConfigPath);
        }
        UiConfigurationSaveGuard.ValidateWholeDocumentJson("{}", json);
        var host = JsonSerializer.Deserialize<Meridian.Core.Config.AppConfig>(json,
            Meridian.Core.Config.AppConfigJsonOptions.Read)!;
        host.Symbols.Should().ContainSingle(symbol => symbol.InstrumentType == InstrumentType.IndexOption
            && symbol.OptionStyle == OptionStyle.European && symbol.Right == OptionRight.Call);
        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("symbols")[0].TryGetProperty("localSymbol", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("[]")]
    public async Task UnreadableExistingConfigurationCannotBeReplacedByDefaultDraft(string source)
    {
        await Seed(source);
        var service = CreateService();
        await Assert.ThrowsAsync<InvalidDataException>(() => service.SaveConfigAsync(new AppConfigDto { DataSource = "Synthetic" }));
        (await File.ReadAllTextAsync(ConfigPath)).Should().Be(source);
    }

    [Fact]
    public async Task FirstSaveCannotWritePlaintextCredentials()
    {
        var service = CreateService();
        await Assert.ThrowsAsync<UiConfigurationValidationException>(() => service.SaveAlpacaOptionsAsync(new AlpacaOptionsDto
        {
            KeyId = "fixture-private-key", SecretKey = "fixture-private-secret"
        }));
        File.Exists(ConfigPath).Should().BeFalse();
    }

    [Theory]
    [InlineData("tenantScopeEnforcement", "\"deployment-boundary\"")]
    [InlineData("brokerage", "{\"liveExecutionEnabled\":true,\"gateway\":\"ib\"}")]
    public async Task ExtensionDataCannotChangeTenancyOrActivateLiveExecution(string key, string value)
    {
        const string baseline = "{\"dataSource\":\"Synthetic\",\"dataRoot\":\"data\"}";
        await Seed(baseline);
        var service = CreateService();
        var config = (await service.LoadConfigAsync())!;
        config.AdditionalSections ??= new();
        config.AdditionalSections[key] = JsonSerializer.Deserialize<JsonElement>(value);
        await Assert.ThrowsAsync<UiConfigurationValidationException>(() => service.SaveConfigAsync(config));
        (await File.ReadAllTextAsync(ConfigPath)).Should().Be(baseline);
    }

    [Fact]
    public async Task StaleUiDraftCannotEraseConcurrentHostChange()
    {
        await Seed("{\"dataSource\":\"Synthetic\",\"dataRoot\":\"data\"}");
        var service = CreateService();
        var draft = (await service.LoadConfigAsync())!;
        await new CoreConfigStore(ConfigPath).UpdateAsync(config => config with { Compress = true });
        var concurrent = await File.ReadAllTextAsync(ConfigPath);
        draft.Symbols = [new() { Symbol = "MSFT" }];
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveConfigAsync(draft));
        (await File.ReadAllTextAsync(ConfigPath)).Should().Be(concurrent);
    }

    [Fact]
    public async Task SaveWaitsForSharedWriteLeaseAndHonorsCancellation()
    {
        await Seed("{\"dataSource\":\"Synthetic\"}");
        using var lease = await new CoreConfigStore(ConfigPath).AcquireWriteLeaseAsync();
        using var cancelled = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateService().SaveConfigAsync(new AppConfigDto(), cancelled.Token));
        (await File.ReadAllTextAsync(ConfigPath)).Should().Be("{\"dataSource\":\"Synthetic\"}");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class SharedOptionsProbe : ConfigServiceBase
    {
        public static string Serialize(AppConfigDto config) => JsonSerializer.Serialize(config, SharedJsonOptions);
        public override string ConfigPath => throw new NotSupportedException();
        protected override Task<AppConfigDto?> LoadConfigCoreAsync(CancellationToken ct = default) => throw new NotSupportedException();
        protected override Task SaveConfigCoreAsync(AppConfigDto config, CancellationToken ct = default) => throw new NotSupportedException();
        protected override void LogError(string message, Exception? exception = null) => throw new NotSupportedException();
    }
}
