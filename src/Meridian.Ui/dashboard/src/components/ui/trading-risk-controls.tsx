import { GovernedApprovalsPanel } from "@/components/ui/governed-approvals-panel";
import { RiskControlPanel } from "@/components/ui/risk-control-panel";
import type { GovernedApprovalsViewModel } from "@/screens/trading-screen.governed-approvals";

export interface TradingRiskControlsProps {
  governedApprovals: GovernedApprovalsViewModel;
}

/**
 * The operator's risk controls on the trading screen: the live guardrail configuration and
 * the governed-approval queue that resolves what those guardrails parked. They belong
 * together — a rail that can park an order is only usable alongside the surface that
 * releases or denies it.
 */
export function TradingRiskControls({ governedApprovals }: TradingRiskControlsProps) {
  return (
    <div className="mt-3 space-y-3">
      <RiskControlPanel />
      <GovernedApprovalsPanel model={governedApprovals} />
    </div>
  );
}
