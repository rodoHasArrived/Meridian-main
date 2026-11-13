import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { describe, expect, it } from "vitest";
import { ReconciliationComparisonPanel } from "@/components/accounting";
import { TieOutPanel } from "@/components/accounting/TieOutPanel";
import { buildReconciliationComparisonViewState } from "@/screens/accounting-screen.reconciliation.view-model";
import type { AccountingSystemReconciliationSummary } from "@/types";

// Renders the same composition the Accounting screen mounts under "Reconciliation exceptions and
// evidence": the comparison summary, then the tie-out that proves it. The two are built from one
// view state, so these tests pin that they can never reach different verdicts.

type Row = AccountingSystemReconciliationSummary["rows"][number];

function row(rowId: string, accountName: string, status: Row["status"], external: number, meridian: number, evidenceRef: string | null = null): Row {
  return {
    rowId,
    accountCode: rowId.toUpperCase(),
    accountName,
    currency: "USD",
    status,
    externalDebit: Math.max(external, 0),
    externalCredit: Math.max(-external, 0),
    meridianDebit: Math.max(meridian, 0),
    meridianCredit: Math.max(-meridian, 0),
    variance: external - meridian,
    detail: `${accountName} detail.`,
    evidenceRef
  };
}

function systemReconciliation(rows: Row[], breakCount: number): AccountingSystemReconciliationSummary {
  return {
    reconciliationId: "gl-recon-proof",
    importId: "gl-import-proof",
    providerId: "quickbooks-fixture",
    fundProfileId: "fund-alpha",
    periodStart: "2026-06-01",
    periodEnd: "2026-06-30",
    generatedAtUtc: "2026-07-01T00:00:00Z",
    matchedCount: rows.filter((item) => item.status === "Matched").length,
    breakCount,
    totalExternalDebits: rows.reduce((total, item) => total + item.externalDebit, 0),
    totalExternalCredits: rows.reduce((total, item) => total + item.externalCredit, 0),
    totalMeridianDebits: rows.reduce((total, item) => total + item.meridianDebit, 0),
    totalMeridianCredits: rows.reduce((total, item) => total + item.meridianCredit, 0),
    postingEnabled: false,
    postingDisabledReason: "Posting/export remains disabled.",
    evidenceReferences: [],
    rows
  };
}

function renderMount(reconciliation: AccountingSystemReconciliationSummary | null) {
  const view = buildReconciliationComparisonViewState({
    statementRuns: [],
    fallbackQueue: [],
    selectedRunId: null,
    cashFlow: null,
    systemReconciliation: reconciliation
  });
  const result = render(
    <main>
      <h3>Reconciliation exceptions and evidence</h3>
      <ReconciliationComparisonPanel view={view} />
      {view.tieOut ? <TieOutPanel {...view.tieOut} /> : null}
    </main>
  );
  return { ...result, view };
}

const comparisonName = "Cash reconciliation broker statement versus ledger comparison";

describe("Accounting reconciliation tie-out proof", () => {
  it("ties out exactly when the summary reads Balanced", () => {
    renderMount(systemReconciliation([row("r-cash", "Cash", "Matched", 500, 500)], 0));

    expect(within(screen.getByRole("region", { name: comparisonName })).getByText("Balanced")).toBeInTheDocument();
    const proof = screen.getByRole("region", { name: "Tie-out proof" });
    expect(proof).toHaveAttribute("data-tie-out-status", "tied");
    expect(within(proof).getByRole("heading", { level: 3, name: "Tie-out proof" })).toBeInTheDocument();
    expect(within(proof).getByText("Tied out")).toBeInTheDocument();
    expect(within(proof).getByText("External GL reconciliation · 2026-06-01 to 2026-06-30")).toBeInTheDocument();
    expect(within(proof).getByText("Custodian statement")).toBeInTheDocument();
    expect(within(proof).getByText("Internal ledger")).toBeInTheDocument();
    expect(within(proof).getAllByText("$500.00")).toHaveLength(2);
    expect(within(proof).getByText("The two sources agree. No reconciling item is needed.")).toBeInTheDocument();
  });

  it("proves the same out-by figure the summary reports and lists the open rows", async () => {
    renderMount(systemReconciliation([
      row("r-cash", "Cash", "Matched", 500, 500),
      row("r-fees", "Fees", "Variance", -200, -180, "qbo:trial-balance:fees"),
      row("r-interest", "Interest", "MissingMeridian", 142, 0)
    ], 2));

    expect(within(screen.getByRole("region", { name: comparisonName })).getByText("Out by $122")).toBeInTheDocument();
    const proof = screen.getByRole("region", { name: "Tie-out proof" });
    expect(proof).toHaveAttribute("data-tie-out-status", "exception");
    expect(within(proof).getByText("Exception")).toBeInTheDocument();
    // Gross difference and residual both carry the summary's 122.
    expect(within(proof).getByText("+$122.00")).toBeInTheDocument();
    expect(within(proof).getByText("$122.00")).toBeInTheDocument();
    expect(within(proof).getByText("Residual exceeds tolerance and is unexplained.")).toBeInTheDocument();

    await userEvent.click(within(proof).getByRole("button", { name: "Explained · 0 reconciling items · 2 identified, not explained" }));

    const rows = within(within(proof).getByRole("region", { name: "Reconciling items" })).getAllByRole("row");
    expect(rows).toHaveLength(3);
    expect(rows[1]).toHaveTextContent("Fees");
    expect(rows[1]).toHaveTextContent("−$20.00");
    expect(rows[1]).toHaveTextContent("Variance");
    expect(rows[1]).toHaveTextContent("1 evidence reference");
    expect(rows[2]).toHaveTextContent("Interest");
    expect(rows[2]).toHaveTextContent("+$142.00");
    expect(rows[2]).toHaveTextContent("Missing in ledger");
    expect(rows[2]).toHaveTextContent("No evidence");
  });

  it("never reads offsetting breaks as tied", () => {
    renderMount(systemReconciliation([
      row("tb-1", "Cash", "Variance", 500, 480),
      row("tb-2", "Payables", "Variance", -500, -480)
    ], 2));

    const comparison = screen.getByRole("region", { name: comparisonName });
    expect(within(comparison).getByText("2 open breaks")).toBeInTheDocument();
    expect(within(comparison).queryByText("Balanced")).not.toBeInTheDocument();

    const proof = screen.getByRole("region", { name: "Tie-out proof" });
    expect(proof).toHaveAttribute("data-tie-out-status", "exception");
    expect(proof).not.toHaveTextContent("Tied out");
    expect(proof).not.toHaveTextContent("Agrees exactly");
    expect(within(proof).getByText("2 open breaks remain although the totals net to zero. Offsetting breaks are not a tie-out.")).toBeInTheDocument();
    expect(within(proof).getByText("Not tied")).toBeInTheDocument();
  });

  it("reads an unusable side as indeterminate, never as zero", () => {
    const reconciliation = systemReconciliation([row("r-cash", "Cash", "Matched", 500, 500)], 0);
    renderMount({ ...reconciliation, totalMeridianDebits: Number.NaN });

    const proof = screen.getByRole("region", { name: "Tie-out proof" });
    expect(proof).toHaveAttribute("data-tie-out-status", "indeterminate");
    expect(proof).not.toHaveTextContent("Tied out");
    expect(within(proof).getByText("Not tied — input missing")).toBeInTheDocument();
    expect(proof).toHaveTextContent("Cannot tie out. Internal ledger has no value.");
  });

  it("renders no tie-out and no actions without a loaded external GL reconciliation", () => {
    renderMount(null);

    expect(screen.getByRole("region", { name: comparisonName })).toBeInTheDocument();
    expect(screen.queryByRole("region", { name: "Tie-out proof" })).not.toBeInTheDocument();
  });

  it("is read-only: no accept, resolve, escalate, reopen or add-item action and no reviewer line", () => {
    renderMount(systemReconciliation([
      row("r-cash", "Cash", "Matched", 500, 500),
      row("r-fees", "Fees", "Variance", -200, -180)
    ], 1));

    const proof = screen.getByRole("region", { name: "Tie-out proof" });
    const buttons = within(proof).getAllByRole("button");
    expect(buttons).toHaveLength(1);
    expect(buttons[0]).toHaveAttribute("aria-expanded", "false");
    expect(proof).not.toHaveTextContent(/Accept|Resolve|Escalate|Reopen|Add reconciling item|Reviewed by/);
  });

  it("has no accessibility violations with the items disclosed", async () => {
    const { container } = renderMount(systemReconciliation([
      row("r-cash", "Cash", "Matched", 500, 500),
      row("r-fees", "Fees", "Variance", -200, -180, "qbo:trial-balance:fees")
    ], 1));

    await userEvent.click(screen.getByRole("button", { name: /Explained · 0 reconciling items/ }));

    expect((await axe(container)).violations).toEqual([]);
  });
});
