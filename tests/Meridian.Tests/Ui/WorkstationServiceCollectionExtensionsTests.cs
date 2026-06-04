using FluentAssertions;
using Meridian.Strategies.Storage;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using CoreConfigStore = Meridian.Application.UI.ConfigStore;

namespace Meridian.Tests.Ui;

public sealed class WorkstationServiceCollectionExtensionsTests
{
    [Fact]
    public void AddWorkstationSharedServices_UsesConfiguredDataRootForStrategyStores()
    {
        var root = Path.Combine(Path.GetTempPath(), "Meridian.Tests", Guid.NewGuid().ToString("N"));
        var configPath = Path.Combine(root, "appsettings.json");
        var configuredDataRoot = Path.Combine(root, "persistent-data");
        Directory.CreateDirectory(root);
        File.WriteAllText(configPath, $$"""{"DataRoot":"{{configuredDataRoot.Replace("\\", "\\\\")}}"}""");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new CoreConfigStore(configPath));

        services.AddWorkstationSharedServices();

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<PromotionRecordStoreOptions>().RootDirectory
            .Should()
            .Be(Path.Combine(configuredDataRoot, "strategies", "promotions"));
        provider.GetRequiredService<StrategyDesignStoreOptions>().RootDirectory
            .Should()
            .Be(Path.Combine(configuredDataRoot, "strategies", "designer"));
        provider.GetRequiredService<ReportingRunStoreOptions>().RootDirectory
            .Should()
            .Be(Path.Combine(configuredDataRoot, "workstation", "reporting", "runs"));
        provider.GetRequiredService<ReportPackWorkflowRecordStoreOptions>().SnapshotPath
            .Should()
            .Be(Path.Combine(configuredDataRoot, "workstation", "reporting", "report-pack-workflows.json"));
        provider.GetRequiredService<ReportTemplateGovernanceStoreOptions>().SnapshotPath
            .Should()
            .Be(Path.Combine(configuredDataRoot, "workstation", "reporting", "report-templates.json"));
    }

    [Fact]
    public void ResolvePersistentDataRoot_UsesConfiguredDataRootFromConfigFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "Meridian.Tests", Guid.NewGuid().ToString("N"));
        var configPath = Path.Combine(root, "config", "appsettings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        File.WriteAllText(configPath, """{"DataRoot":"../portable-data"}""");

        UiServer.ResolvePersistentDataRoot(configPath)
            .Should()
            .Be(Path.GetFullPath(Path.Combine(root, "..", "portable-data")));
    }
}
