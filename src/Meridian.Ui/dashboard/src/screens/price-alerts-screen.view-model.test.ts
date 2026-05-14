import { describe, expect, it } from "vitest";
import {
  buildPriceAlertConditionHelpText,
  buildPriceAlertFieldHelpText,
  buildPriceAlertListSection,
  buildPriceAlertMetrics,
  buildPriceAlertSubmitAction,
  buildPriceAlertSubmitFeedback,
  buildPriceAlertTriggerSection,
  DEFAULT_PRICE_ALERT_FORM,
  formatPriceAlertPrice,
  formatPriceAlertTimestamp,
  parseThreshold,
  priceAlertDraftFromForm,
  sortPriceAlerts,
  validatePriceAlertForm
} from "./price-alerts-screen.view-model";
import type { PriceAlert, PriceAlertTrigger } from "@/lib/price-alerts/types";

describe("validatePriceAlertForm", () => {
  it("flags an empty symbol", () => {
    const v = validatePriceAlertForm({ ...DEFAULT_PRICE_ALERT_FORM, threshold: "10" });
    expect(v.symbolError).not.toBeNull();
    expect(v.canSubmit).toBe(false);
  });

  it("flags an invalid symbol", () => {
    const v = validatePriceAlertForm({ ...DEFAULT_PRICE_ALERT_FORM, symbol: "bad symbol!", threshold: "10" });
    expect(v.symbolError).not.toBeNull();
  });

  it("accepts valid symbol with dot/dash characters", () => {
    const v = validatePriceAlertForm({ ...DEFAULT_PRICE_ALERT_FORM, symbol: "BRK.B", threshold: "300" });
    expect(v.symbolError).toBeNull();
    expect(v.thresholdError).toBeNull();
    expect(v.canSubmit).toBe(true);
  });

  it("flags a missing threshold", () => {
    const v = validatePriceAlertForm({ ...DEFAULT_PRICE_ALERT_FORM, symbol: "AAPL" });
    expect(v.thresholdError).not.toBeNull();
  });

  it("flags a non-positive threshold", () => {
    const v = validatePriceAlertForm({ ...DEFAULT_PRICE_ALERT_FORM, symbol: "AAPL", threshold: "-1" });
    expect(v.thresholdError).not.toBeNull();
  });

  it("flags a non-numeric threshold", () => {
    const v = validatePriceAlertForm({ ...DEFAULT_PRICE_ALERT_FORM, symbol: "AAPL", threshold: "abc" });
    expect(v.thresholdError).not.toBeNull();
  });
});

describe("priceAlertDraftFromForm", () => {
  it("returns null for invalid form", () => {
    expect(priceAlertDraftFromForm(DEFAULT_PRICE_ALERT_FORM)).toBeNull();
  });

  it("normalizes symbol to upper-case and trims note", () => {
    const draft = priceAlertDraftFromForm({
      symbol: "  aapl  ",
      condition: "crosses-up",
      field: "mid",
      threshold: "200.50",
      note: "  earnings prep  "
    });
    expect(draft).not.toBeNull();
    expect(draft?.symbol).toBe("AAPL");
    expect(draft?.threshold).toBe(200.5);
    expect(draft?.note).toBe("earnings prep");
    expect(draft?.condition).toBe("crosses-up");
    expect(draft?.field).toBe("mid");
  });

  it("accepts thresholds with comma separators", () => {
    const draft = priceAlertDraftFromForm({
      ...DEFAULT_PRICE_ALERT_FORM,
      symbol: "BRK.A",
      threshold: "1,000,000"
    });
    expect(draft?.threshold).toBe(1_000_000);
  });

  it("returns null note when only whitespace was entered", () => {
    const draft = priceAlertDraftFromForm({
      symbol: "MSFT",
      condition: "below",
      field: "last",
      threshold: "300",
      note: "   "
    });
    expect(draft?.note).toBeNull();
  });
});

describe("parseThreshold", () => {
  it("handles empty input", () => {
    expect(parseThreshold("")).toBeNull();
    expect(parseThreshold("   ")).toBeNull();
  });
  it("parses decimal", () => {
    expect(parseThreshold("1.25")).toBe(1.25);
  });
  it("rejects non-numeric", () => {
    expect(parseThreshold("abc")).toBeNull();
  });
});

describe("formatters", () => {
  it("formats null prices as em-dash", () => {
    expect(formatPriceAlertPrice(null)).toBe("—");
    expect(formatPriceAlertPrice(undefined)).toBe("—");
    expect(formatPriceAlertPrice(Number.NaN)).toBe("—");
  });
  it("formats numeric prices with locale separators", () => {
    expect(formatPriceAlertPrice(1234.5)).toMatch(/1[,.]234/);
  });
  it("formats null timestamps as em-dash", () => {
    expect(formatPriceAlertTimestamp(null)).toBe("—");
    expect(formatPriceAlertTimestamp(undefined)).toBe("—");
    expect(formatPriceAlertTimestamp("not-a-date")).toBe("—");
  });
  it("formats timestamps with deterministic UTC labels", () => {
    expect(formatPriceAlertTimestamp("2026-05-12T14:02:37.000Z")).toBe("May 12, 14:02:37 UTC");
  });
});

describe("price alert presentation state", () => {
  it("derives submit disabled reasons from validation", () => {
    const validation = validatePriceAlertForm(DEFAULT_PRICE_ALERT_FORM);
    const action = buildPriceAlertSubmitAction(validation);

    expect(action.disabled).toBe(true);
    expect(action.disabledReason).toContain("Enter a symbol");
    expect(action.ariaLabel).toContain("unavailable");
  });

  it("builds a live-quote handoff after creating an alert", () => {
    const feedback = buildPriceAlertSubmitFeedback({
      symbol: "BRK/B",
      condition: "above",
      field: "last",
      threshold: 300,
      note: null
    });

    expect(feedback.text).toBe("Alert set: BRK/B last ≥ 300");
    expect(feedback.action).toEqual({
      label: "Open live quotes",
      href: "/data/quotes?symbol=BRK%2FB",
      ariaLabel: "Open live quotes for BRK/B after creating price alert"
    });
  });

  it("derives condition and field helper text for the alert form", () => {
    expect(buildPriceAlertConditionHelpText("crosses-up")).toBe(
      "Fires once when price rises through the threshold."
    );
    expect(buildPriceAlertFieldHelpText("mid")).toBe("Field: bid-ask midpoint.");
  });

  it("maps alert service counters to design-system metric snapshots", () => {
    const metrics = buildPriceAlertMetrics({
      enabledCount: 2,
      unacknowledgedCount: 1,
      lastPollAt: "2026-05-12T14:02:37.000Z"
    });

    expect(metrics).toHaveLength(3);
    expect(metrics[0]).toMatchObject({ label: "Active alerts", value: "2", tone: "default" });
    expect(metrics[1]).toMatchObject({ label: "Unacknowledged", value: "1", tone: "warning" });
    expect(metrics[2].value).toBe("May 12, 14:02:37 UTC");
    expect(metrics[2].delta).toBe("Refreshed");
  });

  it("sorts active alerts before disabled alerts and builds row actions", () => {
    const sorted = sortPriceAlerts([
      buildAlert({ id: "disabled", symbol: "MSFT", enabled: false }),
      buildAlert({ id: "active-b", symbol: "TSLA", threshold: 180 }),
      buildAlert({ id: "active-a", symbol: "AAPL", threshold: 200 })
    ]);
    const section = buildPriceAlertListSection(sorted, 2);

    expect(section.summary).toContain("3 alerts");
    expect(section.rows.map((row) => row.symbol)).toEqual(["AAPL", "TSLA", "MSFT"]);
    expect(section.rows[0].primaryAction.id).toBe("snooze");
    expect(section.rows[2].primaryAction.id).toBe("reset");
    expect(section.rows[0].rowAriaLabel).toContain("AAPL price alert");
  });

  it("derives trigger rows with acknowledgement action state", () => {
    const section = buildPriceAlertTriggerSection([
      buildTrigger({ id: "trigger-new", acknowledged: false }),
      buildTrigger({ id: "trigger-ack", acknowledged: true, symbol: "MSFT" })
    ], 1, "trigger-ack");

    expect(section.hasRows).toBe(true);
    expect(section.selectedRowId).toBe("trigger-ack");
    expect(section.selectedDetail).toEqual(expect.objectContaining({
      ariaLabel: "Triggered price alert detail for MSFT",
      statusLabel: "Acknowledged",
      action: null
    }));
    expect(section.acknowledgeAllAction.disabled).toBe(false);
    expect(section.rows[0].acknowledgeAction?.ariaLabel).toContain("AAPL");
    expect(section.rows[0].expanded).toBe(false);
    expect(section.rows[1].expanded).toBe(true);
    expect(section.rows[1].detailPanelId).toBe("price-alert-trigger-detail-panel");
    expect(section.rows[0].triggeredAtLabel).toBe("May 12, 14:02:37 UTC");
    expect(section.rows[1].acknowledgeAction).toBeNull();
  });

  it("derives configured alert detail state with row linkage", () => {
    const section = buildPriceAlertListSection([
      buildAlert({ id: "a-1", symbol: "AAPL", note: "Earnings watch" }),
      buildAlert({ id: "a-2", symbol: "MSFT", enabled: false, triggeredAt: "2026-05-12T15:00:00.000Z" })
    ], 1, "a-2");

    expect(section.selectedRowId).toBe("a-2");
    expect(section.rows[0].expanded).toBe(false);
    expect(section.rows[1].expanded).toBe(true);
    expect(section.rows[1].detailPanelId).toBe("price-alert-list-detail-panel");
    expect(section.rows[1].rowSelectAriaLabel).toBe("Inspect configured alert MSFT");
    expect(section.selectedDetail).toEqual(expect.objectContaining({
      ariaLabel: "Configured price alert detail for MSFT",
      statusLabel: "Triggered",
      statusVariant: "outline"
    }));
    expect(section.selectedDetail?.fields).toContainEqual({
      id: "note",
      label: "Operator note",
      value: "No operator note",
      tone: "muted"
    });
    expect(section.selectedDetail?.action).toEqual(expect.objectContaining({
      id: "reset",
      kind: "alert",
      ariaLabel: "Re-enable MSFT alert"
    }));
  });
});

function buildAlert(overrides: Partial<PriceAlert> = {}): PriceAlert {
  return {
    id: "alert-fixture",
    symbol: "AAPL",
    condition: "above",
    field: "last",
    threshold: 200,
    note: null,
    createdAt: "2026-05-12T12:00:00.000Z",
    snoozedUntil: null,
    enabled: true,
    triggeredAt: null,
    lastObservedPrice: null,
    lastObservedAt: null,
    ...overrides
  };
}

function buildTrigger(overrides: Partial<PriceAlertTrigger> = {}): PriceAlertTrigger {
  return {
    id: "trigger-fixture",
    alertId: "alert-fixture",
    symbol: "AAPL",
    condition: "above",
    field: "last",
    threshold: 200,
    triggeredPrice: 201.25,
    triggeredAt: "2026-05-12T14:02:37.000Z",
    acknowledged: false,
    note: null,
    ...overrides
  };
}
