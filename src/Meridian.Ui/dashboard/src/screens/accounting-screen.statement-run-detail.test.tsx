import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import * as detailApi from "@/lib/api/statement-run-detail.api";
import { StatementRunDetailTabs } from "@/screens/accounting-screen.statement-run-detail";
import type { ReconciliationRunDetailTabViewModel } from "@/screens/accounting-screen.view-model";

vi.mock("@/lib/api/statement-run-detail.api", () => ({
  getStatementRunValidation: vi.fn(),
  getStatementRunBreaks: vi.fn(),
  reconcileStatementRun: vi.fn()
}));

const api = vi.mocked(detailApi);

afterEach(() => {
  vi.resetAllMocks();
});

function tab(
  id: ReconciliationRunDetailTabViewModel["id"],
  label: string,
  overrides: Partial<ReconciliationRunDetailTabViewModel> = {}
): ReconciliationRunDetailTabViewModel {
  return {
    id,
    label,
    badgeLabel: "3",
    description: `${label} description.`,
    disabled: false,
    disabledReason: null,
    ariaLabel: `${label} tab`,
    ...overrides
  };
}

const tabs = [tab("validation", "Validation"), tab("breaks-cases", "Breaks & Cases"), tab("evidence", "Evidence")];

function primeReads(options: { blocked?: boolean } = {}) {
  api.getStatementRunValidation.mockResolvedValue({
    runId: "run-1",
    isBlocked: options.blocked ?? false,
    issues: [
      {
        issueId: "issue-1",
        severity: 2,
        code: "CASH_MISSING",
        message: "Statement cash row has no book counterpart.",
        sourceRowNumber: 14,
        sourceColumn: "cashBalance"
      }
    ]
  });
  api.getStatementRunBreaks.mockResolvedValue([
    {
      breakId: "break-1",
      runId: "run-1",
      importId: "import-1",
      sourceReference: "STMT-000014",
      breakType: 7,
      category: "Cash",
      delta: -1250.5,
      tolerance: 100,
      toleranceBreached: true,
      createdAtUtc: "2026-08-26T12:00:00Z",
      status: "Open"
    }
  ]);
}

describe("StatementRunDetailTabs", () => {
  it("renders the served validation rows the tab previously only described", async () => {
    primeReads();
    render(<StatementRunDetailTabs panelId="panel" runId="run-1" tabs={tabs} />);

    expect(await screen.findByText("Statement cash row has no book counterpart.")).toBeInTheDocument();
    expect(screen.getByText("CASH_MISSING")).toBeInTheDocument();
    expect(screen.getByText("Row 14, cashBalance")).toBeInTheDocument();
  });

  it("fetches nothing until a run is selected", async () => {
    render(<StatementRunDetailTabs panelId="panel" runId={null} tabs={tabs} />);

    await waitFor(() => expect(api.getStatementRunValidation).not.toHaveBeenCalled());
    expect(api.getStatementRunBreaks).not.toHaveBeenCalled();
  });

  it("surfaces the blocked verdict and disables the re-run action", async () => {
    primeReads({ blocked: true });
    render(<StatementRunDetailTabs panelId="panel" runId="run-1" tabs={tabs} />);

    expect(await screen.findByText(/Reconciliation is blocked by validation/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Re-run reconciliation matching/ })).toBeDisabled();
  });

  it("reports each read's failure separately instead of collapsing them", async () => {
    api.getStatementRunValidation.mockRejectedValue(new Error("validation route unavailable"));
    api.getStatementRunBreaks.mockResolvedValue([]);
    render(<StatementRunDetailTabs panelId="panel" runId="run-1" tabs={tabs} />);

    const alert = await screen.findByText(/Validation: validation route unavailable/);
    expect(alert).toBeInTheDocument();
    expect(screen.queryByText(/Breaks:/)).not.toBeInTheDocument();
  });

  it("re-runs matching, refetches, and reports the returned status", async () => {
    primeReads();
    api.reconcileStatementRun.mockResolvedValue({
      runId: "run-1",
      status: 7,
      completedAtUtc: "2026-08-26T13:00:00Z"
    });
    const onRunReconciled = vi.fn();
    render(<StatementRunDetailTabs panelId="panel" runId="run-1" tabs={tabs} onRunReconciled={onRunReconciled} />);

    await screen.findByText("CASH_MISSING");
    await userEvent.click(screen.getByRole("button", { name: /Re-run reconciliation matching/ }));

    await waitFor(() => expect(api.reconcileStatementRun).toHaveBeenCalledWith("run-1"));
    expect(await screen.findByText(/Matching returned Completed/)).toBeInTheDocument();
    expect(api.getStatementRunValidation).toHaveBeenCalledTimes(2);
    expect(onRunReconciled).toHaveBeenCalledWith("run-1");
  });

  it("renders served break rows with the ordinal break type resolved", async () => {
    primeReads();
    render(<StatementRunDetailTabs panelId="panel" runId="run-1" tabs={tabs} />);

    await screen.findByText("CASH_MISSING");
    await userEvent.click(screen.getByRole("tab", { name: /Breaks & Cases tab/ }));

    expect(await screen.findByText("Cash balance mismatch")).toBeInTheDocument();
    expect(screen.getByText("-1,250.50")).toBeInTheDocument();
    expect(screen.getByText("STMT-000014")).toBeInTheDocument();
  });

  it("keeps a tab with no route behind it on its descriptive banner", async () => {
    primeReads();
    render(<StatementRunDetailTabs panelId="panel" runId="run-1" tabs={[tab("evidence", "Evidence")]} />);

    expect(await screen.findByText("Evidence description.")).toBeInTheDocument();
  });
});
