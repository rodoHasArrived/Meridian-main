using System.Text;
using System.Text.Json.Nodes;
using Meridian.Contracts.Configuration;
using Meridian.Contracts.Domain;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Core.Serialization;
using Meridian.Domain.Events;
using Meridian.PortfolioRecords.FundAccounts;
using Meridian.Storage;
using Meridian.Storage.Archival;
using Meridian.Storage.Operations;
using Meridian.Storage.Policies;
using Meridian.Storage.Services;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Provisions and tears down the isolated Meridian demo workspace used by the <c>--seed-demo</c> /
/// <c>--reset-demo</c> host verbs and by the <c>--demo</c> serve mode.
/// </summary>
/// <remarks>
/// <para>
/// The seeder composes the same durable, desk-read stores the workstation serves — it never
/// introduces throwaway fixtures. Every store is rooted at the dedicated demo root
/// (<c>{dataRoot}/demo-workspace</c>) using the exact path layout the serving host reads
/// (<c>{root}/workstation</c> for reconciliation casework, <c>{root}/operations</c> for the
/// hash-chained strategy case history, and the storage policy's own
/// <c>{root}/{SYMBOL}/Trade/{date}.jsonl</c> partitions for market history), so a host started
/// with the demo root active renders the seeded data end-to-end. Seeding is idempotent:
/// re-running never duplicates casework or runs, and converges the seeded history on the
/// documented session window.
/// </para>
/// <para>
/// All seeded records carry the Blueprint-1 <see cref="DemoTenantBlueprint.SeededProvenanceLabel"/>
/// provenance, and teardown is guarded by <see cref="DemoWorkspaceLayout.EnsureSafeToDelete"/> so
/// <c>--reset-demo</c> can only ever remove the dedicated demo root — never a non-demo data root.
/// </para>
/// </remarks>
public sealed class DemoWorkspaceSeeder
{
    /// <summary>Records the session files a seed wrote, so the next seed can prune only its own.</summary>
    private const string MarketHistoryManifestFileName = ".meridian-demo-market-history.json";

    private const string MarketHistoryManifestSchema = "meridian.demo-market-history/v1";

    /// <summary>Demo-only market-history layout written before the seeder moved to the storage policy.</summary>
    private const string LegacyMarketHistoryFolderName = "historical";

    private const string LegacyMarketHistoryFileName = "seeded-trades.jsonl";

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private readonly string _demoRoot;
    private readonly string _baseDataRoot;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;

    /// <summary>
    /// Creates a seeder for the demo root derived from <paramref name="baseDataRoot"/>.
    /// </summary>
    /// <param name="baseDataRoot">The active data root; the demo root is nested one level beneath it.</param>
    /// <param name="loggerFactory">Optional logger factory; a null factory disables seeding logs.</param>
    public DemoWorkspaceSeeder(string baseDataRoot, ILoggerFactory? loggerFactory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDataRoot);
        _baseDataRoot = Path.GetFullPath(baseDataRoot);
        _demoRoot = DemoWorkspaceLayout.ResolveDemoRoot(_baseDataRoot);
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        _logger = _loggerFactory.CreateLogger<DemoWorkspaceSeeder>();
    }

    /// <summary>The isolated demo root this seeder operates on.</summary>
    public string DemoRoot => _demoRoot;

    /// <summary>
    /// Seeds the demo workspace durably and idempotently. Creates the demo root, writes the demo
    /// sentinel, and provisions the reconciliation and strategy desks through the real desk stores.
    /// </summary>
    public async Task<DemoWorkspaceSeedReport> SeedAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(_demoRoot);
        await WriteSentinelAsync(ct).ConfigureAwait(false);

        var workstationDirectory = Path.Combine(_demoRoot, "workstation");
        var breaks = new FileReconciliationBreakQueueRepository(
            workstationDirectory,
            _loggerFactory.CreateLogger<FileReconciliationBreakQueueRepository>());

        // FileOperationalCaseHistoryStore appends "operations/case-history.jsonl" under the root it is
        // given, matching the serving host's DI (FileOperationalCaseHistoryStore(dataRoot)), so the
        // seeded, hash-chained strategy run is read back by the running workstation.
        var strategyRuns = new StrategyRunStore(new FileOperationalCaseHistoryStore(_demoRoot));

        // Every store below mirrors the serving host's path resolution for a config whose
        // dataRoot is the demo root, so the running workstation reads the seeded records
        // end-to-end: fund accounts under {root}/governance, position snapshots under
        // {root}/portfolios, journal drafts and report packs under {root}/workstation.
        var fundAccounts = new InMemoryFundAccountService(
            Path.Combine(_demoRoot, "governance", "fund-accounts.json"));
        var positionSnapshots = new JsonlPositionSnapshotStore(
            new StorageOptions { RootPath = _demoRoot },
            _loggerFactory.CreateLogger<JsonlPositionSnapshotStore>());
        var journalDrafts = new FileManualJournalEntryDraftStore(
            Path.Combine(workstationDirectory, "accounting", "manual-journal-drafts.json"));
        var reportPacks = new FileGovernanceReportPackRepository(
            workstationDirectory,
            _loggerFactory.CreateLogger<FileGovernanceReportPackRepository>());

        var provisioner = new DemoTenantProvisioner(
            breaks,
            strategyRuns,
            _loggerFactory.CreateLogger<DemoTenantProvisioner>(),
            fundAccounts,
            positionSnapshots,
            journalDrafts,
            reportPacks);

        var provisioning = await provisioner.ProvisionAsync(ct).ConfigureAwait(false);
        var marketHistoryPrints = await SeedMarketHistoryAsync(ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Seeded demo workspace at {DemoRoot} (breaks={BreaksSeeded}, reconciliation={ReconciliationLoaded}, strategy={StrategyLoaded}, account={FundAccountLoaded}, positions={PositionsLoaded}, drafts={JournalDrafts}, reportPack={ReportPackLoaded}, marketPrints={MarketPrints}).",
            _demoRoot,
            provisioning.ReconciliationBreaksSeeded,
            provisioning.ReconciliationLoaded,
            provisioning.StrategyRunLoaded,
            provisioning.FundAccountLoaded,
            provisioning.PortfolioPositionsLoaded,
            provisioning.JournalDraftsSeeded,
            provisioning.ReportPackLoaded,
            marketHistoryPrints);

        return new DemoWorkspaceSeedReport(
            _demoRoot,
            DemoTenantBlueprint.SeededProvenanceLabel,
            provisioning,
            marketHistoryPrints);
    }

    /// <summary>
    /// Writes deterministic seeded market history as durable trade-event JSONL in the same layout
    /// the live ingestion path produces, so the Data desk reads it back through the ordinary
    /// <c>HistoricalDataQueryService</c> discovery rules rather than a demo-only convention.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Paths come from <see cref="JsonlStoragePolicy"/> and lines from
    /// <see cref="HighPerformanceJson"/> — the very policy and serializer
    /// <c>JsonlStorageSink</c> uses — so the seeded files are indistinguishable in shape from a
    /// real capture: <c>{demoRoot}/{SYMBOL}/Trade/{yyyy-MM-dd}.jsonl</c>. That matters beyond
    /// tidiness. The reader discovers a symbol's files either under <c>{root}/{SYMBOL}</c> or by
    /// matching the symbol against a <em>file name</em>, and lists available symbols only from
    /// top-level directories of the data root, so history filed under a demo-only
    /// <c>{root}/historical/{SYMBOL}/</c> path with a symbol-free file name is invisible to every
    /// Data desk surface.
    /// </para>
    /// <para>
    /// Events carry <see cref="MarketDataSources.Sample"/> as their source, which is what bar
    /// aggregation reports as the series' provenance — the seeded history names itself as sample
    /// data wherever the desk attributes a source (W9-TRUTH-001).
    /// </para>
    /// </remarks>
    private async Task<int> SeedMarketHistoryAsync(CancellationToken ct)
    {
        var lastSession = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var policy = new JsonlStoragePolicy(new StorageOptions { RootPath = _demoRoot });
        var written = new List<string>();
        var totalPrints = 0;

        foreach (var symbol in DemoTenantBlueprint.MarketHistorySymbolList)
        {
            ct.ThrowIfCancellationRequested();
            var normalized = symbol.ToUpperInvariant();
            var sequence = 0L;

            foreach (var session in DemoTenantBlueprint
                         .BuildMarketHistory(normalized, lastSession)
                         .GroupBy(print => DateOnly.FromDateTime(print.Timestamp.UtcDateTime)))
            {
                ct.ThrowIfCancellationRequested();

                var builder = new StringBuilder();
                string? path = null;
                foreach (var print in session)
                {
                    var tradeEvent = BuildSeededTradeEvent(normalized, print, ++sequence);

                    // One path per session file: every event in the group shares a date and symbol,
                    // so the policy resolves them all to the same daily partition.
                    path ??= policy.GetPath(tradeEvent);
                    builder.Append(HighPerformanceJson.Serialize(tradeEvent)).Append('\n');
                    totalPrints++;
                }

                if (path is null)
                {
                    continue;
                }

                await AtomicFileWriter.WriteAsync(path, builder.ToString(), ct).ConfigureAwait(false);
                written.Add(Path.GetRelativePath(_demoRoot, path));
            }
        }

        await PruneSupersededMarketHistoryAsync(written, ct).ConfigureAwait(false);
        RemoveLegacyMarketHistory();
        return totalPrints;
    }

    /// <summary>
    /// Builds one seeded trade event. The payload is a real <see cref="Trade"/> rather than a
    /// hand-shaped JSON object, so the line the serializer emits carries every field a stored
    /// provider event carries.
    /// </summary>
    private static MarketEvent BuildSeededTradeEvent(
        string symbol,
        DemoTenantBlueprint.SampleTradePrint print,
        long sequence)
    {
        var trade = new Trade(
            Timestamp: print.Timestamp,
            Symbol: symbol,
            Price: print.Price,
            Size: print.Size,
            Aggressor: AggressorSide.Unknown,
            SequenceNumber: sequence,
            StreamId: DemoTenantBlueprint.MarketHistorySource,
            Venue: DemoTenantBlueprint.MarketHistoryVenue);

        return MarketEvent.Trade(print.Timestamp, symbol, trade, DemoTenantBlueprint.MarketHistorySource)
            with { ReceivedAtUtc = print.Timestamp };
    }

    /// <summary>
    /// Deletes seeded session files an earlier seed wrote that this run no longer produces, so the
    /// seeded window stays exactly the documented session count instead of growing each time the
    /// demo is re-seeded on a later date.
    /// </summary>
    /// <remarks>
    /// Pruning is driven by the manifest this method rewrites, never by scanning the symbol
    /// directories: only a file a previous seed recorded as its own is ever removed, so market data
    /// an operator collected into the demo workspace is left alone.
    /// </remarks>
    private async Task PruneSupersededMarketHistoryAsync(IReadOnlyList<string> written, CancellationToken ct)
    {
        var manifestPath = Path.Combine(_demoRoot, MarketHistoryManifestFileName);
        var current = new HashSet<string>(written, StringComparer.Ordinal);

        foreach (var stale in await ReadMarketHistoryManifestAsync(manifestPath, ct).ConfigureAwait(false))
        {
            if (current.Contains(stale))
            {
                continue;
            }

            var absolute = Path.GetFullPath(Path.Combine(_demoRoot, stale));

            // A manifest is data on disk; re-check containment before deleting anything it names.
            if (!absolute.StartsWith(_demoRoot + Path.DirectorySeparatorChar, PathComparison))
            {
                _logger.LogWarning(
                    "Ignoring demo market-history manifest entry {Entry} that resolves outside the demo root.",
                    stale);
                continue;
            }

            try
            {
                File.Delete(absolute);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Could not remove superseded seeded market history {Path}.", absolute);
            }
        }

        var files = new JsonArray();
        foreach (var entry in written.Order(StringComparer.Ordinal))
        {
            files.Add(entry);
        }

        var manifest = new JsonObject
        {
            ["schema"] = MarketHistoryManifestSchema,
            ["files"] = files,
        };

        await AtomicFileWriter.WriteAsync(manifestPath, manifest.ToJsonString(), ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<string>> ReadMarketHistoryManifestAsync(string manifestPath, CancellationToken ct)
    {
        if (!File.Exists(manifestPath))
        {
            return [];
        }

        try
        {
            var content = await File.ReadAllTextAsync(manifestPath, ct).ConfigureAwait(false);
            if (JsonNode.Parse(content) is not JsonObject manifest || manifest["files"] is not JsonArray files)
            {
                return [];
            }

            var entries = new List<string>(files.Count);
            foreach (var node in files)
            {
                if (node is JsonValue value
                    && value.TryGetValue<string>(out var relative)
                    && !string.IsNullOrWhiteSpace(relative))
                {
                    entries.Add(relative);
                }
            }

            return entries;
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
        {
            // An unreadable manifest costs a prune, not a seed: the current run still writes its
            // own files and replaces the manifest with a readable one.
            _logger.LogWarning(ex, "Could not read the demo market-history manifest {Path}.", manifestPath);
            return [];
        }
    }

    /// <summary>
    /// Removes market history left by seeds that wrote the demo-only
    /// <c>{demoRoot}/historical/{SYMBOL}/</c> layout, so an already-seeded workspace converges on
    /// the readable layout instead of keeping an unreadable copy beside it.
    /// </summary>
    private void RemoveLegacyMarketHistory()
    {
        var legacyRoot = Path.Combine(_demoRoot, LegacyMarketHistoryFolderName);
        if (!Directory.Exists(legacyRoot))
        {
            return;
        }

        try
        {
            // Keyed off the legacy layout rather than the current symbol list: a workspace seeded
            // when the blueprint named a different set would otherwise keep that symbol's
            // unreadable file forever, and the legacy root would never empty out. Only the fixed
            // file name the old seeder wrote is ever removed, one level under the legacy root.
            foreach (var directory in Directory.EnumerateDirectories(legacyRoot))
            {
                var file = Path.Combine(directory, LegacyMarketHistoryFileName);
                if (File.Exists(file))
                {
                    File.Delete(file);
                }

                if (!Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory);
                }
            }

            if (!Directory.EnumerateFileSystemEntries(legacyRoot).Any())
            {
                Directory.Delete(legacyRoot);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not remove the legacy seeded market history under {Path}.", legacyRoot);
        }
    }

    /// <summary>
    /// Tears down the demo workspace, guarded so only the dedicated, sentinel-marked demo root can be
    /// deleted. A missing demo root is a no-op, not an error.
    /// </summary>
    public Task<DemoWorkspaceResetReport> ResetAsync(CancellationToken ct = default)
    {
        // Structural + sentinel guard: throws DemoWorkspaceIsolationException for any non-demo root.
        var validated = DemoWorkspaceLayout.EnsureSafeToDelete(_demoRoot, _baseDataRoot);

        if (!Directory.Exists(validated))
        {
            _logger.LogInformation("Demo workspace {DemoRoot} does not exist; nothing to reset.", validated);
            return Task.FromResult(new DemoWorkspaceResetReport(validated, Deleted: false));
        }

        ct.ThrowIfCancellationRequested();
        Directory.Delete(validated, recursive: true);
        _logger.LogInformation("Reset (deleted) demo workspace {DemoRoot}.", validated);
        return Task.FromResult(new DemoWorkspaceResetReport(validated, Deleted: true));
    }

    private async Task WriteSentinelAsync(CancellationToken ct)
    {
        var sentinel = new JsonObject
        {
            ["schema"] = "meridian.demo-workspace/v1",
            ["provenance"] = DemoTenantBlueprint.SeededProvenanceLabel,
            ["sourceType"] = DemoTenantBlueprint.SeededSourceType,
            ["sourceSystem"] = DemoTenantBlueprint.SeededSourceSystem,
            ["portfolio"] = DemoTenantBlueprint.PortfolioName,
            ["createdUtc"] = DateTimeOffset.UtcNow.ToString("O"),
        };

        var path = DemoWorkspaceLayout.ResolveSentinelPath(_demoRoot);
        await File.WriteAllTextAsync(path, sentinel.ToJsonString(), ct).ConfigureAwait(false);
    }
}

/// <summary>Outcome of seeding the demo workspace.</summary>
public sealed record DemoWorkspaceSeedReport(
    string DemoRoot,
    string Provenance,
    DemoTenantProvisioningReport Provisioning,
    int MarketHistoryTradePrintsSeeded = 0);

/// <summary>Outcome of tearing down the demo workspace.</summary>
public sealed record DemoWorkspaceResetReport(string DemoRoot, bool Deleted);
