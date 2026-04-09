using System.IO;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Services;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.Tests.Services;

public sealed class WorkstationOperatingContextServiceTests
{
    [Fact]
    public async Task LoadAsync_ShouldBuildFundCompatibilityContexts()
    {
        var fundContext = await CreateFundContextAsync();
        var storagePath = BuildStoragePath("operating-context");
        var service = new WorkstationOperatingContextService(fundContext, storagePath: storagePath);

        await service.LoadAsync();

        service.Contexts.Should().ContainSingle(context =>
            context.ScopeKind == OperatingContextScopeKind.Fund &&
            context.DisplayName == "Alpha Credit" &&
            context.CompatibilityFundProfileId == "alpha-credit");
    }

    [Fact]
    public async Task SetWindowModeAsync_ShouldPersistModeAndPresetAcrossReload()
    {
        var fundContext = await CreateFundContextAsync();
        var storagePath = BuildStoragePath("operating-context");
        var service = new WorkstationOperatingContextService(fundContext, storagePath: storagePath);

        await service.LoadAsync();
        await service.SelectContextAsync(service.Contexts[0].ContextKey);
        await service.SetWindowModeAsync(BoundedWindowMode.WorkbenchPreset, "accounting-review");

        var reloaded = new WorkstationOperatingContextService(fundContext, storagePath: storagePath);
        await reloaded.LoadAsync();

        reloaded.CurrentWindowMode.Should().Be(BoundedWindowMode.WorkbenchPreset);
        reloaded.CurrentLayoutPresetId.Should().Be("accounting-review");
        reloaded.CurrentContext.Should().NotBeNull();
        reloaded.CurrentContext!.CompatibilityFundProfileId.Should().Be("alpha-credit");
    }

    [Fact]
    public async Task LoadAsync_ShouldRestoreLastContextWithoutRaisingChangedEvent()
    {
        var fundContext = await CreateFundContextAsync();
        var storagePath = BuildStoragePath("operating-context");
        var service = new WorkstationOperatingContextService(fundContext, storagePath: storagePath);

        await service.LoadAsync();
        await service.SelectContextAsync(service.Contexts[0].ContextKey);

        var reloaded = new WorkstationOperatingContextService(fundContext, storagePath: storagePath);
        var changedEventCount = 0;
        reloaded.ActiveContextChanged += (_, _) => changedEventCount++;

        await reloaded.LoadAsync();

        changedEventCount.Should().Be(0);
        reloaded.CurrentContext.Should().NotBeNull();
        reloaded.CurrentContext!.ContextKey.Should().Be(service.Contexts[0].ContextKey);
    }

    private static async Task<FundContextService> CreateFundContextAsync()
    {
        var storagePath = BuildStoragePath("fund-context");
        var service = new FundContextService(storagePath);
        await service.UpsertProfileAsync(new FundProfileDetail(
            FundProfileId: "alpha-credit",
            DisplayName: "Alpha Credit",
            LegalEntityName: "Alpha Credit Master Fund LP",
            BaseCurrency: "USD",
            DefaultWorkspaceId: "governance",
            DefaultLandingPageTag: "GovernanceShell",
            DefaultLedgerScope: FundLedgerScope.Consolidated,
            IsDefault: true));
        await service.SelectFundProfileAsync("alpha-credit");
        return service;
    }

    private static string BuildStoragePath(string category)
        => Path.Combine(
            Path.GetTempPath(),
            "meridian-operating-context-tests",
            category,
            $"{Guid.NewGuid():N}.json");
}
