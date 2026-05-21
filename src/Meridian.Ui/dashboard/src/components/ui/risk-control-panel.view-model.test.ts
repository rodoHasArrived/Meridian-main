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

  it("projects drawdown save disabled reasons and field semantics from command state", () => {
    const vm = buildRiskControlPanelViewModel([], {
      loading: false,
      saving: false,
      loadFailed: false,
      drawdownPercent: "",
      submitted: true,
      statusMessage: null,
      statusTone: "default"
    });

    expect(vm.drawdownField).toMatchObject({
      id: "risk-drawdown-threshold",
      label: "Drawdown threshold percent",
      error: true,
      helpText: "Drawdown threshold is required before saving risk policy."
    });
    expect(vm.drawdownField.describedBy).toBe("risk-drawdown-threshold-help risk-control-status");
    expect(vm.saveAction).toMatchObject({
      disabled: true,
      disabledReason: "Enter a drawdown threshold before saving.",
      ariaLabel: "Save drawdown threshold unavailable: Enter a drawdown threshold before saving."
    });
  });

  it("projects loading, refresh, and successful save announcement state", () => {
    const vm = buildRiskControlPanelViewModel([], {
      loading: true,
      saving: false,
      loadFailed: false,
      drawdownPercent: "5",
      submitted: false,
      statusMessage: "Drawdown threshold saved.",
      statusTone: "success"
    });

    expect(vm.loading).toBe(true);
    expect(vm.panelAriaLabel).toBe("Trading risk controls");
    expect(vm.panelAriaBusy).toBe(true);
    expect(vm.emptyRowsText).toBe("Loading risk rules...");
    expect(vm.refreshAction).toMatchObject({
      label: "Refreshing",
      busy: true,
      disabledReason: "Risk controls are already refreshing."
    });
    expect(vm.statusAnnouncement).toBe("Loading risk controls.");
  });
});
