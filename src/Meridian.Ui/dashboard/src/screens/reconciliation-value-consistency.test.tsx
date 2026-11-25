import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import type { ReconciliationBreakQueueItem, AccountingWorkspaceResponse } from "@/types";
import { CaseVariance } from "./case-inspector";
import { buildReconciliationBreakRows } from "./accounting-screen.reconciliation.view-model";
import { buildOperatorReadinessConsoleState } from "./operator-readiness-console.view-model";
import { caseQueueResidual, caseResidual, caseVarianceLabel, operationsCaseVarianceLabel } from "./reconciliation-case-values";

const item: ReconciliationBreakQueueItem = {
  breakId: "cash-exact", runId: "run-exact", strategyName: "Synthetic cash review", category: "CashMismatch",
  status: "Open", variance: 0, reason: "Review retained residual", assignedTo: null,
  detectedAt: "2026-11-20T00:00:00Z", lastUpdatedAt: "2026-11-20T00:00:00Z",
  reviewedBy: null, reviewedAt: null, resolvedBy: null, resolvedAt: null, resolutionNote: null,
  measures: [{ kind: "Value", unit: "EUR", expected: 9007199254740992, actual: 9007199254740992, variance: 0,
    tolerance: 0, expectedExact: "9007199254740993.00000000", actualExact: "9007199254740993.00000001",
    varianceExact: "0.00000001", toleranceExact: "0.00000000" }]
};

describe("reconciliation values across work surfaces", () => {
  it("keeps the same exact, unit-labelled residual in Today, Close, Exceptions and the inspector", () => {
    const label = "+0.00000001 EUR";
    expect(caseVarianceLabel(item)).toBe(label);
    const queue = caseQueueResidual(item);
    expect(`${queue.amount} ${queue.unit}`).toBe(label);
    expect(buildReconciliationBreakRows([item], null)[0]).toMatchObject({ varianceLabel: label, varianceTone: "success" });
    expect(operationsCaseVarianceLabel({ measures: item.measures, expectedAmount: 0, actualAmount: 0, variance: 0 }))
      .toBe("Expected 9,007,199,254,740,993.00000000 / Actual 9,007,199,254,740,993.00000001 / Variance +0.00000001 EUR");
    const accounting = { metrics: [], reconciliationQueue: [], breakQueue: [item], cashFlow: null, reporting: null } as unknown as AccountingWorkspaceResponse;
    const today = buildOperatorReadinessConsoleState({ strategy: null, trading: null, data: null, accounting,
      operatorInbox: null, inboxLoading: false, inboxError: null });
    expect(today.reconciliationRows[0].meta).toContain(label);
    render(<MemoryRouter><CaseVariance item={item} /></MemoryRouter>);
    expect(screen.getByText(label)).toBeInTheDocument();
    expect(screen.queryByText("0.00 EUR")).not.toBeInTheDocument();
  });

  it.each([
    ["USD", "0", "0.00 USD"], ["JPY", "500", "+500 JPY"], ["KWD", "1.001", "+1.001 KWD"],
    ["EUR", "-0.0000000000000000000000000001", "−0.0000000000000000000000000001 EUR"],
    ["units", "0.125", "+0.125 units"]
  ])("retains the source unit and precision for %s", (unit, value, label) => {
    const record = { ...item, measures: [{ ...item.measures![0], unit, varianceExact: value }] };
    expect(caseVarianceLabel(record)).toBe(label);
    render(<MemoryRouter><CaseVariance item={record} /></MemoryRouter>);
    expect(screen.getByText(label)).toBeInTheDocument();
  });

  it("never substitutes the legacy zero for an explicitly absent exact value", () => {
    // Exercise an unavailable wire value even though the current queue DTO emits non-null text.
    const absent = { ...item, measures: undefined, variance: 0, varianceExact: null } as unknown as ReconciliationBreakQueueItem;
    expect(caseResidual(absent)).toBeNull();
    expect(caseVarianceLabel(absent)).toBe("Not recorded");
    expect(buildReconciliationBreakRows([absent], null)[0].varianceTone).toBe("default");
    render(<MemoryRouter><CaseVariance item={absent} /></MemoryRouter>);
    expect(screen.getByText("Not recorded")).toBeInTheDocument();
  });

  it("keeps unavailable evidence distinct from zero and does not infer a currency", () => {
    const unavailable = { ...item, measures: [{ ...item.measures![0], actualExact: null,
      unavailableReason: "Custodian amount unavailable" }] };
    expect(caseVarianceLabel(unavailable)).toBe("Unavailable");
    expect(caseResidual(unavailable)).toBeNull();
    const legacy = { ...item, measures: undefined, varianceExact: "500.00" };
    expect(caseVarianceLabel(legacy)).toBe("+500.00 Unit unavailable");
    expect(operationsCaseVarianceLabel({ expectedAmount: null, actualAmount: null, variance: null })).toBe("Amounts not recorded");
    render(<MemoryRouter><CaseVariance item={unavailable} /></MemoryRouter>);
    expect(screen.getByText("Unavailable")).toBeInTheDocument();
  });

  it("does not collapse multiple measures into one amount", () => {
    const multiple: ReconciliationBreakQueueItem = { ...item, measures: [...item.measures!,
      { kind: "Quantity", unit: "units", expected: 1, actual: 2, variance: 1, tolerance: 0 }] };
    expect(caseVarianceLabel(multiple)).toBe("Multiple");
    expect(buildReconciliationBreakRows([multiple], null)[0]).toMatchObject({ varianceLabel: "Multiple", varianceTone: "default" });
    render(<MemoryRouter><CaseVariance item={multiple} /></MemoryRouter>);
    expect(screen.getByText("Multiple")).toBeInTheDocument();
  });

  it("withholds legacy amounts that exceed the response's numeric precision", () => {
    const unsafe = { ...item, variance: 9007199254740992, measures: undefined };
    expect(caseVarianceLabel(unsafe)).toBe("Unavailable");
    expect(caseResidual(unsafe)).toBeNull();
    expect(buildReconciliationBreakRows([unsafe], null)[0]).toMatchObject({ varianceLabel: "Unavailable", varianceTone: "default" });
    expect(operationsCaseVarianceLabel({ expectedAmount: 9007199254740992, actualAmount: 9007199254740992, variance: 9007199254740992 }))
      .toBe("Expected Unavailable / Actual Unavailable / Variance Unavailable · Unit unavailable");
    const unsafeMeasure = { ...item, measures: [{ kind: "Value" as const, unit: "EUR", expected: 9007199254740992,
      actual: 9007199254740992, variance: 9007199254740992, tolerance: 0 }] };
    expect(caseVarianceLabel(unsafeMeasure)).toBe("Unavailable");
    expect(caseResidual(unsafeMeasure)).toBeNull();
    render(<MemoryRouter><CaseVariance item={unsafeMeasure} /></MemoryRouter>);
    expect(screen.getByText("Unavailable")).toBeInTheDocument();
    expect(screen.queryByText(/9,007,199,254,740,992/)).not.toBeInTheDocument();
  });
});
