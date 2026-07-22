using Meridian.Contracts.Workstation;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Deterministic definition of the Meridian sample demo tenant. This is the single source of truth
/// consumed both by <see cref="DemoTenantProvisioner"/> (which writes it into the real, desk-read
/// stores so the workstation is genuinely populated) and by <see cref="FirstRunExperienceService"/>
/// (which surfaces it in onboarding so the "Ready" step shows a populated workspace, not a badge).
/// </summary>
/// <remarks>
/// Every artifact is clearly sample-labelled (identifiers, portfolio, and strategy names) and can
/// never place live trades or post production accounting. Values are fixed constants so the demo is
/// identical on every install and idempotent to re-provision.
/// </remarks>
public static class DemoTenantBlueprint
{
    /// <summary>
    /// Blueprint-1 provenance stamp shared by every durable record the demo seeds. Carrying an
    /// explicit "seeded" source (rather than a generic "sample" tag) lets desk surfaces render the
    /// <see cref="SeededProvenanceLabel"/> and lets teardown and audit reason about demo data.
    /// </summary>
    public const string SeededSourceType = "seeded";

    /// <summary>Human-facing source system stamped on seeded records and rendered on desk surfaces.</summary>
    public const string SeededSourceSystem = "Meridian Seeded Demo";

    /// <summary>The provenance label onboarding renders for the seeded workspace.</summary>
    public const string SeededProvenanceLabel = "Seeded";

    public const string PortfolioName = "Northstar Sample Portfolio";
    public const decimal Cash = 125_000m;

    public const string StrategyId = "sample-covered-call";
    public const string StrategyName = "Sample Covered Call";
    public const string StrategyRunId = "SAMPLE-RUN-COVERED-CALL";

    public const string PortfolioRoute = "/portfolio";
    public const string ReconciliationRoute = "/accounting/reconciliation/match";
    public const string ReportingRunRoute = "/reporting/run";
    public const string StrategyRoute = "/strategy";
    public const string DataRoute = "/data";

    private static readonly SampleHoldingDto[] HoldingDefinitions =
    [
        BuildHolding("SPY", quantity: 120m, averagePrice: 452.10m, lastPrice: 468.32m),
        BuildHolding("MSFT", quantity: 80m, averagePrice: 358.40m, lastPrice: 372.15m),
        BuildHolding("AAPL", quantity: 150m, averagePrice: 168.20m, lastPrice: 175.44m),
        BuildHolding("TLT", quantity: 200m, averagePrice: 96.10m, lastPrice: 92.85m)
    ];

    private static readonly string[] Watchlist = ["AAPL", "NVDA", "TLT", "EURUSD", "SPY"];

    private static readonly string[] MarketHistorySymbols = ["SPY", "MSFT", "TLT"];
    private const int MarketHistorySessions = 90;

    /// <summary>
    /// Sample reconciliation breaks. These are seeded into the reconciliation break queue and shown
    /// in onboarding. Identifiers are prefixed <c>SAMPLE-</c> so they are unmistakably demo casework.
    /// </summary>
    public static IReadOnlyList<SampleBreakDefinition> BreakDefinitions { get; } =
    [
        new SampleBreakDefinition(
            "SAMPLE-BRK-001",
            "Cash variance",
            ReconciliationBreakCategory.CashMismatch,
            ReconciliationBreakSeverity.High,
            1_250.00m,
            "Custodian cash balance is $1,250.00 above the internal ledger for the sample account."),
        new SampleBreakDefinition(
            "SAMPLE-BRK-002",
            "Unmatched fee",
            ReconciliationBreakCategory.AmountMismatch,
            ReconciliationBreakSeverity.Medium,
            42.50m,
            "A $42.50 custodian fee has no matching ledger entry in the sample statement.")
    ];

    public static IReadOnlyList<SampleHoldingDto> Holdings => HoldingDefinitions;

    public static decimal PortfolioValue => Cash + HoldingDefinitions.Sum(static holding => holding.MarketValue);

    public static decimal UnrealizedPnl => HoldingDefinitions.Sum(static holding => holding.UnrealizedPnl);

    /// <summary>
    /// Builds the populated snapshot surfaced to onboarding clients. Desk highlights (with links) are
    /// included only for surfaces that actually loaded, so a click-through never contradicts what
    /// onboarding claimed; the illustrative sample portfolio is always shown as a preview.
    /// </summary>
    public static SampleWorkspaceDto BuildSampleWorkspace(bool reconciliationLoaded = true, bool strategyLoaded = true)
    {
        var breaks = BreakDefinitions
            .Select(static definition => new SampleBreakDto(
                definition.Id,
                definition.Title,
                definition.Category.ToString(),
                definition.Severity.ToString(),
                definition.Variance,
                definition.Summary,
                ReconciliationRoute))
            .ToArray();

        var report = new SampleArtifactDto(
            "Sample portfolio review",
            "Template ready to run",
            "A governed report template you can run against the sample data.",
            ReportingRunRoute);

        var strategy = new SampleArtifactDto(
            StrategyName,
            "Paper · Completed",
            "A completed paper covered-call run loaded into the Strategy desk — no live orders, safe to inspect.",
            StrategyRoute);

        var marketHistory = new SampleMarketHistoryDto(MarketHistorySymbols, MarketHistorySessions, DataRoute);

        // Highlights advertise only surfaces that are genuinely written into their desk stores, so
        // a click-through never contradicts what onboarding claimed. The sample portfolio, watchlist,
        // report, and market history are shown as an illustrative preview (see the DTO fields below),
        // not as loaded live-desk state — the live Portfolio desk reads in-memory execution state that
        // this offline sample flow cannot durably seed.
        var highlights = new List<SampleHighlightDto>();
        var loadedParts = new List<string>();
        if (reconciliationLoaded)
        {
            highlights.Add(new SampleHighlightDto(
                "Reconciliation",
                $"{breaks.Length} breaks",
                "Real open casework loaded into the reconciliation control tower.",
                ReconciliationRoute));
            loadedParts.Add("sample reconciliation casework");
        }

        if (strategyLoaded)
        {
            highlights.Add(new SampleHighlightDto(
                "Strategy",
                "1 paper run",
                "A completed paper covered-call run loaded into the Strategy desk.",
                StrategyRoute));
            loadedParts.Add("a completed paper strategy run");
        }

        var loadedClause = loadedParts.Count == 0
            ? string.Empty
            : $"{Capitalize(string.Join(" and ", loadedParts))} {(loadedParts.Count == 1 ? "is" : "are")} loaded into your desks now. ";

        return new SampleWorkspaceDto(
            Headline: "Your sample workspace is ready",
            Summary: loadedClause + "The sample portfolio below is illustrative — your live Portfolio desk starts from a clean paper account until you connect data or run a strategy.",
            PortfolioName: PortfolioName,
            PortfolioValue: PortfolioValue,
            Cash: Cash,
            UnrealizedPnl: UnrealizedPnl,
            Holdings: HoldingDefinitions,
            Watchlist: Watchlist,
            ReconciliationBreaks: breaks,
            Report: report,
            Strategy: strategy,
            MarketHistory: marketHistory,
            Highlights: highlights,
            Provenance: SeededProvenanceLabel);
    }

    private static string Capitalize(string value) =>
        string.IsNullOrEmpty(value) ? value : char.ToUpperInvariant(value[0]) + value[1..];

    private static SampleHoldingDto BuildHolding(string symbol, decimal quantity, decimal averagePrice, decimal lastPrice)
    {
        var marketValue = decimal.Round(quantity * lastPrice, 2);
        var unrealizedPnl = decimal.Round(quantity * (lastPrice - averagePrice), 2);
        return new SampleHoldingDto(symbol, quantity, averagePrice, lastPrice, marketValue, unrealizedPnl);
    }

    /// <summary>Fixed definition of a sample reconciliation break, before it is stamped with timestamps.</summary>
    public sealed record SampleBreakDefinition(
        string Id,
        string Title,
        ReconciliationBreakCategory Category,
        ReconciliationBreakSeverity Severity,
        decimal Variance,
        string Summary);
}
