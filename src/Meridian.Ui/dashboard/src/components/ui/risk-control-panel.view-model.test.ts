import { describe, expect, it } from "vitest";
import { buildRiskControlPanelViewModel } from "@/components/ui/risk-control-panel.view-model";
import type { RiskRuleStatus } from "@/types";

describe("buildRiskControlPanelViewModel", () => {
  it("prioritizes constrained state and projects violations", () => {
    const statuses: RiskRuleStatus[] = [
      {
        ruleName: "PositionLimit",
        state: "Healthy",
        summary: "Healthy.",
        isBreached: false,
        threshold: "5000",
        currentValue: "2500",
        asOf: "2026-05-01T00:00:00Z",
        recentViolations: []
      },
      {
        ruleName: "DrawdownCircuitBreaker",
        state: "Constrained",
        summary: "Drawdown breached.",
        isBreached: true,
        threshold: "5.00%",
        currentValue: "-6.10%",
        asOf: "2026-05-01T00:00:00Z",
        recentViolations: ["Drawdown threshold breached at -6.10%."]
      }
    ];

    const vm = buildRiskControlPanelViewModel(statuses);

    expect(vm.overallState).toBe("Constrained");
    expect(vm.overallSummary).toBe("Drawdown breached.");
    expect(vm.rows).toHaveLength(2);
    expect(vm.rows[1]?.tone).toBe("danger");
    expect(vm.violationTimeline).toEqual([
      {
        id: "DrawdownCircuitBreaker-0",
        ruleName: "DrawdownCircuitBreaker",
        message: "Drawdown threshold breached at -6.10%."
      }
    ]);
  });
});
