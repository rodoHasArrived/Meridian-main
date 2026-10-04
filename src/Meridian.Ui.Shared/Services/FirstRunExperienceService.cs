using System.Text.Json;
using Meridian.Contracts.Workstation;
using Meridian.Storage.Archival;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Owns durable first-run, starter-workspace, sample-pack, and activation evidence.
/// UI clients consume this governed state instead of defining onboarding policy locally.
/// </summary>
public sealed class FirstRunExperienceService(
    ConfigStore configStore,
    DemoTenantProvisioner? demoTenantProvisioner = null)
{
    public const string SamplePackVersion = "2026.1";
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private static readonly StarterWorkspaceDto[] StarterKits =
    [
        new("personal-portfolio", "Personal Portfolio Desk", "monitor-investments", "Track holdings, watchlists, and portfolio changes.", "/portfolio"),
        new("fund-operations", "Fund Operations Desk", "operate-fund", "Run daily controls, approvals, and operational evidence.", "/accounting/operations-continuity"),
        new("accounting-reconciliation", "Accounting and Reconciliation Desk", "reconcile-accounts", "Import statements, investigate breaks, and close with evidence.", "/accounting/statement-import"),
        new("strategy-research", "Strategy Research Desk", "research-strategies", "Research, backtest, and paper-trade governed strategies.", "/strategy"),
        new("market-data", "Market Data Desk", "collect-market-data", "Connect, validate, and monitor trusted market data.", "/data")
    ];

    private static readonly (string Key, string Label, string Action, string Route)[] OutcomeCatalog =
    [
        ("workspace-opened", "Open or create a workspace", "Open workspace", "/portfolio"),
        ("data-imported", "Import sample or real data", "Import data", "/accounting/statement-import"),
        ("validation-resolved", "Resolve one validation issue", "Review breaks", "/accounting/reconciliation/match"),
        ("report-run", "Run one report", "Run report", "/reporting/run"),
        ("result-saved", "Save or export one useful result", "Review results", "/reporting/library")
    ];

    public async Task<FirstRunStatusDto> GetAsync(string username, CancellationToken ct = default)
    {
        var state = await ReadAsync(username, ct).ConfigureAwait(false);
        return ToDto(state);
    }

    public async Task<FirstRunStatusDto> CompleteAsync(
        string username,
        CompleteFirstRunRequestDto request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var kit = StarterKits.FirstOrDefault(item => item.Id.Equals(request.StarterKitId, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException("Choose a supported starter workspace.", nameof(request));

        var dataChoice = NormalizeDataChoice(request.DataChoice);
        var useSample = request.UseSampleData || dataChoice == "sample";
        var now = DateTimeOffset.UtcNow;

        // Load the demo tenant into the real, desk-read stores before persisting state so the status
        // reflects what actually loaded. Provisioning is best-effort and must never block the user
        // from completing onboarding.
        DemoTenantProvisioningReport? provisioning = null;
        if (useSample && demoTenantProvisioner is not null)
        {
            provisioning = await demoTenantProvisioner.ProvisionAsync(ct).ConfigureAwait(false);
        }

        var state = new FirstRunState(
            true,
            string.IsNullOrWhiteSpace(request.Goal) ? kit.Goal : request.Goal.Trim(),
            kit.Id,
            dataChoice,
            useSample,
            useSample ? "sample" : "primary",
            new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase)
            {
                ["workspace-opened"] = now,
                ["data-imported"] = useSample ? now : default
            }.Where(pair => pair.Value != default).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase),
            now,
            ReconciliationLoaded: provisioning?.ReconciliationLoaded ?? true,
            StrategyLoaded: provisioning?.StrategyRunLoaded ?? true);

        await WriteAsync(username, state, ct).ConfigureAwait(false);
        if (useSample)
        {
            await ProvisionSamplePackAsync(username, provisioning, ct).ConfigureAwait(false);
        }

        return ToDto(state);
    }

    public async Task<FirstRunStatusDto> CompleteOutcomeAsync(string username, string key, CancellationToken ct = default)
    {
        if (!OutcomeCatalog.Any(item => item.Key.Equals(key, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("Unknown activation outcome.", nameof(key));
        }

        var state = await ReadAsync(username, ct).ConfigureAwait(false);
        var outcomes = new Dictionary<string, DateTimeOffset>(state.CompletedOutcomes, StringComparer.OrdinalIgnoreCase)
        {
            [key.Trim()] = DateTimeOffset.UtcNow
        };
        state = state with { CompletedOutcomes = outcomes };
        await WriteAsync(username, state, ct).ConfigureAwait(false);
        return ToDto(state);
    }

    private FirstRunStatusDto ToDto(FirstRunState state)
    {
        var sample = state.IsSample;
        var workspace = new WorkspaceModeDto(
            state.WorkspaceId,
            sample ? "Meridian Sample Workspace" : "Meridian Workspace",
            sample,
            sample ? "SAMPLE · PAPER" : "LOCAL",
            sample ? "Sample data only. No live trading or production accounting." : "Local workspace data.",
            sample ? SamplePackVersion : string.Empty);
        var outcomes = OutcomeCatalog.Select(item =>
        {
            state.CompletedOutcomes.TryGetValue(item.Key, out var completedAt);
            return new ActivationOutcomeDto(item.Key, item.Label, item.Action, item.Route, completedAt != default, completedAt == default ? null : completedAt);
        }).ToArray();
        var kit = StarterKits.FirstOrDefault(item => item.Id == state.StarterKitId);
        var recommendations = BuildRecommendations(state.DataChoice, kit?.DefaultRoute ?? "/portfolio", sample);
        var sampleWorkspace = sample
            ? DemoTenantBlueprint.BuildSampleWorkspace(state.ReconciliationLoaded ?? true, state.StrategyLoaded ?? true)
            : null;
        return new FirstRunStatusDto(state.IsComplete, state.Goal, state.StarterKitId, state.DataChoice, workspace, StarterKits, outcomes, recommendations, sampleWorkspace);
    }

    /// <summary>
    /// Turns the starting-data choice the user made during setup into the next steps the Ready
    /// screen offers. Setup promises "Meridian will guide the next step" for the upload and
    /// provider choices, so those choices lead the list with the surface that actually completes
    /// them rather than with a desk that is still empty.
    /// </summary>
    private static IReadOnlyList<RecommendedActionDto> BuildRecommendations(string? dataChoice, string deskRoute, bool sample)
    {
        var openDesk = new RecommendedActionDto("Open your desk", deskRoute, "See the workspace configured for your goal.");
        var runReport = new RecommendedActionDto("Run a report", "/reporting/run", "Produce a useful result and keep the evidence.");

        return dataChoice?.Trim().ToLowerInvariant() switch
        {
            "provider" => new[]
            {
                new RecommendedActionDto("Connect a data provider", "/settings/providers", "Add provider credentials so your desk shows your own data. Paper and read-only options come first."),
                openDesk,
                runReport
            },
            "upload" => new[]
            {
                new RecommendedActionDto("Import your statement or holdings file", "/accounting/statement-import", "Upload the file you chose during setup and validate it before anything posts."),
                openDesk,
                runReport
            },
            "skip" => new[]
            {
                openDesk,
                new RecommendedActionDto("Add starting data when you are ready", "/accounting/statement-import", "Import a statement or holdings file, or connect a provider from Settings."),
                runReport
            },
            _ => new[]
            {
                openDesk,
                new RecommendedActionDto(sample ? "Review the sample statement" : "Add starting data", "/accounting/statement-import", "Import and validate a statement or holdings file."),
                runReport
            }
        };
    }

    private async Task<FirstRunState> ReadAsync(string username, CancellationToken ct)
    {
        var path = StatePath(username);
        if (!File.Exists(path))
            return FirstRunState.Empty;
        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<FirstRunState>(stream, _json, ct).ConfigureAwait(false) ?? FirstRunState.Empty;
        }
        catch (JsonException)
        {
            return FirstRunState.Empty;
        }
    }

    private async Task WriteAsync(string username, FirstRunState state, CancellationToken ct)
    {
        await _writeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await AtomicFileWriter.WriteAsync(StatePath(username), JsonSerializer.Serialize(state, _json), ct).ConfigureAwait(false);
        }
        finally { _writeGate.Release(); }
    }

    private Task ProvisionSamplePackAsync(string username, DemoTenantProvisioningReport? provisioning, CancellationToken ct)
    {
        // The manifest now records the exact populated snapshot loaded into the workspace plus what
        // the provisioner wrote into the real desk stores, so the pack reflects live workspace state
        // instead of a disconnected, unread blob.
        var pack = new
        {
            version = SamplePackVersion,
            label = "Sample · Paper",
            generatedAtUtc = DateTimeOffset.UtcNow,
            workspace = DemoTenantBlueprint.BuildSampleWorkspace(
                provisioning?.ReconciliationLoaded ?? true,
                provisioning?.StrategyRunLoaded ?? true),
            provisioning
        };
        return AtomicFileWriter.WriteAsync(SamplePath(username), JsonSerializer.Serialize(pack, _json), ct);
    }

    private string StatePath(string username) => Path.Combine(Root(username), "first-run.json");
    private string SamplePath(string username) => Path.Combine(Root(username), "sample-pack.json");
    private string Root(string username) => Path.Combine(configStore.GetDataRoot(), "workspaces", SafeSegment(username));
    private static string SafeSegment(string value) => string.Concat(value.Trim().ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '-'));
    private static string NormalizeDataChoice(string value) => value?.Trim().ToLowerInvariant() switch
    {
        "upload" => "upload",
        "provider" => "provider",
        "sample" => "sample",
        "skip" => "skip",
        _ => throw new ArgumentException("Choose upload, provider, sample, or skip.", nameof(value))
    };

    private sealed record FirstRunState(
        bool IsComplete,
        string? Goal,
        string? StarterKitId,
        string? DataChoice,
        bool IsSample,
        string WorkspaceId,
        IReadOnlyDictionary<string, DateTimeOffset> CompletedOutcomes,
        DateTimeOffset? CompletedAtUtc,
        // Null on state written before provisioning-outcome tracking existed; treated as loaded.
        bool? ReconciliationLoaded = null,
        bool? StrategyLoaded = null)
    {
        public static FirstRunState Empty { get; } = new(false, null, null, null, false, "primary", new Dictionary<string, DateTimeOffset>(), null);
    }
}
