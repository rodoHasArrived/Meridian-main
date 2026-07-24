using System.Text.Json.Nodes;
using Meridian.Contracts.Configuration;
using Meridian.Storage.Operations;
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
/// hash-chained strategy case history), so a host started with the demo root active renders the
/// seeded data end-to-end. Seeding is idempotent: re-running never duplicates casework or runs.
/// </para>
/// <para>
/// All seeded records carry the Blueprint-1 <see cref="DemoTenantBlueprint.SeededProvenanceLabel"/>
/// provenance, and teardown is guarded by <see cref="DemoWorkspaceLayout.EnsureSafeToDelete"/> so
/// <c>--reset-demo</c> can only ever remove the dedicated demo root — never a non-demo data root.
/// </para>
/// </remarks>
public sealed class DemoWorkspaceSeeder
{
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

        var breaks = new FileReconciliationBreakQueueRepository(
            Path.Combine(_demoRoot, "workstation"),
            _loggerFactory.CreateLogger<FileReconciliationBreakQueueRepository>());

        // FileOperationalCaseHistoryStore appends "operations/case-history.jsonl" under the root it is
        // given, matching the serving host's DI (FileOperationalCaseHistoryStore(dataRoot)), so the
        // seeded, hash-chained strategy run is read back by the running workstation.
        var strategyRuns = new StrategyRunStore(new FileOperationalCaseHistoryStore(_demoRoot));

        var provisioner = new DemoTenantProvisioner(
            breaks,
            strategyRuns,
            _loggerFactory.CreateLogger<DemoTenantProvisioner>());

        var provisioning = await provisioner.ProvisionAsync(ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Seeded demo workspace at {DemoRoot} (breaks seeded={BreaksSeeded}, reconciliation loaded={ReconciliationLoaded}, strategy loaded={StrategyLoaded}).",
            _demoRoot,
            provisioning.ReconciliationBreaksSeeded,
            provisioning.ReconciliationLoaded,
            provisioning.StrategyRunLoaded);

        return new DemoWorkspaceSeedReport(
            _demoRoot,
            DemoTenantBlueprint.SeededProvenanceLabel,
            provisioning);
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
    DemoTenantProvisioningReport Provisioning);

/// <summary>Outcome of tearing down the demo workspace.</summary>
public sealed record DemoWorkspaceResetReport(string DemoRoot, bool Deleted);
