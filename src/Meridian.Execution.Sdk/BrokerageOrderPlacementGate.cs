namespace Meridian.Execution.Sdk;

/// <summary>
/// Central broker order-placement gate shared by HTTP endpoints and the OMS.
/// </summary>
public static class BrokerageOrderPlacementGate
{
    public static BrokerageOrderPlacementGateDecision Evaluate(
        BrokerageConfiguration? configuration,
        string? selectedGatewayId = null,
        ExecutionMode? selectedExecutionMode = null)
    {
        var hasSelectedGateway = !string.IsNullOrWhiteSpace(selectedGatewayId);
        var selectedGateway = NormalizeGatewayId(selectedGatewayId);
        var executionMode = selectedExecutionMode ?? ResolveExecutionMode(configuration, selectedGatewayId);

        if (configuration is null)
        {
            return IsSimulatedMode(executionMode)
                ? BrokerageOrderPlacementGateDecision.Allowed()
                : BrokerageOrderPlacementGateDecision.Rejected(
                    $"Brokerage configuration is required before broker '{selectedGateway}' can route orders.");
        }

        var configuredGateway = NormalizeGatewayId(configuration.Gateway);
        var gatewayId = string.IsNullOrWhiteSpace(configuration.Gateway) && hasSelectedGateway
            ? selectedGateway
            : configuredGateway;

        if (hasSelectedGateway && !string.Equals(gatewayId, selectedGateway, StringComparison.Ordinal))
        {
            return BrokerageOrderPlacementGateDecision.Rejected(
                $"Brokerage configuration gateway '{gatewayId}' does not match active broker '{selectedGateway}'.");
        }

        if (!configuration.BrokerFlows.TryGetValue(gatewayId, out var flow))
        {
            flow = new BrokerFlowFlags();
        }

        if (IsSimulatedMode(executionMode))
        {
            return flow.PaperOrderFlowEnabled
                ? BrokerageOrderPlacementGateDecision.Allowed()
                : BrokerageOrderPlacementGateDecision.Rejected($"Paper order flow is disabled for broker '{gatewayId}'.");
        }

        if (!flow.ProductionOrderRoutingEnabled)
        {
            return BrokerageOrderPlacementGateDecision.Rejected(
                $"Production order routing is disabled for broker '{gatewayId}'.");
        }

        if (!IsLiveProductionRouting(configuration, executionMode))
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

    public static ExecutionMode ResolveExecutionMode(
        BrokerageConfiguration? configuration,
        string? selectedGatewayId = null)
    {
        var gatewayId = string.IsNullOrWhiteSpace(selectedGatewayId)
            ? NormalizeGatewayId(configuration?.Gateway)
            : NormalizeGatewayId(selectedGatewayId);

        return IsPaperGateway(gatewayId)
            ? ExecutionMode.Paper
            : ExecutionMode.Live;
    }

    private static string NormalizeGatewayId(string? gatewayId) =>
        string.IsNullOrWhiteSpace(gatewayId)
            ? "paper"
            : gatewayId.Trim().ToLowerInvariant();

    private static bool IsPaperGateway(string gatewayId) =>
        string.Equals(gatewayId, "paper", StringComparison.Ordinal);

    private static bool IsSimulatedMode(ExecutionMode executionMode) =>
        executionMode is ExecutionMode.Paper or ExecutionMode.Simulation;

    private static bool IsLiveProductionRouting(BrokerageConfiguration configuration, ExecutionMode executionMode) =>
        configuration.LiveExecutionEnabled &&
        executionMode is ExecutionMode.Live;
}

public sealed record BrokerageOrderPlacementGateDecision(bool IsAllowed, string? RejectReason)
{
    public static BrokerageOrderPlacementGateDecision Allowed() => new(true, null);

    public static BrokerageOrderPlacementGateDecision Rejected(string reason) => new(false, reason);
}
