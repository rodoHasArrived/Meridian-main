import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { vi } from "vitest";
import { ReconciliationComparisonPanel } from "./ReconciliationComparisonPanel";
import type { ReconciliationComparisonViewState } from "@/screens/accounting-screen.view-model";

const view: ReconciliationComparisonViewState = {
  title: "Statement vs ledger",
  subtitle: "Line comparison",
  statementHeading: "Statement",
  ledgerHeading: "Ledger",
  matchedBadgeLabel: "1 matched",
  openBadgeLabel: "1 open",
  statementBalanceLabel: "$100.00",
  ledgerBalanceLabel: "$95.00",
  varianceLabel: "Out by $5.00",
  varianceTone: "warning",
  rows: [],
  statementLines: [
    {
      id: "s1",
      matchKey: "m1",
      title: "Wire receipt",
      meta: "Custodian",
      amountLabel: "$100.00",
      statusLabel: "Matched",
      statusTone: "success"
    }
  ],
  ledgerLines: [
    {
      id: "l1",
      matchKey: "m1",
      title: "Ledger receipt",
      meta: "Journal",
      amountLabel: "$100.00",
      statusLabel: "Matched",
      statusTone: "success"
    },
    {
      id: "l2",
      matchKey: "m2",
      title: "Fee accrual",
      meta: "Journal",
      amountLabel: "($5.00)",
      statusLabel: "Break",
      statusTone: "danger"
    }
  ],
  lineSource: "transactions",
  ariaLabel: "Reconciliation comparison"
};

describe("ReconciliationComparisonPanel", () => {
  it("renders statement and ledger panes with balances", () => {
    render(<ReconciliationComparisonPanel view={view} selectedRunId={null} />);

    expect(screen.getByRole("region", { name: "Reconciliation comparison" })).toBeInTheDocument();
    expect(screen.getByRole("table", { name: "Statement reconciliation lines" })).toBeInTheDocument();
    expect(screen.getByRole("table", { name: "Ledger reconciliation lines" })).toBeInTheDocument();
    expect(screen.getByText("Statement balance")).toBeInTheDocument();
    expect(screen.getByText("Out by $5.00")).toBeInTheDocument();
  });

  it("cross-lights paired lines when a side is selected", async () => {
    render(<ReconciliationComparisonPanel view={view} selectedRunId={null} />);

    const statementLine = screen.getByRole("row", { name: "Wire receipt - Matched" });
    const ledgerLine = screen.getByRole("row", { name: "Ledger receipt - Matched" });
    await userEvent.click(statementLine);

    expect(statementLine).toHaveClass("is-selected");
    expect(ledgerLine).toHaveClass("is-cross-lit");
  });

  it("selects fallback runs by click and keyboard while retaining local cross-lighting", async () => {
    const user = userEvent.setup();
    const onSelectRun = vi.fn();
    render(<ReconciliationComparisonPanel
      view={{ ...view, lineSource: "runs" }}
      selectedRunId="m1"
      onSelectRun={onSelectRun}
    />);

    const statementLine = screen.getByRole("row", { name: "Wire receipt - Matched" });
    const ledgerLine = screen.getByRole("row", { name: "Ledger receipt - Matched" });

    await user.click(statementLine);
    expect(onSelectRun).toHaveBeenLastCalledWith("m1");
    expect(statementLine).toHaveClass("is-selected");
    expect(ledgerLine).toHaveClass("is-cross-lit");

    ledgerLine.focus();
    await user.keyboard("{Enter}");
    expect(onSelectRun).toHaveBeenCalledTimes(2);
    expect(onSelectRun).toHaveBeenLastCalledWith("m1");
    expect(ledgerLine).toHaveClass("is-selected");
    expect(statementLine).toHaveClass("is-cross-lit");
  });

  it("does not toggle the controlled active run off when it is activated repeatedly", async () => {
    const user = userEvent.setup();
    const onSelectRun = vi.fn();
    render(<ReconciliationComparisonPanel
      view={{ ...view, lineSource: "runs" }}
      selectedRunId="m1"
      onSelectRun={onSelectRun}
    />);

    const statementLine = screen.getByRole("row", { name: "Wire receipt - Matched" });
    await user.click(statementLine);
    await user.click(statementLine);

    expect(onSelectRun).toHaveBeenCalledTimes(2);
    expect(onSelectRun).toHaveBeenLastCalledWith("m1");
    expect(statementLine).toHaveClass("is-selected");
    expect(statementLine).toHaveAttribute("aria-selected", "true");
  });

  it("follows parent-driven run selection changes", () => {
    const runView = { ...view, lineSource: "runs" as const };
    const { rerender } = render(<ReconciliationComparisonPanel
      view={runView}
      selectedRunId="m1"
    />);

    const firstRun = screen.getByRole("row", { name: "Wire receipt - Matched" });
    const secondRun = screen.getByRole("row", { name: "Fee accrual - Break" });
    expect(firstRun).toHaveClass("is-selected");

    rerender(<ReconciliationComparisonPanel view={runView} selectedRunId="m2" />);

    expect(firstRun).not.toHaveClass("is-selected");
    expect(firstRun).not.toHaveClass("is-cross-lit");
    expect(secondRun).toHaveClass("is-selected");
    expect(secondRun).toHaveAttribute("aria-selected", "true");
  });

  it("omits zero-match chips and describes warning rows as needing review", () => {
    render(
      <ReconciliationComparisonPanel
        view={{
          ...view,
          statementLines: [{
            ...view.statementLines[0],
            statusLabel: "Review required",
            statusTone: "warning"
          }],
          ledgerLines: []
        }}
        selectedRunId={null}
      />
    );

    expect(screen.queryByText("0 matched")).not.toBeInTheDocument();
    expect(screen.getByText("1 needs review")).toBeInTheDocument();
    expect(screen.queryByText("1 timing")).not.toBeInTheDocument();
  });
});
