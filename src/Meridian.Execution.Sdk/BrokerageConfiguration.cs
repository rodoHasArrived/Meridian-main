namespace Meridian.Execution.Sdk;

/// <summary>
/// Configuration for the brokerage execution layer.
/// Determines which gateway is used for live order routing.
/// </summary>
public sealed class BrokerageConfiguration
{
    /// <summary>
    /// Per-broker feature flags that separate read-only data access,
    /// paper-trading order flow, and production order routing.
    /// Keys are normalized broker IDs (e.g., "alpaca", "ib", "robinhood").
    /// </summary>
    public Dictionary<string, BrokerFlowFlags> BrokerFlows { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Validation/signoff artifact requirements for enabling broker order routing.
    /// </summary>
    public BrokerValidationGateOptions ValidationGates { get; set; } = new();
    /// <summary>
    /// The brokerage gateway to use. Must match a registered gateway ID
    /// (e.g., "alpaca", "ib", "paper").
    /// Default is "paper" for safety.
    /// </summary>
    public string Gateway { get; set; } = "paper";

    /// <summary>
    /// Whether live execution is explicitly enabled. Must be set to true
    /// along with a valid gateway to route orders to a real broker.
    /// This is a safety switch to prevent accidental live trading.
    /// </summary>
    public bool LiveExecutionEnabled { get; set; }

    /// <summary>
    /// Maximum position size (in shares) across all symbols. Zero means unlimited.
    /// Applied as a pre-trade risk check.
    /// </summary>
    public decimal MaxPositionSize { get; set; }

    /// <summary>
    /// Maximum notional order value (price * quantity). Zero means unlimited.
    /// Applied as a pre-trade risk check.
    /// </summary>
    public decimal MaxOrderNotional { get; set; }

    /// <summary>
    /// Maximum number of open orders allowed at any time. Zero means unlimited.
    /// </summary>
    public int MaxOpenOrders { get; set; }
}

public sealed class BrokerFlowFlags
{
    public bool ReadOnlyDataEnabled { get; set; } = true;

    public bool PaperOrderFlowEnabled { get; set; }

    public bool ProductionOrderRoutingEnabled { get; set; }
}

public sealed class BrokerValidationGateOptions
{
    public bool RequireValidationArtifactsForOrderPlacement { get; set; } = true;

    public string ValidationArtifactPath { get; set; } = "artifacts/provider-validation/_automation/current/wave1-validation-summary.json";

    public string SignoffArtifactPath { get; set; } = "artifacts/provider-validation/_automation/current/dk1-operator-signoff.json";
}
