import { describe, expect, it } from "vitest";
import { formatCurrencyAmount, formatCurrencyWithCode, financialValueState, VALUE_STATE_LABELS } from "@/lib/format";
import { buildBasisBridgeViewState } from "./accounting-screen.basis-bridge.view-model";
import { buildAccountingReportPackageRequest } from "./accounting-screen.close-cockpit.view-model";
import type { BasisAwareLedgerTrialBalanceLine } from "./accounting-screen.view-model";
import type { AccountingReportPackageBundle, ClosePeriodPlan, OperationsContinuityWorkflow } from "@/types";

// P1 financial-value meaning: every amount is a value, a confirmed zero, or an explicit state.
// Missing is never 0, and an unknown currency is never "$".

function line(accountName: string, accountingBasis: BasisAwareLedgerTrialBalanceLine["accountingBasis"], balance: number): BasisAwareLedgerTrialBalanceLine {
  return {
    accountName, accountType: "Asset", symbol: null, financialAccountId: null, balance, entryCount: 1, security: null,
    accountingBasis, accountingPolicyId: "policy", accountingPolicyVersion: "1"
  } as BasisAwareLedgerTrialBalanceLine;
}

describe("basis bridge one-sided accounts", () => {
  const rows = [
    line("Cash", "Primary", 1000), line("Cash", "Tax", 900),
    line("Deferred tax", "Tax", 12400),
    line("Accrued fees", "Primary", 250)
  ];

  it("labels accounts present on one basis as not compared instead of a full-balance variance", () => {
    const view = buildBasisBridgeViewState(rows, "Tax", "run-1", "EUR");
    const deferred = view.rows.find((row) => row.accountLabel === "Deferred tax")!;
    const accrued = view.rows.find((row) => row.accountLabel === "Accrued fees")!;

    expect(deferred.comparison).toBe("comparison-only");
    expect(deferred.variance).toBeNull();
    expect(deferred.varianceLabel).toBe("Only on Tax basis — not compared");
    expect(deferred.primaryBalanceLabel).toBe("Not on Primary basis");
    expect(deferred.varianceLabel).not.toMatch(/12,400/);
    expect(accrued.comparison).toBe("primary-only");
    expect(accrued.varianceLabel).toBe("Only on Primary basis — not compared");
    expect(accrued.comparisonBalanceLabel).toBe("Not on Tax basis");
  });

  it("excludes one-sided accounts from the variance total and counts them", () => {
    const view = buildBasisBridgeViewState(rows, "Tax", "run-1", "EUR");

    expect(view.summary).toMatchObject({
      comparedCount: 1,
      oneSidedCount: 2,
      countsLabel: "Compared 1 · one-sided 2",
      oneSidedLabel: "2 accounts on one basis only"
    });
    expect(view.summary?.varianceTotalLabel).toBe("-€100.00");
  });

  it("renders a EUR book in EUR and never as dollars", () => {
    const view = buildBasisBridgeViewState(rows, "Tax", "run-1", "EUR");
    const cash = view.rows.find((row) => row.accountLabel === "Cash")!;

    expect(cash.primaryBalanceLabel).toBe("€1,000.00");
    expect(cash.comparisonBalanceLabel).toBe("€900.00");
    expect(JSON.stringify(view)).not.toContain("$");
  });

  it("renders bare amounts rather than assuming USD when the book currency is unknown", () => {
    const view = buildBasisBridgeViewState(rows, "Tax", "run-1", null);
    expect(JSON.stringify(view)).not.toContain("$");
    expect(view.rows.find((row) => row.accountLabel === "Cash")!.primaryBalanceLabel).toBe("1,000.00");
  });
});

describe("currency-required money formatting", () => {
  it("never prints $ for a non-USD or unknown currency", () => {
    expect(formatCurrencyAmount(1250, "EUR")).toBe("€1,250.00");
    expect(formatCurrencyAmount(1250, "EUR", { showCode: true })).toBe("1,250.00 EUR");
    expect(formatCurrencyAmount(1250, "JPY", { showCode: true })).toBe("1,250 JPY");
    expect(formatCurrencyAmount(1250, null)).toBe("1,250.00");
    expect(formatCurrencyWithCode(1250, "")).toBe("1,250");
    expect(formatCurrencyWithCode(1250, "EUR")).not.toContain("$");
  });

  it("keeps missing distinct from a confirmed zero", () => {
    expect(formatCurrencyAmount(null, "EUR")).toBe(VALUE_STATE_LABELS.unavailable);
    expect(formatCurrencyAmount(0, "EUR")).toBe("€0.00");
    expect(financialValueState(null)).toBe("unavailable");
    expect(financialValueState(0)).toBe("zero");
    expect(financialValueState("0.00")).toBe("zero");
    expect(financialValueState(12.5)).toBe("value");
  });
});

describe("close report-package request", () => {
  const workflow = {
    workflowId: "wf-1", fundAccountId: "fund-1", periodId: "2026-10",
    evidenceLinks: [], reportPackReadiness: { evidenceLinks: [] }, accountingRecordSummary: null, closePackage: null
  } as unknown as OperationsContinuityWorkflow;

  function seed(overrides: { nav?: number | null; currency?: string | null } = {}): AccountingReportPackageBundle {
    return {
      financialStatements: { ledgerBookId: null },
      investorCapitalStatements: [{
        capitalAccountId: "ca-1", investorId: "inv-1", beginningCapital: 100, contributions: 10, distributions: 5,
        realizedGainLoss: 2, endingCapital: 107, currency: overrides.currency === undefined ? "EUR" : overrides.currency
      }],
      realizedGainLoss: { realizedGainLoss: 2 },
      navPackage: { nav: overrides.nav === undefined ? 107 : overrides.nav, currency: overrides.currency === undefined ? "EUR" : overrides.currency }
    } as unknown as AccountingReportPackageBundle;
  }

  it("is blocked, not sent with zero, when NAV is missing", () => {
    const bundle = seed();
    bundle.navPackage.nav = null as unknown as number;
    bundle.investorCapitalStatements[0].endingCapital = null as unknown as number;
    const result = buildAccountingReportPackageRequest(workflow, null as ClosePeriodPlan | null, bundle);

    expect(result.ok).toBe(false);
    if (result.ok === false) {
      expect(result.missingInputs).toEqual(["NAV"]);
      expect(result.message).toContain("NAV is not available");
    }
  });

  it("refuses to build without any seed rather than inventing 0 and USD", () => {
    const result = buildAccountingReportPackageRequest(workflow, null, null);
    expect(result.ok).toBe(false);
    if (result.ok === false) {
      expect(result.missingInputs).toEqual(["beginning capital", "contributions", "distributions", "realized gain/loss", "NAV", "currency"]);
    }
  });

  it("sends the supplied figures in the supplied currency", () => {
    const result = buildAccountingReportPackageRequest(workflow, null, seed());
    expect(result.ok).toBe(true);
    if (result.ok === true) {
      expect(result.request).toMatchObject({ nav: 107, currency: "EUR", beginningCapital: 100, contributions: 10, distributions: 5, realizedGainLoss: 2 });
    }
  });
});
