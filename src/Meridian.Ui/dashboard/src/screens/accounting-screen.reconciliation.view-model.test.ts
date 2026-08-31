import { describe, expect, it } from "vitest";
import {
  buildOperationalExceptionWorkbenchState,
  buildReconciliationBreakQueueState,
  buildReconciliationBreakRows,
  buildReconciliationComparisonViewState,
  buildReconciliationDetailActions,
  buildReconciliationDetailViewState,
  buildReconciliationNarrative,
  buildReconciliationQueuePanelViewState,
  buildReconciliationResolveDialogState,
  buildReconciliationStatementRunsViewState,
} from "@/screens/accounting-screen.view-model";
import type {
  AccountingSystemReconciliationSummary,
  AccountingWorkspaceResponse,
  ReconciliationBreakQueueItem,
} from "@/types";

const reconciliationQueue: AccountingWorkspaceResponse["reconciliationQueue"] = [
  {
    runId: "run-42",
    strategyName: "Paper Index Mean Reversion",
    mode: "paper",
    status: "Running",
    lastUpdated: "3m ago",
    breakCount: 2,
    openBreakCount: 1,
    reconciliationStatus: "BreaksOpen"
  },
  {
    runId: "run-57",
    strategyName: "Intraday Vol Carry",
    mode: "paper",
    status: "Paused",
    lastUpdated: "7m ago",
    breakCount: 1,
    openBreakCount: 0,
    reconciliationStatus: "Resolved"
  }
];

const breakQueue: ReconciliationBreakQueueItem[] = [
  {
    breakId: "run-42:cash",
    runId: "run-42",
    strategyName: "Paper Index Mean Reversion",
    category: "AmountMismatch",
    status: "Open",
    variance: 500,
    reason: "Cash variance over tolerance.",
    assignedTo: null,
    detectedAt: "2026-01-01T00:00:00Z",
    lastUpdatedAt: "2026-01-01T00:00:00Z",
    reviewedBy: null,
    reviewedAt: null,
    resolvedBy: null,
    resolvedAt: null,
    resolutionNote: null
  },
  {
    breakId: "run-57:fees",
    runId: "run-57",
    strategyName: "Intraday Vol Carry",
    category: "FeeMismatch",
    status: "Resolved",
    variance: 0,
    reason: "Fee variance resolved.",
    assignedTo: "ops.gov",
    detectedAt: "2026-01-02T00:00:00Z",
    lastUpdatedAt: "2026-01-02T00:00:00Z",
    reviewedBy: "ops.gov",
    reviewedAt: "2026-01-02T00:05:00Z",
    resolvedBy: "ops.gov",
    resolvedAt: "2026-01-02T00:10:00Z",
    resolutionNote: "Reviewed in accounting panel.",
    exceptionRoute: "fund-ops-review",
    toleranceProfileId: "fee-variance-ops",
    toleranceBand: 100,
    requiredSignoffRole: "Fund operations lead",
    signoffStatus: "Pending Signoff",
    routingTarget: "FundTrialBalance",
    routingDetail: "Open the accounting trial balance for evidence review.",
    recommendedAction: "Review matched fee entries before closing.",
    breakExplanation: {
      summary: "Provider fees and Meridian ledger fees now match after operator review.",
      sourceSystems: ["Provider activity", "Meridian ledger"],
      probableCause: "The provider posted fees after the first reconciliation pass.",
      ledgerImpact: "No remaining ledger adjustment is required.",
      suggestedNextAction: "Attach the provider activity evidence before close sign-off.",
      evidenceLinks: ["/accounting/evidence/provider-fees"]
    }
  }
];

describe("accounting-screen reconciliation view model", () => {
  it("builds operational exception workbench state from reconciliation queues", () => {
    const breakRows = buildReconciliationBreakRows([
      {
        breakId: "run-42:cash",
        runId: "run-42",
        strategyName: "Paper Index Mean Reversion",
        category: "AmountMismatch",
        status: "Open",
        variance: 500,
        reason: "Cash variance over tolerance.",
        assignedTo: null,
        detectedAt: "2026-01-01T00:00:00Z",
        lastUpdatedAt: "2026-01-01T00:00:00Z",
        reviewedBy: null,
        reviewedAt: null,
        resolvedBy: null,
        resolvedAt: null,
        resolutionNote: null,
        routingTarget: "FundTrialBalance",
        routingDetail: "Open the accounting trial balance for evidence review.",
        recommendedAction: "Review cash ledger entries before resolving.",
        commentCount: 2,
        evidenceCount: 3,
        signoffStatus: "Pending"
      }
    ], null, "run-42:cash");

    const state = buildOperationalExceptionWorkbenchState({
      reconciliationQueue,
      breakRows
    });

    expect(state.title).toBe("Operational exception workbench");
    expect(state.metricRows.find((metric) => metric.id === "active-breaks")).toMatchObject({
      value: "1",
      tone: "warning"
    });
    expect(state.metricRows.find((metric) => metric.id === "comments")?.value).toBe("2");
    expect(state.metricRows.find((metric) => metric.id === "audit-evidence")?.value).toBe("3");
    expect(state.cases[0]).toMatchObject({
      id: "run-42:cash",
      ownerLabel: "Unassigned",
      slaLabel: "Review required · assign owner · set sign-off role",
      routeHref: "/accounting/ledger"
    });
  });

  it("fails exception urgency closed when nominal SLA state lacks ownership, evidence, and sign-off policy", () => {
    const openBreak: ReconciliationBreakQueueItem = {
      ...breakQueue[0],
      slaState: "OnTrack",
      slaBadgeLabel: "OnTrack",
      evidenceCount: 0,
      requiredSignoffRole: null
    };

    const state = buildReconciliationBreakQueueState({
      breakQueue: [openBreak],
      selectedBreakId: openBreak.breakId,
      loading: false,
      loadError: null,
      action: null,
      actionError: null
    });
    const urgency = state.selectedDetail?.fields.find((field) => field.label === "Urgency");

    expect(urgency?.value).toBe("Review required · assign owner · attach evidence · set sign-off role");
    expect(urgency?.value).not.toContain("OnTrack");
  });

  it("derives statement run rows and detail tabs from endpoint supplied counts", () => {
    const state = buildReconciliationStatementRunsViewState({
      statementRuns: [
        {
          runId: "statement-run-1",
          importId: "import-1",
          startedAtUtc: "2026-05-01T00:00:00Z",
          completedAtUtc: "2026-05-01T00:03:00Z",
          positionMatches: 8,
          cashMatches: 3,
          transactionMatches: 13,
          openExceptionCount: 2,
          brokerCustodian: "Northern Trust",
          account: "Fund A - Prime",
          period: "2026-04",
          status: "ReviewRequired",
          validationIssueCount: 4,
          breakCount: 2,
          caseCount: 1,
          importedAtUtc: "2026-05-01T00:04:00Z"
        }
      ],
      fallbackQueue: reconciliationQueue,
      selectedRunId: "statement-run-1",
      loading: false,
      error: null
    });

    expect(state).toMatchObject({
      title: "Statement runs",
      tableLabel: "Accounting statement runs",
      hasRows: true,
      loadingText: null,
      errorText: null
    });
    expect(state.rows[0]).toMatchObject({
      brokerCustodianLabel: "Northern Trust",
      accountLabel: "Fund A - Prime",
      periodLabel: "2026-04",
      statusLabel: "Review required",
      validationIssueCountLabel: "4",
      matchCountLabel: "24",
      breakCountLabel: "2",
      caseCountLabel: "1",
      importedAtLabel: "May 1, 00:04 UTC",
      unavailableReason: null
    });
    expect(state.tabs.map((tab) => tab.label)).toEqual([
      "Overview",
      "Validation",
      "Positions",
      "Cash",
      "Transactions",
      "Breaks & Cases",
      "Evidence"
    ]);
    expect(state.tabs.every((tab) => !tab.disabled)).toBe(true);
  });

  it("reports match counts as not reported on rows derived from the queue", () => {
    // With no statement runs the desk synthesizes rows from the reconciliation queue, which carries
    // no match totals. Printing the placeholder zeros would tell an operator the statement matched
    // nothing, when the truth is that Meridian has no match data for the run.
    const state = buildReconciliationStatementRunsViewState({
      statementRuns: [],
      fallbackQueue: reconciliationQueue,
      selectedRunId: null,
      loading: false,
      error: null
    });

    expect(state.rows.length).toBeGreaterThan(0);
    expect(state.rows[0].matchCountLabel).toBe("—");
    expect(state.rows[0].unavailableReason).toContain("Match counts");
    expect(state.rows[0].ariaLabel).toContain("match counts not reported");
    expect(state.rows[0].ariaLabel).not.toContain("0 matches");
  });

  it("keeps break and case counts on derived rows, since the queue does report those", () => {
    const state = buildReconciliationStatementRunsViewState({
      statementRuns: [],
      fallbackQueue: reconciliationQueue,
      selectedRunId: null,
      loading: false,
      error: null
    });

    expect(state.rows[0].breakCountLabel).not.toBe("—");
    expect(state.rows[0].caseCountLabel).not.toBe("—");
  });

  it("does not credit the reconciliation service for totals it never supplied", () => {
    const state = buildReconciliationStatementRunsViewState({
      statementRuns: [],
      fallbackQueue: reconciliationQueue,
      selectedRunId: null,
      loading: false,
      error: null
    });

    for (const id of ["positions", "cash", "transactions"]) {
      const tab = state.tabs.find((entry) => entry.id === id);
      expect(tab?.badgeLabel).toBeNull();
      expect(tab?.description).toContain("not reported");
      expect(tab?.description).not.toContain("supplied by the reconciliation service");
    }
  });

  it("still shows reported match counts when the service supplies a run", () => {
    const state = buildReconciliationStatementRunsViewState({
      statementRuns: [
        {
          runId: "statement-run-1",
          importId: "import-1",
          startedAtUtc: "2026-05-01T00:00:00Z",
          completedAtUtc: "2026-05-01T00:03:00Z",
          positionMatches: 8,
          cashMatches: 3,
          transactionMatches: 13,
          openExceptionCount: 2
        }
      ],
      fallbackQueue: reconciliationQueue,
      selectedRunId: null,
      loading: false,
      error: null
    });

    expect(state.rows[0].matchCountLabel).toBe("24");
    expect(state.rows[0].unavailableReason ?? "").not.toContain("Match counts");
    expect(state.tabs.find((entry) => entry.id === "positions")?.badgeLabel).toBe("8");
    expect(state.tabs.find((entry) => entry.id === "positions")?.description).toContain(
      "supplied by the reconciliation service"
    );
  });

  it("reports a genuine zero as zero, not as unreported", () => {
    // A service-reported run that truly matched nothing must still read 0 -- the point of the
    // change is to separate that fact from "we do not know".
    const state = buildReconciliationStatementRunsViewState({
      statementRuns: [
        {
          runId: "statement-run-zero",
          importId: "import-zero",
          startedAtUtc: "2026-05-01T00:00:00Z",
          completedAtUtc: "2026-05-01T00:03:00Z",
          positionMatches: 0,
          cashMatches: 0,
          transactionMatches: 0,
          openExceptionCount: 4
        }
      ],
      fallbackQueue: reconciliationQueue,
      selectedRunId: null,
      loading: false,
      error: null
    });

    expect(state.rows[0].matchCountLabel).toBe("0");
    expect(state.rows[0].ariaLabel).toContain("0 matches");
    expect(state.tabs.find((entry) => entry.id === "cash")?.badgeLabel).toBe("0");
  });

  it("selects the first available statement run when the requested run is not in the statement list", () => {
    const state = buildReconciliationStatementRunsViewState({
      statementRuns: [
        {
          runId: "statement-run-1",
          importId: "import-1",
          startedAtUtc: "2026-05-01T00:00:00Z",
          completedAtUtc: "2026-05-01T00:03:00Z",
          positionMatches: 8,
          cashMatches: 3,
          transactionMatches: 13,
          openExceptionCount: 2,
          brokerCustodian: "Northern Trust",
          account: "Fund A - Prime",
          period: "2026-04",
          status: "ReviewRequired"
        }
      ],
      fallbackQueue: reconciliationQueue,
      selectedRunId: "run-42",
      loading: false,
      error: null
    });

    expect(state.rows[0]).toMatchObject({
      runId: "statement-run-1",
      isSelected: true
    });
    expect(state.tabs.every((tab) => !tab.disabled)).toBe(true);
    expect(state.tabs[0].ariaLabel).toContain("statement run statement-run-1");
  });

  it("orders statement runs by the latest retained service timestamp", () => {
    const state = buildReconciliationStatementRunsViewState({
      statementRuns: [
        {
          runId: "older-run",
          importId: "older-import",
          startedAtUtc: "2026-05-01T00:00:00Z",
          completedAtUtc: "2026-05-01T00:03:00Z",
          importedAtUtc: "2026-05-01T00:04:00Z",
          positionMatches: 1,
          cashMatches: 0,
          transactionMatches: 0,
          openExceptionCount: 0
        },
        {
          runId: "newer-run",
          importId: "newer-import",
          startedAtUtc: "2026-05-02T00:00:00Z",
          completedAtUtc: "2026-05-02T00:03:00Z",
          importedAtUtc: "2026-05-02T00:04:00Z",
          positionMatches: 1,
          cashMatches: 0,
          transactionMatches: 0,
          openExceptionCount: 0
        }
      ],
      fallbackQueue: [],
      selectedRunId: null,
      loading: false,
      error: null
    });

    expect(state.rows.map((row) => row.runId)).toEqual(["newer-run", "older-run"]);
    expect(state.rows[0].isSelected).toBe(true);
  });

  it("derives the compact reconciliation comparison from statement and cash-flow evidence", () => {
    const state = buildReconciliationComparisonViewState({
      statementRuns: [
        {
          runId: "statement-run-1",
          importId: "import-1",
          startedAtUtc: "2026-05-01T00:00:00Z",
          completedAtUtc: "2026-05-01T00:03:00Z",
          positionMatches: 8,
          cashMatches: 3,
          transactionMatches: 13,
          openExceptionCount: 2,
          brokerCustodian: "Northern Trust",
          account: "Fund A - Prime",
          period: "2026-04",
          status: "ReviewRequired",
          validationIssueCount: 4,
          breakCount: 2,
          caseCount: 1,
          importedAtUtc: "2026-05-01T00:04:00Z"
        }
      ],
      fallbackQueue: reconciliationQueue,
      selectedRunId: "statement-run-1",
      cashFlow: {
        totalCash: 120000,
        totalLedgerCash: 120500,
        netVariance: 500,
        totalFinancing: 1400,
        runsWithCashSignals: 4,
        runsWithCashVariance: 1,
        tone: "warning",
        summary: "Cash-flow coverage is available for 4 runs; 1 run needs variance review."
      }
    });

    expect(state).toMatchObject({
      title: "Cash reconciliation - broker statement vs. ledger",
      statementHeading: "Statement",
      ledgerHeading: "Ledger",
      matchedBadgeLabel: "24 matched",
      openBadgeLabel: "2 open",
      statementBalanceLabel: "$120,000",
      ledgerBalanceLabel: "$120,500",
      varianceLabel: "Out by $500",
      varianceTone: "warning"
    });
    expect(state.rows[0]).toMatchObject({
      statementTitle: "Northern Trust",
      statementValue: "$120,000",
      ledgerValue: "$120,500",
      statusTone: "warning"
    });
    expect(state.lineSource).toBe("runs");
    expect(state.statementLines).toHaveLength(state.rows.length);
    expect(state.statementLines[0]).toMatchObject({
      matchKey: "statement-run-1",
      title: "Northern Trust",
      amountLabel: "$120,000",
      statusTone: "warning"
    });
    expect(state.ledgerLines[0].matchKey).toBe("statement-run-1");
  });

  it("drives the reconciliation comparison from transaction-level external GL detail when loaded", () => {
    const systemReconciliation: AccountingSystemReconciliationSummary = {
      reconciliationId: "gl-recon-txn",
      importId: "gl-import-txn",
      providerId: "quickbooks-fixture",
      fundProfileId: "fund-alpha",
      periodStart: "2026-06-01",
      periodEnd: "2026-06-30",
      generatedAtUtc: "2026-07-01T00:00:00Z",
      matchedCount: 1,
      breakCount: 3,
      totalExternalDebits: 1000,
      totalExternalCredits: 200,
      totalMeridianDebits: 700,
      totalMeridianCredits: 100,
      postingEnabled: false,
      postingDisabledReason: "Open breaks remain.",
      evidenceReferences: [],
      rows: [
        { rowId: "r-matched", accountCode: "1100", accountName: "Cash", currency: "USD", status: "Matched", externalDebit: 500, externalCredit: 0, meridianDebit: 500, meridianCredit: 0, variance: 0, detail: "Settlement credit.", evidenceRef: null },
        { rowId: "r-variance", accountCode: "6000", accountName: "Fees", currency: "EUR", status: "Variance", externalDebit: 0, externalCredit: 200, meridianDebit: 0, meridianCredit: 180, variance: 20, detail: "Commission variance.", evidenceRef: null },
        { rowId: "r-review", accountCode: "1500", accountName: "Custody", currency: "USD", status: "ReviewRequired", externalDebit: 850, externalCredit: 0, meridianDebit: 850, meridianCredit: 0, variance: 0, detail: "Custody fee timing.", evidenceRef: null },
        { rowId: "r-miss-ext", accountCode: "7000", accountName: "Mgmt fee payable", currency: "USD", status: "MissingExternal", externalDebit: 0, externalCredit: 0, meridianDebit: 300, meridianCredit: 0, variance: -300, detail: "Ledger-only accrual.", evidenceRef: null },
        { rowId: "r-miss-mer", accountCode: "4000", accountName: "Interest", currency: "USD", status: "MissingMeridian", externalDebit: 142, externalCredit: 0, meridianDebit: 0, meridianCredit: 0, variance: 142, detail: "Statement-only interest.", evidenceRef: null }
      ]
    };

    const state = buildReconciliationComparisonViewState({
      statementRuns: [],
      fallbackQueue: reconciliationQueue,
      selectedRunId: null,
      cashFlow: null,
      systemReconciliation
    });

    expect(state.lineSource).toBe("transactions");
    expect(state.statementHeading).toBe("Custodian statement");
    expect(state.ledgerHeading).toBe("Internal ledger");

    // Statement side drops the ledger-only (MissingExternal) row; ledger side drops the statement-only (MissingMeridian) row.
    expect(state.statementLines.map((line) => line.matchKey)).toEqual(["r-matched", "r-variance", "r-review", "r-miss-mer"]);
    expect(state.ledgerLines.map((line) => line.matchKey)).toEqual(["r-matched", "r-variance", "r-review", "r-miss-ext"]);

    expect(state.statementLines[0]).toMatchObject({ id: "r-matched:statement", title: "Cash", statusLabel: "Matched", statusTone: "success" });
    expect(state.statementLines.find((line) => line.matchKey === "r-review")?.statusTone).toBe("warning");
    expect(state.statementLines.find((line) => line.matchKey === "r-miss-mer")).toMatchObject({ statusLabel: "Missing in ledger", statusTone: "danger" });
    expect(state.ledgerLines.find((line) => line.matchKey === "r-miss-ext")).toMatchObject({ statusLabel: "Missing in statement", statusTone: "danger" });

    // Line amounts are currency-aware per row — the EUR row renders with the euro symbol, not USD.
    expect(state.statementLines[0].amountLabel).toBe("$500");
    expect(state.statementLines.find((line) => line.matchKey === "r-variance")?.amountLabel).toBe("-€200");

    // Summary and badges follow the system reconciliation totals.
    expect(state.matchedBadgeLabel).toBe("1 matched");
    expect(state.openBadgeLabel).toBe("3 open");
    expect(state.statementBalanceLabel).toBe("$800");
    expect(state.ledgerBalanceLabel).toBe("$600");
    expect(state.varianceLabel).toBe("Out by $200");
    expect(state.varianceTone).toBe("warning");
  });

  it("does not report balanced when a transaction-level reconciliation has open breaks despite tied totals", () => {
    const systemReconciliation: AccountingSystemReconciliationSummary = {
      reconciliationId: "gl-recon-tb",
      importId: "gl-import-tb",
      providerId: "quickbooks-fixture",
      fundProfileId: "fund-alpha",
      periodStart: "2026-06-01",
      periodEnd: "2026-06-30",
      generatedAtUtc: "2026-07-01T00:00:00Z",
      matchedCount: 0,
      breakCount: 2,
      totalExternalDebits: 1000,
      totalExternalCredits: 1000,
      totalMeridianDebits: 1000,
      totalMeridianCredits: 1000,
      postingEnabled: false,
      postingDisabledReason: "Open breaks remain.",
      evidenceReferences: [],
      rows: [
        { rowId: "tb-1", accountCode: "1100", accountName: "Cash", currency: "USD", status: "Variance", externalDebit: 500, externalCredit: 0, meridianDebit: 480, meridianCredit: 0, variance: 20, detail: "Cash variance.", evidenceRef: null },
        { rowId: "tb-2", accountCode: "2000", accountName: "Payables", currency: "USD", status: "Variance", externalDebit: 0, externalCredit: 500, meridianDebit: 0, meridianCredit: 480, variance: -20, detail: "Payables variance.", evidenceRef: null }
      ]
    };

    const state = buildReconciliationComparisonViewState({
      statementRuns: [],
      fallbackQueue: reconciliationQueue,
      selectedRunId: null,
      cashFlow: null,
      systemReconciliation
    });

    // Debit/credit totals tie out (net variance 0) but two breaks remain — must not read "Balanced".
    expect(state.varianceLabel).toBe("2 open breaks");
    expect(state.varianceTone).toBe("warning");
  });

  it("keeps the comparison summary on the run-level source when the GL reconciliation has no row detail", () => {
    const systemReconciliation: AccountingSystemReconciliationSummary = {
      reconciliationId: "gl-recon-empty",
      importId: "gl-import-empty",
      providerId: "quickbooks-fixture",
      fundProfileId: "fund-alpha",
      periodStart: "2026-06-01",
      periodEnd: "2026-06-30",
      generatedAtUtc: "2026-07-01T00:00:00Z",
      matchedCount: 9,
      breakCount: 0,
      totalExternalDebits: 99999,
      totalExternalCredits: 0,
      totalMeridianDebits: 99999,
      totalMeridianCredits: 0,
      postingEnabled: false,
      postingDisabledReason: "No row detail.",
      evidenceReferences: [],
      rows: []
    };

    const state = buildReconciliationComparisonViewState({
      statementRuns: [],
      fallbackQueue: reconciliationQueue,
      selectedRunId: null,
      cashFlow: {
        totalCash: 120000,
        totalLedgerCash: 120500,
        netVariance: 500,
        totalFinancing: 1400,
        runsWithCashSignals: 4,
        runsWithCashVariance: 1,
        tone: "warning",
        summary: "Cash-flow coverage is available."
      },
      systemReconciliation
    });

    // No GL row detail => panes show run-level lines, so the summary must stay on the run-level source too.
    expect(state.lineSource).toBe("runs");
    expect(state.statementBalanceLabel).toBe("$120,000");
    expect(state.ledgerBalanceLabel).toBe("$120,500");
    expect(state.varianceLabel).toBe("Out by $500");
  });

  it("derives reconciliation detail queue row state and empty inspector copy", () => {
    const state = buildReconciliationQueuePanelViewState(reconciliationQueue, "run-57");

    expect(state).toMatchObject({
      title: "Reconciliation detail queue",
      overviewTitle: "Reconciliation queue",
      overviewCaption: "Read-only reconciliation queue summary. Open the reconciliation workstream to inspect selected run detail.",
      overviewActionHref: "/accounting/reconciliation",
      overviewActionLabel: "Open reconciliation",
      overviewActionAriaLabel: "Open Accounting reconciliation workstream",
      listLabel: "Reconciliation runs",
      detailPanelId: "reconciliation-run-detail-panel",
      hasRows: true
    });
    expect(state.rows[0]).toMatchObject({
      runId: "run-42",
      isSelected: false,
      isExpanded: false,
      controlsId: "reconciliation-run-detail-panel",
      reconciliationTone: "warning",
      openBreakLabel: "1 open",
      selectAriaLabel: "Inspect reconciliation run Paper Index Mean Reversion"
    });
    expect(state.rows[1]).toMatchObject({
      runId: "run-57",
      isSelected: true,
      isExpanded: true,
      reconciliationTone: "primary",
      openBreakLabel: "0 open"
    });

    const emptyState = buildReconciliationQueuePanelViewState([], null);
    expect(emptyState.hasRows).toBe(false);
    expect(emptyState.rows).toEqual([]);
    expect(emptyState.detailEmptyAriaLabel).toBe("No reconciliation run selected");
  });

  it("derives reconciliation break action state and live announcements", () => {
    const rows = buildReconciliationBreakRows(breakQueue, { breakId: "run-42:cash", command: "assign" });

    expect(rows[0]).toMatchObject({
      breakId: "run-42:cash",
      actionBusy: true,
      varianceLabel: "+$500.00",
      varianceTone: "success",
      statusBadgeVariant: "danger",
      ownerLabel: "Unassigned",
      rowSelectAriaLabel: "Inspect reconciliation break run-42:cash",
      assignLabel: "Assigning...",
      canAssign: false,
      canResolve: false,
      canDismiss: false
    });
    expect(rows[1]).toMatchObject({
      breakId: "run-57:fees",
      resolveLabel: "Resolve",
      canAssign: false,
      canResolve: false,
      canDismiss: false
    });

    const state = buildReconciliationBreakQueueState({
      breakQueue,
      selectedBreakId: "run-57:fees",
      loading: false,
      loadError: null,
      action: { breakId: "run-42:cash", command: "assign" },
      actionError: null
    });

    expect(state.hasBreaks).toBe(true);
    expect(state.tableLabel).toBe("Reconciliation break queue");
    expect(state.selectedBreakId).toBe("run-57:fees");
    expect(state.selectedDetail).toMatchObject({
      id: "reconciliation-break-detail-panel",
      title: "Intraday Vol Carry - Cash variance needs review",
      statusLabel: "Resolved",
      statusBadgeVariant: "success",
      analysisText: "Provider fees and Meridian ledger fees now match after operator review.",
      recommendedActionText: "Attach the provider activity evidence before close sign-off.",
      routingActionLabel: "Open routing target",
      routingActionHref: "/accounting/ledger",
      routingActionAriaLabel: "Open routing target for reconciliation break run-57:fees"
    });
    expect(state.selectedDetail?.fields).toEqual(expect.arrayContaining([
      { label: "Detected", value: "Jan 2, 00:00 UTC" },
      { label: "Updated", value: "Jan 2, 00:00 UTC" },
      { label: "Exception route", value: "fund-ops-review" },
      { label: "Tolerance profile", value: "fee-variance-ops" },
      { label: "Tolerance band", value: "$100" },
      {
        label: "Required sign-off",
        value: "Decision captured; sign-off: Pending Signoff by Fund operations lead. Close approval remains blocked."
      },
      { label: "Decision note", value: "Reviewed in accounting panel." },
      { label: "Explanation summary", value: "Provider fees and Meridian ledger fees now match after operator review." },
      { label: "Source systems", value: "Provider activity, Meridian ledger" },
      { label: "Probable cause", value: "The provider posted fees after the first reconciliation pass." },
      { label: "Ledger impact", value: "No remaining ledger adjustment is required." },
      { label: "Suggested next action", value: "Attach the provider activity evidence before close sign-off." },
      { label: "Explanation evidence", value: "/accounting/evidence/provider-fees" }
    ]));
    expect(state.rows[1]).toMatchObject({
      isSelected: true,
      isExpanded: true,
      detailPanelId: "reconciliation-break-detail-panel"
    });
    expect(state.statusAnnouncement).toBe("Assigning reconciliation break run-42:cash.");
  });

  it("derives reconciliation empty and failure copy", () => {
    const empty = buildReconciliationBreakQueueState({
      breakQueue: [],
      loading: false,
      loadError: null,
      action: null,
      actionError: null
    });

    expect(empty.hasBreaks).toBe(false);
    expect(empty.selectedDetail).toBeNull();
    expect(empty.detailEmptyAriaLabel).toBe("No reconciliation break selected");
    expect(empty.emptyText).toBe("No reconciliation breaks in the current queue.");
    expect(empty.statusAnnouncement).toBe("No reconciliation breaks in the current queue.");

    const failed = buildReconciliationBreakQueueState({
      breakQueue,
      loading: false,
      loadError: "Provider offline",
      action: null,
      actionError: "Review endpoint rejected"
    });

    expect(failed.errorText).toBe("Reconciliation break queue failed: Provider offline");
    expect(failed.actionErrorText).toBe("Break action failed: Review endpoint rejected");
    expect(failed.statusAnnouncement).toBe("Break action failed: Review endpoint rejected");
  });

  it("derives reconciliation resolve dialog labels and validation state", () => {
    const blankResolve = buildReconciliationResolveDialogState("run-42:cash", "Resolved", "  ");

    expect(blankResolve).toMatchObject({
      breakId: "run-42:cash",
      status: "Resolved",
      inputId: "rationale-run-42:cash",
      helpId: "rationale-help-run-42:cash",
      formAriaLabel: "Resolve reconciliation break run-42:cash",
      label: "Resolve rationale",
      submitLabel: "Confirm resolve",
      submitAriaLabel: "Confirm resolve for reconciliation break run-42:cash",
      cancelLabel: "Cancel",
      cancelAriaLabel: "Cancel resolve for reconciliation break run-42:cash",
      isSubmitDisabled: true
    });

    const dismiss = buildReconciliationResolveDialogState("run-42:cash", "Dismissed", "Reviewed duplicate break");

    expect(dismiss).toMatchObject({
      label: "Dismiss rationale",
      placeholder: "Describe why this break is being dismissed...",
      submitLabel: "Confirm dismiss",
      isSubmitDisabled: false
    });
  });

  it("keeps reconciliation narratives in the view model", () => {
    expect(buildReconciliationNarrative(reconciliationQueue[0])).toContain("Open reconciliation breaks remain");
    expect(buildReconciliationNarrative({ ...reconciliationQueue[0], reconciliationStatus: "Balanced" })).toContain("currently balanced");
    expect(buildReconciliationNarrative({ ...reconciliationQueue[0], reconciliationStatus: "NotStarted" })).toContain("Accounting review work");
    expect(buildReconciliationNarrative({ ...reconciliationQueue[0], reconciliationStatus: "NotStarted" })).not.toContain("governance review work");
  });

  it("derives reconciliation detail presentation state", () => {
    expect(buildReconciliationDetailViewState(reconciliationQueue[0])).toMatchObject({
      eyebrow: "Reconciliation detail",
      title: "Paper Index Mean Reversion",
      description: "Current reconciliation status: Breaks open.",
      ariaLabel: "Reconciliation detail for Paper Index Mean Reversion",
      narrativeLabel: "Reconciliation narrative for Paper Index Mean Reversion",
      fields: [
        { label: "Mode", value: "PAPER", tone: "default", ariaLabel: "Mode: PAPER" },
        { label: "Run status", value: "Running", tone: "default", ariaLabel: "Run status: Running" },
        { label: "Break count", value: "2", tone: "default", ariaLabel: "Break count: 2" },
        { label: "Open breaks", value: "1", tone: "warning", ariaLabel: "Open breaks: 1" },
        { label: "Last updated", value: "3m ago", tone: "default", ariaLabel: "Last updated: 3m ago" }
      ]
    });
  });

  it("derives reconciliation detail actions from the selected run", () => {
    expect(buildReconciliationDetailActions(reconciliationQueue[0])).toEqual({
      breakChecklistTargetId: "reconciliation-break-queue",
      breakChecklistHref: "#reconciliation-break-queue",
      breakChecklistLabel: "Open break checklist",
      breakChecklistAriaLabel: "Open break checklist for Paper Index Mean Reversion; 1 open break",
      evidencePacketHref: "/reporting/evidence?subjectKind=reconciliation-review&subjectId=run-42",
      evidencePacketLabel: "Evidence packet",
      evidencePacketAriaLabel: "Open reconciliation evidence packet for Paper Index Mean Reversion",
      auditPacketHref: "/api/workstation/runs/run-42/review-packet",
      auditPacketLabel: "Review audit packet",
      auditPacketAriaLabel: "Review audit packet for Paper Index Mean Reversion"
    });
  });

});
