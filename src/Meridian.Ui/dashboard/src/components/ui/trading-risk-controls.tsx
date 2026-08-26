import { GovernedApprovalsPanel } from "@/components/ui/governed-approvals-panel";
import { RiskControlPanel } from "@/components/ui/risk-control-panel";
import { ExecutionAuditTrailPanel } from "@/screens/trading-screen.audit-trail";
import type { GovernedApprovalsViewModel } from "@/screens/trading-screen.governed-approvals";

export interface TradingRiskControlsProps {
  governedApprovals: GovernedApprovalsViewModel;
}

/**
 * The operator's risk controls on the trading screen: the live guardrail configuration,
 * the governed-approval queue that resolves what those guardrails parked, and the audit
 * trail that records how each one was resolved. They belong together — a rail that can
 * park an order is only usable alongside the surface that releases or denies it, and
 * neither is reviewable without the record of what was decided.
 */
export function TradingRiskControls({ governedApprovals }: TradingRiskControlsProps) {
  return (
    <div className="mt-3 space-y-3">
      <RiskControlPanel />
      <GovernedApprovalsPanel model={governedApprovals} />
      <ExecutionAuditTrailPanel />
    </div>
  );
}
