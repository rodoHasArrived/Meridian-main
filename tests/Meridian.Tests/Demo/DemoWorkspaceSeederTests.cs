using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Meridian;
using Meridian.Contracts.Configuration;
using Meridian.Contracts.Workstation;
using Meridian.Storage.Operations;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Meridian.Testing;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Demo;

/// <summary>
/// Verifies that <c>--seed-demo</c> writes durable, Seeded-labelled records that survive a restart —
/// modelled as fresh store instances reading the same demo root, exactly as a new process would.
/// </summary>
public sealed class DemoWorkspaceSeederTests
{
    [Fact]
    public void WriteDemoConfig_WhenRemoteUrlEnvironmentVariableIsSet_UsesLoopbackBinding()
    {
        using var artifacts = TestArtifactDirectory.Create(nameof(WriteDemoConfig_WhenRemoteUrlEnvironmentVariableIsSet_UsesLoopbackBinding));
        var demoRoot = Path.Combine(artifacts.RootPath, "demo-workspace");
        Directory.CreateDirectory(demoRoot);

        var configPath = DemoWorkspaceCli.WriteDemoConfig(demoRoot);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ASPNETCORE_URLS"] = "http://0.0.0.0:8080",
            })
            .AddJsonFile(configPath)
            .Build();

        var options = ApiHostOptions.FromConfiguration(configuration, port: 8080);

        options.DeploymentMode.Should().Be(MeridianApiDeploymentMode.LocalWorkstation);
        options.Urls.Should().Equal("http://localhost:8080");
    }

    [Fact]
    public async Task SeedAsync_ProducesSeededProvenanceThatSurvivesRestart()
    {
        using var artifacts = TestArtifactDirectory.Create(nameof(SeedAsync_ProducesSeededProvenanceThatSurvivesRestart));
        var baseRoot = Path.Combine(artifacts.RootPath, "data");
        var seeder = new DemoWorkspaceSeeder(baseRoot);

        var report = await seeder.SeedAsync();

        report.Provenance.Should().Be(DemoTenantBlueprint.SeededProvenanceLabel);
        report.Provisioning.ReconciliationLoaded.Should().BeTrue();
        report.Provisioning.StrategyRunLoaded.Should().BeTrue();
        File.Exists(DemoWorkspaceLayout.ResolveSentinelPath(seeder.DemoRoot)).Should().BeTrue();

        // Simulate a process restart: brand-new store instances over the SAME durable demo root, with
        // no shared in-memory state. Reading the records back proves they were persisted, not cached.
        var breaks = new FileReconciliationBreakQueueRepository(
            Path.Combine(seeder.DemoRoot, "workstation"),
            NullLogger<FileReconciliationBreakQueueRepository>.Instance);
        var reloaded = await breaks.GetAllAsync(new ReconciliationBreakQueueScope(
            DemoTenantBlueprint.TenantId,
            DemoTenantBlueprint.CompanyId));

        reloaded.Should().HaveCount(DemoTenantBlueprint.BreakDefinitions.Count);
        reloaded.Should().OnlyContain(item =>
            item.SourceType == DemoTenantBlueprint.SeededSourceType &&
            item.SourceSystem == DemoTenantBlueprint.SeededSourceSystem &&
            item.TenantId == DemoTenantBlueprint.TenantId &&
            item.CompanyId == DemoTenantBlueprint.CompanyId);

        var strategy = new StrategyRunStore(new FileOperationalCaseHistoryStore(seeder.DemoRoot));
        var run = await strategy.GetRunByIdAsync(DemoTenantBlueprint.StrategyRunId);
        run.Should().NotBeNull();
        run!.StrategyName.Should().Be(DemoTenantBlueprint.StrategyName);
        run.EndedAt.Should().NotBeNull();
        run.DataProvenanceToken.Should().Be(DemoTenantBlueprint.SeededSourceType,
            "the seeded strategy run must carry the blocking simulation provenance mark (W9-TRUTH-001)");
    }

    [Fact]
    public async Task SeedAsync_SeedsAllFiveDomainsDurably()
    {
        using var artifacts = TestArtifactDirectory.Create(nameof(SeedAsync_SeedsAllFiveDomainsDurably));
        var baseRoot = Path.Combine(artifacts.RootPath, "data");
        var seeder = new DemoWorkspaceSeeder(baseRoot);

        var report = await seeder.SeedAsync();

        // W9-DEMO-002: provider data, portfolios, ledger records (drafts), reconciliation
        // cases, and report packs all provision over durable storage in one command.
        report.Provisioning.Warnings.Should().BeEmpty();
        report.Provisioning.ReconciliationLoaded.Should().BeTrue();
        report.Provisioning.StrategyRunLoaded.Should().BeTrue();
        report.Provisioning.FundAccountLoaded.Should().BeTrue();
        report.Provisioning.PortfolioPositionsLoaded.Should().BeTrue();
        report.Provisioning.JournalDraftsSeeded.Should().Be(2);
        report.Provisioning.ReportPackLoaded.Should().BeTrue();
        report.MarketHistoryTradePrintsSeeded.Should().Be(
            DemoTenantBlueprint.MarketHistorySymbolList.Count * DemoTenantBlueprint.MarketHistorySessionCount);

        // Restart-modelled durability: fresh store instances over the same demo root.
        var fundAccounts = new Meridian.PortfolioRecords.FundAccounts.InMemoryFundAccountService(
            Path.Combine(seeder.DemoRoot, "governance", "fund-accounts.json"));
        var account = await fundAccounts.GetAccountAsync(DemoTenantBlueprint.FundAccountId);
        account.Should().NotBeNull();
        account!.DisplayName.Should().Be(DemoTenantBlueprint.FundAccountDisplayName);

        var positionStore = new Meridian.Storage.Services.JsonlPositionSnapshotStore(
            new Meridian.Storage.StorageOptions { RootPath = seeder.DemoRoot },
            NullLogger<Meridian.Storage.Services.JsonlPositionSnapshotStore>.Instance);
        var snapshot = await positionStore.GetLatestSnapshotAsync(
            DemoTenantBlueprint.StrategyRunId,
            DemoTenantBlueprint.FundAccountId.ToString("D"));
        snapshot.Should().NotBeNull();
        snapshot!.Positions.Should().HaveCount(DemoTenantBlueprint.Holdings.Count);

        var drafts = new FileManualJournalEntryDraftStore(
            Path.Combine(seeder.DemoRoot, "workstation", "accounting", "manual-journal-drafts.json"));
        var draftList = await drafts.ListAsync(DemoTenantBlueprint.FundProfileId);
        draftList.Should().HaveCount(2);
        draftList.Should().OnlyContain(static draft => draft.Imbalance == 0m,
            "seeded accounting records are balanced drafts for human review, never posted entries");

        var reportPacks = new FileGovernanceReportPackRepository(
            Path.Combine(seeder.DemoRoot, "workstation"),
            NullLogger<FileGovernanceReportPackRepository>.Instance);
        var pack = await reportPacks.GetAsync(DemoTenantBlueprint.ReportPackId);
        pack.Should().NotBeNull();
        pack!.Provenance.DataProvenanceToken.Should().Be(DemoTenantBlueprint.SeededSourceType,
            "the seeded report pack must carry the simulation provenance mark (W9-TRUTH-001)");

        var historyFile = Path.Combine(seeder.DemoRoot, "historical", "SPY", "seeded-trades.jsonl");
        File.Exists(historyFile).Should().BeTrue("seeded market history must be durable provider data");
        (await File.ReadAllLinesAsync(historyFile)).Should().HaveCount(DemoTenantBlueprint.MarketHistorySessionCount);
    }

    [Fact]
    public async Task SeedAsync_IsIdempotent()
    {
        using var artifacts = TestArtifactDirectory.Create(nameof(SeedAsync_IsIdempotent));
        var baseRoot = Path.Combine(artifacts.RootPath, "data");
        var seeder = new DemoWorkspaceSeeder(baseRoot);

        await seeder.SeedAsync();
        var second = await seeder.SeedAsync();

        // Re-running never duplicates casework, but every desk remains loaded.
        second.Provisioning.ReconciliationBreaksSeeded.Should().Be(0);
        second.Provisioning.ReconciliationLoaded.Should().BeTrue();
        second.Provisioning.StrategyRunLoaded.Should().BeTrue();
        second.Provisioning.FundAccountLoaded.Should().BeTrue();
        second.Provisioning.PortfolioPositionsLoaded.Should().BeTrue();
        second.Provisioning.JournalDraftsSeeded.Should().Be(2, "existing drafts are counted, not duplicated");
        second.Provisioning.ReportPackLoaded.Should().BeTrue();
        second.Provisioning.Warnings.Should().BeEmpty();

        var breaks = new FileReconciliationBreakQueueRepository(
            Path.Combine(seeder.DemoRoot, "workstation"),
            NullLogger<FileReconciliationBreakQueueRepository>.Instance);
        (await breaks.GetAllAsync()).Should().HaveCount(DemoTenantBlueprint.BreakDefinitions.Count);

        var drafts = new FileManualJournalEntryDraftStore(
            Path.Combine(seeder.DemoRoot, "workstation", "accounting", "manual-journal-drafts.json"));
        (await drafts.ListAsync(DemoTenantBlueprint.FundProfileId)).Should().HaveCount(2);
    }

}
