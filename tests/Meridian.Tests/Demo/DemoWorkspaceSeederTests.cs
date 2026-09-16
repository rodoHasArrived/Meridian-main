using System.IO;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using FluentAssertions;
using Meridian;
using Meridian.Contracts.Configuration;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Workstation;
using Meridian.DataIntegration.Historical;
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

        // Market history is asserted through the reader rather than by path, because a durable file
        // the Data desk cannot discover is not seeded provider data. See
        // SeedAsync_SeedsMarketHistoryTheDataDeskCanRead below for the discovery contract itself.
        var history = new HistoricalDataQueryService(seeder.DemoRoot);
        history.GetAvailableSymbols()
            .Should().Contain(DemoTenantBlueprint.MarketHistorySymbolList);
    }

    /// <summary>
    /// The seeded history has to satisfy the discovery rules the Data desk actually applies, not
    /// merely exist on disk. <c>HistoricalDataQueryService</c> lists symbols from top-level
    /// directories of the data root and finds a symbol's files under <c>{root}/{SYMBOL}</c> or by
    /// matching the symbol against a file name — so history written to a demo-only path with a
    /// symbol-free file name reads as an empty Data desk while every file is present.
    /// </summary>
    [Fact]
    public async Task SeedAsync_SeedsMarketHistoryTheDataDeskCanRead()
    {
        using var artifacts = TestArtifactDirectory.Create(nameof(SeedAsync_SeedsMarketHistoryTheDataDeskCanRead));
        var baseRoot = Path.Combine(artifacts.RootPath, "data");
        var seeder = new DemoWorkspaceSeeder(baseRoot);

        await seeder.SeedAsync();

        // Restart-modelled: a fresh reader over the same demo root, exactly as the serving host
        // composes it (HistoricalDataQueryService is registered with the config data root).
        var history = new HistoricalDataQueryService(seeder.DemoRoot);

        history.GetAvailableSymbols().Should().Contain(DemoTenantBlueprint.MarketHistorySymbolList);

        foreach (var symbol in DemoTenantBlueprint.MarketHistorySymbolList)
        {
            var result = await history.QueryAsync(new HistoricalDataQuery(symbol));

            result.Records.Should().HaveCount(
                DemoTenantBlueprint.MarketHistorySessionCount,
                "every seeded session for {0} must be readable", symbol);
            result.Records.Should().OnlyContain(record =>
                record.Symbol == symbol && record.EventType == nameof(MarketEventType.Trade));
        }

        // The bar aggregation that powers the Data desk price chart must attribute the seeded
        // series to the sample source, so a simulated series can never read as provider data.
        var bars = await history.GetBarsAsync(new HistoricalBarsQuery(
            Symbol: "SPY",
            IntervalMinutes: 1440,
            From: DateOnly.FromDateTime(DateTime.UtcNow.Date).AddDays(-200),
            To: DateOnly.FromDateTime(DateTime.UtcNow.Date)));

        bars.Success.Should().BeTrue();
        bars.Bars.Should().HaveCount(DemoTenantBlueprint.MarketHistorySessionCount);
        bars.Bars.Should().OnlyContain(bar =>
            bar.Close > 0m && bar.Source == DemoTenantBlueprint.MarketHistorySource);
        bars.Sources.Should().Equal(DemoTenantBlueprint.MarketHistorySource);
    }

    /// <summary>
    /// Re-seeding on a later date must converge on the documented session window instead of
    /// stacking a new session onto the previous seed's files.
    /// </summary>
    [Fact]
    public async Task SeedAsync_WhenAnEarlierSeedLeftOtherSessions_PrunesOnlyItsOwnFiles()
    {
        using var artifacts = TestArtifactDirectory.Create(nameof(SeedAsync_WhenAnEarlierSeedLeftOtherSessions_PrunesOnlyItsOwnFiles));
        var baseRoot = Path.Combine(artifacts.RootPath, "data");
        var seeder = new DemoWorkspaceSeeder(baseRoot);

        await seeder.SeedAsync();

        // Model a seed taken on an earlier date: a session file the current run no longer writes,
        // recorded in the manifest as the seeder's own.
        var tradeDirectory = Path.Combine(seeder.DemoRoot, "SPY", "Trade");
        var supersededRelative = Path.Combine("SPY", "Trade", "1999-01-04.jsonl");
        var superseded = Path.Combine(seeder.DemoRoot, supersededRelative);
        await File.WriteAllTextAsync(superseded, "{}\n");

        // Data an operator collected into the demo workspace is not the seeder's to remove. A real
        // readable print, so the assertion below distinguishes "left on disk" from "still served".
        var operatorCapture = Path.Combine(tradeDirectory, "operator-capture.jsonl");
        await File.WriteAllTextAsync(
            operatorCapture,
            """
            {"type":"Trade","symbol":"SPY","timestamp":"2026-01-05T15:30:00.0000000+00:00","source":"ALPACA","payload":{"kind":"trade","price":1.23,"size":1}}
            """);

        var manifestPath = Path.Combine(seeder.DemoRoot, ".meridian-demo-market-history.json");
        var manifest = JsonNode.Parse(await File.ReadAllTextAsync(manifestPath))!.AsObject();
        manifest["files"]!.AsArray().Add(supersededRelative);
        await File.WriteAllTextAsync(manifestPath, manifest.ToJsonString());

        await seeder.SeedAsync();

        File.Exists(superseded).Should().BeFalse("a session the seeder recorded and no longer writes is pruned");
        File.Exists(operatorCapture).Should().BeTrue("the seeder only prunes files its own manifest claims");

        var history = new HistoricalDataQueryService(seeder.DemoRoot);
        var result = await history.QueryAsync(new HistoricalDataQuery("SPY"));
        result.Records.Should().HaveCount(
            DemoTenantBlueprint.MarketHistorySessionCount + 1,
            "the seeded window is unchanged and the operator's own print is still served");
    }

    /// <summary>
    /// A workspace seeded before the history moved onto the storage policy's layout must converge
    /// rather than keep an unreadable copy of the same sessions beside the readable one.
    /// </summary>
    [Fact]
    public async Task SeedAsync_WhenTheWorkspaceCarriesLegacyHistory_RemovesIt()
    {
        using var artifacts = TestArtifactDirectory.Create(nameof(SeedAsync_WhenTheWorkspaceCarriesLegacyHistory_RemovesIt));
        var baseRoot = Path.Combine(artifacts.RootPath, "data");
        var seeder = new DemoWorkspaceSeeder(baseRoot);

        var legacyRoot = Path.Combine(seeder.DemoRoot, "historical");
        foreach (var symbol in new[] { "SPY", "DELISTED" })
        {
            // DELISTED models a symbol the blueprint no longer names: migration keys off the
            // legacy layout, not the current symbol list, so its file goes too.
            var legacyDirectory = Path.Combine(legacyRoot, symbol);
            Directory.CreateDirectory(legacyDirectory);
            await File.WriteAllTextAsync(Path.Combine(legacyDirectory, "seeded-trades.jsonl"), "{}\n");
        }

        await seeder.SeedAsync();

        Directory.Exists(legacyRoot).Should().BeFalse();
    }

    /// <summary>
    /// Migration removes only the file name the previous seeder owned. Anything else an operator
    /// filed under the legacy root stays, and the root is kept because it is no longer empty.
    /// </summary>
    [Fact]
    public async Task SeedAsync_WhenLegacyRootHoldsOperatorFiles_KeepsThem()
    {
        using var artifacts = TestArtifactDirectory.Create(nameof(SeedAsync_WhenLegacyRootHoldsOperatorFiles_KeepsThem));
        var baseRoot = Path.Combine(artifacts.RootPath, "data");
        var seeder = new DemoWorkspaceSeeder(baseRoot);

        var legacyDirectory = Path.Combine(seeder.DemoRoot, "historical", "SPY");
        Directory.CreateDirectory(legacyDirectory);
        await File.WriteAllTextAsync(Path.Combine(legacyDirectory, "seeded-trades.jsonl"), "{}\n");
        var operatorArchive = Path.Combine(legacyDirectory, "SPY-2019-archive.jsonl");
        await File.WriteAllTextAsync(operatorArchive, "{}\n");

        await seeder.SeedAsync();

        File.Exists(Path.Combine(legacyDirectory, "seeded-trades.jsonl")).Should().BeFalse();
        File.Exists(operatorArchive).Should().BeTrue("migration removes only the file the seeder wrote");
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
