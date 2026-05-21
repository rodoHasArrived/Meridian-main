using Meridian.Contracts.StrategyEngine;
using Meridian.Contracts.Workstation;

namespace Meridian.Strategies.Services;

/// <summary>
/// In-process registry for Strategy Engine definitions. Definitions stay explicit and
/// serializable so Strategy Designer, QuantScript, covered-call, and future strategy types
/// can share validation without a reflection-heavy plugin framework.
/// </summary>
public sealed class StrategyEngineRegistry
{
    private readonly StrategyDesignService _strategyDesignService;

    public StrategyEngineRegistry(StrategyDesignService strategyDesignService)
    {
        _strategyDesignService = strategyDesignService ?? throw new ArgumentNullException(nameof(strategyDesignService));
    }

    public IReadOnlyList<StrategyEngineDefinition> GetDefinitions()
    {
        var definitions = new List<StrategyEngineDefinition>
        {
            CreateCoveredCallDefinition()
        };

        definitions.AddRange(_strategyDesignService.GetTemplates().Select(CreateDesignerDefinition));
        return definitions
            .OrderBy(static definition => definition.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public StrategyEngineDefinition? GetDefinition(string strategyId, string? version = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(strategyId);

        return GetDefinitions().FirstOrDefault(definition =>
            string.Equals(definition.StrategyId, strategyId, StringComparison.OrdinalIgnoreCase) &&
            (string.IsNullOrWhiteSpace(version) || string.Equals(definition.Version, version, StringComparison.OrdinalIgnoreCase)));
    }

    private static StrategyEngineDefinition CreateDesignerDefinition(StrategyDesignTemplate template)
    {
        var document = template.Document;
        var fieldRefs = document.Cells
            .SelectMany(static cell => cell.FieldRefs ?? Array.Empty<string>())
            .Where(static field => !string.IsNullOrWhiteSpace(field))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static field => field, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new StrategyEngineDefinition(
            StrategyId: document.DocumentId,
            Version: document.Version,
            Name: document.Name,
            Type: StrategyEngineStrategyType.VisualDesigner,
            Description: document.Description,
            AssetUniverse: document.Universe,
            RequiredDataInputs:
            [
                new(
                    DependencyId: "designer-dataset",
                    Kind: StrategyEngineDataDependencyKind.PriceBars,
                    Description: document.DatasetReference,
                    Granularity: "strategy-designer",
                    MissingDataPolicy: StrategyEngineMissingDataPolicy.Block,
                    RequiredFields: fieldRefs)
            ],
            ParameterSchema:
            [
                new(
                    Name: "datasetFingerprint",
                    Type: StrategyEngineParameterType.String,
                    DefaultValue: null,
                    Description: "Fingerprint of dataset reference, universe, fields, and strategy version.",
                    DisplayLabel: "Dataset fingerprint",
                    IsRequired: false),
                new(
                    Name: "operatorNotes",
                    Type: StrategyEngineParameterType.String,
                    DefaultValue: null,
                    Description: "Operator rationale retained with the run request.",
                    DisplayLabel: "Operator notes",
                    IsRequired: false)
            ],
            SupportedModes:
            [
                StrategyEngineRunMode.Backtest,
                StrategyEngineRunMode.Replay,
                StrategyEngineRunMode.ReviewOnly,
                StrategyEngineRunMode.PaperValidation
            ],
            Owner: "Strategy Designer",
            Source: template.SourcePrototype,
            UiMetadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["route"] = "/workstation/strategy/designer",
                ["category"] = template.Category,
                ["tags"] = string.Join(",", template.Tags)
            });
    }

    private static StrategyEngineDefinition CreateCoveredCallDefinition()
        => new(
            StrategyId: "covered-call",
            Version: "1",
            Name: "Covered Call",
            Type: StrategyEngineStrategyType.CoveredCall,
            Description: "Options overwrite workflow using an underlying, option chain preview, backtest proof, and gated paper-validation handoff.",
            AssetUniverse: ["Equity", "ETF", "Option"],
            RequiredDataInputs:
            [
                new(
                    DependencyId: "underlying-bars",
                    Kind: StrategyEngineDataDependencyKind.PriceBars,
                    Description: "Historical bars for the covered underlying.",
                    AssetClass: "Equity",
                    Granularity: "Daily",
                    MissingDataPolicy: StrategyEngineMissingDataPolicy.Block,
                    RequiredFields: ["open", "high", "low", "close", "volume"]),
                new(
                    DependencyId: "options-chain",
                    Kind: StrategyEngineDataDependencyKind.OptionsChain,
                    Description: "Option chain, greeks, expiration, strike, bid/ask, and assignment context.",
                    AssetClass: "Option",
                    MissingDataPolicy: StrategyEngineMissingDataPolicy.Block,
                    RequiredFields: ["expiration", "strike", "delta", "bid", "ask"]),
                new(
                    DependencyId: "position-lots",
                    Kind: StrategyEngineDataDependencyKind.OpenLots,
                    Description: "Open-lot and position coverage for assignment, cost basis, and accounting evidence.",
                    MissingDataPolicy: StrategyEngineMissingDataPolicy.AllowDegradedWithEvidence)
            ],
            ParameterSchema:
            [
                new("underlying", StrategyEngineParameterType.Symbol, "SPY", "Covered underlying symbol.", "Underlying"),
                new("daysToExpiration", StrategyEngineParameterType.Integer, "30", "Target option expiration window.", "DTE", MinValue: "1", MaxValue: "90"),
                new("targetDelta", StrategyEngineParameterType.Decimal, "0.30", "Target short-call delta.", "Target delta", MinValue: "0.05", MaxValue: "0.80", SweepHint: "0.15,0.30,0.45"),
                new("contracts", StrategyEngineParameterType.Integer, "1", "Contract count to evaluate.", "Contracts", MinValue: "1", MaxValue: "100"),
                new("maxAssignmentRisk", StrategyEngineParameterType.Decimal, "0.25", "Maximum assignment-risk score allowed before promotion.", "Max assignment risk", MinValue: "0", MaxValue: "1")
            ],
            SupportedModes:
            [
                StrategyEngineRunMode.Backtest,
                StrategyEngineRunMode.Replay,
                StrategyEngineRunMode.PaperValidation,
                StrategyEngineRunMode.ReviewOnly
            ],
            Owner: "Meridian Strategy Engine",
            Source: "src/Meridian.Ui.Shared/Services/CoveredCall + src/Meridian.Backtesting.Sdk/Strategies/OptionsOverwrite",
            UiMetadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["route"] = "/workstation/strategy/covered-call",
                ["designerRoute"] = "/workstation/strategy/designer",
                ["readinessRoute"] = "/workstation/trading/readiness",
                ["evidenceRoute"] = "/workstation/reporting/evidence"
            });
}
