import { act, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import * as detailApi from "@/lib/api/statement-run-detail.api";
import { StatementRunDetailTabs } from "@/screens/accounting-screen.statement-run-detail";
import type { ReconciliationRunDetailTabViewModel } from "@/screens/accounting-screen.view-model";
import type { StatementRunValidation } from "@/types/statement-run-detail.types";

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

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason?: unknown) => void;
  const promise = new Promise<T>((promiseResolve, promiseReject) => {
    resolve = promiseResolve;
    reject = promiseReject;
  });

  return { promise, resolve, reject };
}

function validationFor(runId: string, isBlocked: boolean, code: string): StatementRunValidation {
  return {
    runId,
    isBlocked,
    issues: [
      {
        issueId: `issue-${runId}`,
        severity: 2,
        code,
        message: `${code} raised on ${runId}.`,
        sourceRowNumber: 1,
        sourceColumn: "cashBalance"
      }
    ]
  };
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

  it("ignores a late validation response for a previously selected run", async () => {
    const runA = deferred<StatementRunValidation>();
    const runB = deferred<StatementRunValidation>();
    api.getStatementRunValidation.mockImplementation((runId) => (runId === "run-a" ? runA.promise : runB.promise));
    api.getStatementRunBreaks.mockResolvedValue([]);

    const { rerender } = render(<StatementRunDetailTabs panelId="panel" runId="run-a" tabs={tabs} />);
    rerender(<StatementRunDetailTabs panelId="panel" runId="run-b" tabs={tabs} />);

    // The selected run answers first and is blocked.
    await act(async () => {
      runB.resolve(validationFor("run-b", true, "RUN_B_BLOCKED"));
    });
    expect(await screen.findByText("RUN_B_BLOCKED")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Re-run reconciliation matching/ })).toBeDisabled();

    // The earlier run's clean verdict lands afterwards and must not unblock run B.
    await act(async () => {
      runA.resolve(validationFor("run-a", false, "RUN_A_CLEAN"));
    });

    expect(screen.queryByText("RUN_A_CLEAN")).not.toBeInTheDocument();
    expect(screen.getByText("RUN_B_BLOCKED")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Re-run reconciliation matching/ })).toBeDisabled();
    expect(api.reconcileStatementRun).not.toHaveBeenCalled();
  });

  it("drops the previous run's validation as soon as another run is selected", async () => {
    const runB = deferred<StatementRunValidation>();
    api.getStatementRunValidation.mockImplementation((runId) =>
      runId === "run-a" ? Promise.resolve(validationFor("run-a", true, "RUN_A_BLOCKED")) : runB.promise);
    api.getStatementRunBreaks.mockResolvedValue([]);

    const { rerender } = render(<StatementRunDetailTabs panelId="panel" runId="run-a" tabs={tabs} />);
    expect(await screen.findByText("RUN_A_BLOCKED")).toBeInTheDocument();

    rerender(<StatementRunDetailTabs panelId="panel" runId="run-b" tabs={tabs} />);

    // Run B has not answered yet: run A's rows and blocked verdict are not run B's.
    expect(screen.queryByText("RUN_A_BLOCKED")).not.toBeInTheDocument();
    expect(screen.queryByText(/Reconciliation is blocked by validation/)).not.toBeInTheDocument();

    await act(async () => {
      runB.resolve(validationFor("run-b", false, "RUN_B_CLEAN"));
    });
    expect(await screen.findByText("RUN_B_CLEAN")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Re-run reconciliation matching/ })).toBeEnabled();
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
