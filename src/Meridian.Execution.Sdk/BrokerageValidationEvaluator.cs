namespace Meridian.Execution.Sdk;

/// <summary>
/// Evaluates whether the configured brokerage can support a live-readiness review.
/// </summary>
public static class BrokerageValidationEvaluator
{
    public static BrokerageValidationReport Evaluate(BrokerageConfiguration? configuration)
    {
        if (configuration is null)
        {
            return new BrokerageValidationReport(
                State: BrokerageValidationState.Required,
                GatewayId: string.Empty,
                GatewayDisplayName: "Unconfigured",
                Summary: "Brokerage configuration is not projected into this workflow. Validate the target broker selection, credentials, connectivity, and operator controls before making a live-readiness claim.",
                Findings: []);
        }

        var gatewayId = NormalizeGatewayId(configuration.Gateway);
        var findings = new List<string>();

        if (!configuration.LiveExecutionEnabled)
        {
            findings.Add("Brokerage configuration does not enable live execution.");
        }

        if (string.IsNullOrWhiteSpace(gatewayId))
        {
            findings.Add("No live brokerage gateway is configured.");
        }
        else if (string.Equals(gatewayId, "paper", StringComparison.Ordinal))
        {
            findings.Add("The configured gateway still points to paper trading.");
        }

        var gatewayDisplayName = GetGatewayDisplayName(gatewayId);
        if (findings.Count > 0)
        {
            return new BrokerageValidationReport(
                State: BrokerageValidationState.Gap,
                GatewayId: gatewayId,
                GatewayDisplayName: gatewayDisplayName,
                Summary: BuildGapSummary(gatewayDisplayName, findings),
                Findings: findings);
        }

        var warnings = new List<string>();
        if (!HasExplicitGuardrail(configuration))
        {
            warnings.Add("No explicit MaxPositionSize, MaxOrderNotional, or MaxOpenOrders limits are configured.");
        }

        return new BrokerageValidationReport(
            State: BrokerageValidationState.Required,
            GatewayId: gatewayId,
            GatewayDisplayName: gatewayDisplayName,
            Summary: BuildRequiredSummary(gatewayDisplayName, warnings),
            Findings: warnings);
    }

    private static bool HasExplicitGuardrail(BrokerageConfiguration configuration) =>
        configuration.MaxPositionSize > 0m ||
        configuration.MaxOrderNotional > 0m ||
        configuration.MaxOpenOrders > 0;

    private static string NormalizeGatewayId(string? gateway) =>
        string.IsNullOrWhiteSpace(gateway)
            ? string.Empty
            : gateway.Trim().ToLowerInvariant();

    private static string GetGatewayDisplayName(string gatewayId) =>
        gatewayId switch
        {
            "" => "Unconfigured",
            "paper" => "Paper trading",
            "alpaca" => "Alpaca",
            "robinhood" => "Robinhood",
            "ib" or "interactivebrokers" => "Interactive Brokers",
            _ => gatewayId
        };

    private static string BuildGapSummary(string gatewayDisplayName, IReadOnlyList<string> findings)
    {
        var reason = findings.Count == 1
            ? findings[0]
            : string.Join(" ", findings);

        return gatewayDisplayName == "Unconfigured"
            ? $"{reason} Configure a live broker before treating the lane as live-ready."
            : $"{reason} Resolve the {gatewayDisplayName} broker configuration before treating the lane as live-ready.";
    }

    private static string BuildRequiredSummary(string gatewayDisplayName, IReadOnlyList<string> warnings)
    {
        var summary = $"Brokerage gateway {gatewayDisplayName} is selected for live review. Validate connectivity, credentials, account permissions, and operator controls before making a live-readiness claim.";
        if (warnings.Count == 0)
        {
            return summary;
        }

        return $"{summary} {string.Join(" ", warnings)}";
    }
}

public enum BrokerageValidationState
{
    Required,
    Gap
}

public sealed record BrokerageValidationReport(
    BrokerageValidationState State,
    string GatewayId,
    string GatewayDisplayName,
    string Summary,
    IReadOnlyList<string> Findings)
{
    public bool HasBlockingGap => State == BrokerageValidationState.Gap;
}
