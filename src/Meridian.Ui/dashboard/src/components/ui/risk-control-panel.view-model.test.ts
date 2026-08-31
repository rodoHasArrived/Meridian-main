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
    expect(vm.fatFingerQuantityField).toBeDefined();
    expect(vm.fatFingerDeviationField).toBeDefined();
    expect(vm.saveFatFingerAction).toBeDefined();
    expect(vm.priceCollarField).toBeDefined();
    expect(vm.savePriceCollarAction).toBeDefined();
  });

  it("projects drawdown save disabled reasons and field semantics from command state", () => {
    const vm = buildRiskControlPanelViewModel([], {
      loading: false,
      saving: false,
      loadFailed: false,
      drawdownPercent: "",
      submitted: true,
      fatFingerQuantity: "",
      fatFingerDeviation: "",
      submittedFatFinger: false,
      priceCollar: "",
      submittedPriceCollar: false,
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
    expect(vm.fatFingerQuantityField).toBeDefined();
    expect(vm.priceCollarField).toBeDefined();
  });

  it("projects loading, refresh, and successful save announcement state", () => {
    const vm = buildRiskControlPanelViewModel([], {
      loading: true,
      saving: false,
      loadFailed: false,
      drawdownPercent: "5",
      submitted: false,
      fatFingerQuantity: "",
      fatFingerDeviation: "",
      submittedFatFinger: false,
      priceCollar: "",
      submittedPriceCollar: false,
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
    expect(vm.fatFingerQuantityField).toBeDefined();
    expect(vm.priceCollarField).toBeDefined();
  });

  it("projects fat-finger and price collar field errors when submitted with empty values", () => {
    const vm = buildRiskControlPanelViewModel([], {
      loading: false,
      saving: false,
      loadFailed: false,
      drawdownPercent: "5",
      submitted: false,
      fatFingerQuantity: "",
      fatFingerDeviation: "",
      submittedFatFinger: true,
      priceCollar: "",
      submittedPriceCollar: true,
      statusMessage: null,
      statusTone: "default"
    });

    expect(vm.fatFingerQuantityField).toMatchObject({
      id: "risk-fat-finger-quantity",
      label: "Maximum order quantity",
      error: true,
      helpText: "Maximum order quantity is required before saving."
    });
    expect(vm.fatFingerQuantityField.describedBy).toBe("risk-fat-finger-quantity-help risk-control-status");
    expect(vm.fatFingerDeviationField).toMatchObject({
      id: "risk-fat-finger-deviation",
      label: "Maximum price deviation percent",
      error: true,
      helpText: "Price deviation percent is required before saving."
    });
    expect(vm.saveFatFingerAction).toMatchObject({
      disabled: true,
      disabledReason: "Enter a maximum order quantity before saving.",
      ariaLabel: "Save fat-finger limits unavailable: Enter a maximum order quantity before saving."
    });
    expect(vm.priceCollarField).toMatchObject({
      id: "risk-price-collar",
      label: "Price collar percent",
      error: true,
      helpText: "Price collar percent is required before saving."
    });
    expect(vm.priceCollarField.describedBy).toBe("risk-price-collar-help risk-control-status");
    expect(vm.savePriceCollarAction).toMatchObject({
      disabled: true,
      disabledReason: "Enter a price collar percent before saving.",
      ariaLabel: "Save price collar unavailable: Enter a price collar percent before saving."
    });
  });

  it("validates fat-finger quantity as a positive integer and deviation as < 100", () => {
    const vm = buildRiskControlPanelViewModel([], {
      loading: false,
      saving: false,
      loadFailed: false,
      drawdownPercent: "5",
      submitted: false,
      fatFingerQuantity: "1.5",
      fatFingerDeviation: "150",
      submittedFatFinger: true,
      priceCollar: "150",
      submittedPriceCollar: true,
      statusMessage: null,
      statusTone: "default"
    });

    expect(vm.fatFingerQuantityField).toMatchObject({
      error: true,
      helpText: "Enter a positive whole number, for example 1000."
    });
    expect(vm.fatFingerDeviationField).toMatchObject({
      error: true,
      helpText: "Enter a positive number less than 100, for example 5."
    });
    expect(vm.saveFatFingerAction).toMatchObject({
      disabled: true,
      disabledReason: "Enter a positive whole number for maximum order quantity."
    });
    expect(vm.priceCollarField).toMatchObject({
      error: true,
      helpText: "Enter a positive number less than 100, for example 3."
    });
    expect(vm.savePriceCollarAction).toMatchObject({
      disabled: true,
      disabledReason: "Enter a positive number less than 100 for the price collar."
    });
  });

  it("enables fat-finger and price collar save actions with valid values", () => {
    const vm = buildRiskControlPanelViewModel([], {
      loading: false,
      saving: false,
      loadFailed: false,
      drawdownPercent: "5",
      submitted: false,
      fatFingerQuantity: "1000",
      fatFingerDeviation: "5",
      submittedFatFinger: true,
      priceCollar: "3",
      submittedPriceCollar: true,
      statusMessage: null,
      statusTone: "default"
    });

    expect(vm.fatFingerQuantityField.error).toBe(false);
    expect(vm.fatFingerDeviationField.error).toBe(false);
    expect(vm.saveFatFingerAction.disabled).toBe(false);
    expect(vm.saveFatFingerAction.ariaLabel).toBe("Save fat-finger limits");
    expect(vm.priceCollarField.error).toBe(false);
    expect(vm.savePriceCollarAction.disabled).toBe(false);
    expect(vm.savePriceCollarAction.ariaLabel).toBe("Save price collar");
  });
});
