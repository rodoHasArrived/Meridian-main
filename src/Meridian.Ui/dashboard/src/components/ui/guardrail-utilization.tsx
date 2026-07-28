import { Progress } from "@/components/ui/progress";
import { cn } from "@/lib/utils";
import type { RiskGuardrail } from "@/types";

// Status job: guardrail state drives the meter fill; state text and the percent value
// always accompany the color so identity is never color-alone.
const guardrailBarTone: Record<RiskGuardrail["state"], string> = {
  Healthy: "bg-success",
  Observe: "bg-warning",
  Constrained: "bg-danger"
};

const guardrailStateTextTone: Record<RiskGuardrail["state"], string> = {
  Healthy: "text-success",
  Observe: "text-warning",
  Constrained: "text-danger"
};

const guardrailRuleLabel: Record<string, string> = {
  PositionLimit: "Position limit",
  DrawdownCircuitBreaker: "Drawdown circuit breaker",
  OrderRateThrottle: "Order rate throttle",
  GrossExposure: "Gross exposure ceiling",
  SymbolConcentration: "Single-name concentration",
  OrderNotional: "Per-order notional"
};

const guardrailSeverityLabel: Record<RiskGuardrail["severity"], string> = {
  Info: "flags",
  Warning: "flags",
  Error: "rejects",
  Escalate: "parks for approval",
  Critical: "trips breaker"
};

/**
 * Body of the "Active guardrails" panel: utilization meters generated from the live
 * risk-rule registry, falling back to the flat string list for older payloads, and an
 * honest empty state when no registry telemetry is available.
 */
export function GuardrailPanelBody({
  guardrails,
  activeGuardrails
}: {
  guardrails: RiskGuardrail[] | null | undefined;
  activeGuardrails: string[];
}) {
  if (guardrails?.length) {
    return <GuardrailUtilizationList guardrails={guardrails} />;
  }
  if (activeGuardrails.length) {
    return (
      <ul className="list-disc space-y-1 pl-6 text-sm text-foreground">
        {activeGuardrails.map((guardrail) => (
          <li key={guardrail}>{guardrail}</li>
        ))}
      </ul>
    );
  }
  return (
    <p className="text-sm text-muted-foreground">
      Risk rule registry is unavailable; no live guardrail telemetry.
    </p>
  );
}

/**
 * Utilization meters for the live risk-rule registry: one row per guardrail with the
 * rule label, its enforced severity outcome, current/threshold values, and a
 * state-toned bar showing how much of the configured headroom is consumed.
 */
export function GuardrailUtilizationList({ guardrails }: { guardrails: RiskGuardrail[] }) {
  return (
    <ul className="space-y-3" aria-label="Guardrail utilization">
      {guardrails.map((guardrail) => (
        <li key={guardrail.ruleName} className="text-sm">
          <div className="flex flex-wrap items-baseline justify-between gap-x-3 gap-y-0.5">
            <span className="font-medium text-foreground">
              {guardrailRuleLabel[guardrail.ruleName] ?? guardrail.ruleName}
              <span className="ml-2 text-xs font-normal text-muted-foreground">
                {guardrailSeverityLabel[guardrail.severity] ?? guardrail.severity}
              </span>
            </span>
            <span className="text-xs text-muted-foreground">
              {guardrail.currentValue}
              {" / "}
              {guardrail.threshold}
              {" · "}
              <span className={cn("font-medium", guardrailStateTextTone[guardrail.state])}>
                {guardrail.utilizationPercent != null
                  ? `${guardrail.utilizationPercent.toFixed(0)}% · ${guardrail.state}`
                  : guardrail.state}
              </span>
            </span>
          </div>
          {guardrail.utilizationPercent != null ? (
            <Progress
              className="mt-1.5"
              value={Math.min(100, guardrail.utilizationPercent)}
              aria-label={`${guardrailRuleLabel[guardrail.ruleName] ?? guardrail.ruleName} utilization`}
              indicatorClassName={guardrailBarTone[guardrail.state]}
            />
          ) : null}
        </li>
      ))}
    </ul>
  );
}
