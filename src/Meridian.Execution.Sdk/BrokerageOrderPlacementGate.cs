namespace Meridian.Execution.Sdk;

/// <summary>
/// Central broker order-placement gate shared by HTTP endpoints and the OMS.
/// </summary>
public static class BrokerageOrderPlacementGate
{
    public static BrokerageOrderPlacementGateDecision Evaluate(BrokerageConfiguration? configuration)
    {
        if (configuration is null)
        {
            return BrokerageOrderPlacementGateDecision.Allowed();
        }

        var gatewayId = string.IsNullOrWhiteSpace(configuration.Gateway)
            ? "paper"
            : configuration.Gateway.Trim().ToLowerInvariant();

        if (!configuration.BrokerFlows.TryGetValue(gatewayId, out var flow))
        {
            flow = new BrokerFlowFlags();
        }

        if (string.Equals(gatewayId, "paper", StringComparison.Ordinal))
        {
            return flow.PaperOrderFlowEnabled
                ? BrokerageOrderPlacementGateDecision.Allowed()
                : BrokerageOrderPlacementGateDecision.Rejected("Paper order flow is disabled for broker 'paper'.");
        }

        if (!flow.ProductionOrderRoutingEnabled)
        {
            return BrokerageOrderPlacementGateDecision.Rejected(
                $"Production order routing is disabled for broker '{gatewayId}'.");
        }

        if (!IsLiveProductionRouting(configuration))
        {
            return BrokerageOrderPlacementGateDecision.Rejected(
                $"Live execution must be explicitly enabled before broker '{gatewayId}' can route orders.");
        }

        if (!configuration.ReadOnlyPhaseEnabled)
        {
            return BrokerageOrderPlacementGateDecision.Rejected(
                "Order routing is blocked because the read-only phase is disabled.");
        }

        if (!configuration.PaperTradingPhaseEnabled)
        {
            return BrokerageOrderPlacementGateDecision.Rejected(
                "Order routing is blocked because the paper-trading phase is disabled.");
        }

        if (!configuration.ProductionRoutingPhaseEnabled)
        {
            return BrokerageOrderPlacementGateDecision.Rejected(
                "Order routing is blocked because production routing is disabled.");
        }

        if (!configuration.ReadOnlyVerificationPassed)
        {
            return BrokerageOrderPlacementGateDecision.Rejected(
                "Production routing gate failed: read-only verification must pass.");
        }

        if (!configuration.PaperLifecycleTestsPassed)
        {
            return BrokerageOrderPlacementGateDecision.Rejected(
                "Production routing gate failed: paper-trading lifecycle tests must pass.");
        }

        if (!configuration.ReplayEvidencePassed)
        {
            return BrokerageOrderPlacementGateDecision.Rejected(
                "Production routing gate failed: replay evidence must pass.");
        }

        if (configuration.ValidationGates.RequireValidationArtifactsForOrderPlacement)
        {
            if (!File.Exists(configuration.ValidationGates.ValidationArtifactPath))
            {
                return BrokerageOrderPlacementGateDecision.Rejected(
                    $"Order placement is gated until validation artifact is present: {configuration.ValidationGates.ValidationArtifactPath}.");
            }

            if (!File.Exists(configuration.ValidationGates.SignoffArtifactPath))
            {
                return BrokerageOrderPlacementGateDecision.Rejected(
                    $"Order placement is gated until signoff artifact is present: {configuration.ValidationGates.SignoffArtifactPath}.");
            }
        }

        return BrokerageOrderPlacementGateDecision.Allowed();
    }

    private static bool IsLiveProductionRouting(BrokerageConfiguration configuration) =>
        configuration.LiveExecutionEnabled &&
        !string.IsNullOrWhiteSpace(configuration.Gateway) &&
        !string.Equals(configuration.Gateway, "paper", StringComparison.OrdinalIgnoreCase);
}

public sealed record BrokerageOrderPlacementGateDecision(bool IsAllowed, string? RejectReason)
{
    public static BrokerageOrderPlacementGateDecision Allowed() => new(true, null);

    public static BrokerageOrderPlacementGateDecision Rejected(string reason) => new(false, reason);
}
