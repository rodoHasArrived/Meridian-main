import { describe, expect, it } from "vitest";
import {
  buildStatementRunBreakRow,
  buildStatementRunBreaksViewModel,
  buildStatementRunReconcileAction,
  buildStatementRunValidationRow,
  buildStatementRunValidationViewModel
} from "@/screens/accounting-screen.statement-run-detail.view-model";
import type { StatementRunBreak, StatementValidationIssue } from "@/types/statement-run-detail.types";

function issue(overrides: Partial<StatementValidationIssue> = {}): StatementValidationIssue {
  return {
    issueId: "issue-1",
    severity: 2,
    code: "CASH_MISSING",
    message: "Statement cash row has no book counterpart.",
    sourceRowNumber: 14,
    sourceColumn: "cashBalance",
    ...overrides
  };
}

function breakRow(overrides: Partial<StatementRunBreak> = {}): StatementRunBreak {
  return {
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
    status: "Open",
    ...overrides
  };
}

describe("statement run validation view model", () => {
  it("reports an unloaded run without inventing a clean result", () => {
    const view = buildStatementRunValidationViewModel(null);

    expect(view.loaded).toBe(false);
    expect(view.countLabel).toBe("—");
    expect(view.blocked).toBe(false);
    expect(view.emptyState).toContain("has not loaded");
  });

  it("restates the server's blocked verdict rather than deriving one", () => {
    // Severities here are non-blocking, but the server said blocked; the server wins.
    const view = buildStatementRunValidationViewModel({
      runId: "run-1",
      issues: [issue({ severity: 0 })],
      isBlocked: true
    });

    expect(view.blocked).toBe(true);
    expect(view.blockedNotice).toContain("blocked by validation");
  });

  it("resolves severity ordinals through the transcribed enum map", () => {
    expect(buildStatementRunValidationRow(issue({ severity: 0 })).severityLabel).toBe("Info");
    expect(buildStatementRunValidationRow(issue({ severity: 1 })).severityTone).toBe("warning");
    expect(buildStatementRunValidationRow(issue({ severity: 3 })).severityTone).toBe("danger");
  });

  it("names an ordinal the map does not cover instead of guessing a neighbour", () => {
    const row = buildStatementRunValidationRow(issue({ severity: 9 }));

    expect(row.severityLabel).toBe("Unrecognized severity 9");
    expect(row.severityTone).toBe("default");
  });

  it("says a severity was not reported when the contract omits it", () => {
    expect(buildStatementRunValidationRow(issue({ severity: null })).severityLabel).toBe("Severity not reported");
  });

  it("keeps row and column together when both identify the source", () => {
    expect(buildStatementRunValidationRow(issue()).sourceLabel).toBe("Row 14, cashBalance");
    expect(buildStatementRunValidationRow(issue({ sourceColumn: null })).sourceLabel).toBe("Row 14");
    expect(
      buildStatementRunValidationRow(issue({ sourceRowNumber: null, sourceColumn: null })).sourceLabel
    ).toBe("Source not identified");
  });

  it("falls back to a stable id when the contract supplies none", () => {
    expect(buildStatementRunValidationRow(issue({ issueId: null }), 3).issueId).toBe("CASH_MISSING-3");
  });
});

describe("statement run breaks view model", () => {
  it("distinguishes not-loaded from a run with no breaks", () => {
    expect(buildStatementRunBreaksViewModel(null).loaded).toBe(false);
    expect(buildStatementRunBreaksViewModel(null).countLabel).toBe("—");

    const empty = buildStatementRunBreaksViewModel([]);
    expect(empty.loaded).toBe(true);
    expect(empty.countLabel).toBe("0");
    expect(empty.emptyState).toBe("This run produced no breaks.");
  });

  it("surfaces the breached subset in the count rather than only the total", () => {
    const view = buildStatementRunBreaksViewModel([
      breakRow(),
      breakRow({ breakId: "break-2", toleranceBreached: false, delta: 5 })
    ]);

    expect(view.countLabel).toBe("2 (1 over tolerance)");
    expect(view.breachedCount).toBe(1);
  });

  it("resolves break-type ordinals and signs the delta", () => {
    const row = buildStatementRunBreakRow(breakRow());

    expect(row.typeLabel).toBe("Cash balance mismatch");
    expect(row.deltaLabel).toBe("-1,250.50");
    expect(row.toleranceTone).toBe("danger");
    expect(row.toleranceNote).toContain("exceeds the 100.00 tolerance band");
  });

  it("marks an unrecognized break type with its ordinal", () => {
    expect(buildStatementRunBreakRow(breakRow({ breakType: 99 })).typeLabel).toBe("Unrecognized break type 99");
  });

  it("labels blank contract strings instead of rendering an empty cell", () => {
    const row = buildStatementRunBreakRow(breakRow({ category: "  ", sourceReference: "", status: "" }));

    expect(row.category).toBe("Uncategorized");
    expect(row.sourceReference).toBe("No source reference");
    expect(row.status).toBe("Unknown");
  });
});

describe("statement run reconcile action", () => {
  const base = { runId: "run-1", forbidden: false, inFlight: false, blockedByValidation: false, lastAcknowledgement: null };

  it("is enabled for a selected, unblocked run", () => {
    const action = buildStatementRunReconcileAction(base);

    expect(action.enabled).toBe(true);
    expect(action.disabledReason).toBeNull();
  });

  it("requires a selected run", () => {
    const action = buildStatementRunReconcileAction({ ...base, runId: null });

    expect(action.enabled).toBe(false);
    expect(action.disabledReason).toContain("Select a statement run");
  });

  it("only cites permission once the server has actually declined", () => {
    expect(buildStatementRunReconcileAction(base).disabledReason).toBeNull();
    expect(buildStatementRunReconcileAction({ ...base, forbidden: true }).disabledReason)
      .toContain("does not hold reconciliation mutation permission");
  });

  it("blocks a re-run while validation is blocking the run", () => {
    const action = buildStatementRunReconcileAction({ ...base, blockedByValidation: true });

    expect(action.enabled).toBe(false);
    expect(action.disabledReason).toContain("Clear the blocking issues");
  });

  it("reports no outcome until a reconcile round-trip has returned", () => {
    expect(buildStatementRunReconcileAction(base).lastOutcome).toBeNull();
    expect(
      buildStatementRunReconcileAction({
        ...base,
        lastAcknowledgement: { runId: "run-1", status: 7, completedAtUtc: "2026-08-26T13:00:00Z" }
      }).lastOutcome
    ).toBe("Matching returned Completed, completed 2026-08-26T13:00:00Z.");
  });

  it("does not claim a completion time the acknowledgement omitted", () => {
    expect(
      buildStatementRunReconcileAction({ ...base, lastAcknowledgement: { runId: "run-1", status: 5 } }).lastOutcome
    ).toBe("Matching returned Reconciling.");
  });
});
