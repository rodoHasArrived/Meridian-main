import { act, renderHook, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import {
  buildCalibrationSummaryViewState,
  buildCorporateActionsViewState,
  buildGovernanceCashFlowViewState,
  buildGovernanceLoadingViewState,
  buildGovernanceReportingViewState,
  buildGovernanceTrialBalanceViewState,
  formatReportingExportResult,
  buildReconciliationBreakQueueState,
  buildReconciliationBreakRows,
  buildReconciliationDetailActions,
  buildReconciliationQueuePanelViewState,
  buildReconciliationDetailViewState,
  buildReconciliationNarrative,
  buildReconciliationResolveDialogState,
  buildSecurityConflictRows,
  buildSecurityConflictRefreshCommand,
  buildSecurityIdentityDrillInState,
  buildSecurityMasterPageViewState,
  buildSecuritySearchResultRows,
  buildSecuritySearchState,
  countOpenSecurityConflicts,
  resolveGovernanceWorkstream,
  resolveSelectedReconciliation,
  useSecurityMasterViewModel
} from "@/screens/governance-screen.view-model";
import type {
  SecurityMasterDrillInServices,
  SecurityMasterServices
} from "@/screens/governance-screen.view-model";
import type {
  CorporateAction,
  GovernanceCashFlowSummary,
  GovernanceWorkspaceResponse,
  LedgerTrialBalanceLine,
  ReconciliationCalibrationSummary,
  ReconciliationBreakQueueItem,
  SecurityMasterConflict,
  SecurityMasterEntry,
  SecurityIdentityDrillIn,
  TradingParameters
} from "@/types";

const reconciliationQueue: GovernanceWorkspaceResponse["reconciliationQueue"] = [
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

const securityResult: SecurityMasterEntry = {
  securityId: "sec-1",
  displayName: "Apple Inc.",
  status: "Active",
  classification: {
    assetClass: "Equity",
    subType: "CommonStock",
    primaryIdentifierKind: "Ticker",
    primaryIdentifierValue: "AAPL"
  },
  economicDefinition: {
    currency: "USD",
    version: 3,
    effectiveFrom: "2024-01-01T00:00:00Z",
    effectiveTo: null,
    subType: "CommonStock",
    assetFamily: "Equity",
    issuerType: "Corporate"
  }
};

const securityIdentity: SecurityIdentityDrillIn = {
  securityId: "sec-1",
  displayName: "Apple Inc.",
  assetClass: "Equity",
  status: "Active",
  version: 3,
  effectiveFrom: "2024-01-01T00:00:00Z",
  effectiveTo: null,
  identifiers: [
    {
      kind: "Ticker",
      value: "AAPL",
      isPrimary: true,
      validFrom: "2024-01-01T00:00:00Z",
      validTo: null,
      provider: "Bloomberg"
    }
  ],
  aliases: [
    {
      aliasId: "alias-1",
      securityId: "sec-1",
      aliasKind: "ProviderSymbol",
      aliasValue: "AAPL.OQ",
      provider: null,
      scope: "Collector",
      reason: "Market data source mapping",
      createdBy: "ops.gov",
      createdAt: "2025-01-01T00:00:00Z",
      validFrom: "2025-01-01T00:00:00Z",
      validTo: null,
      isEnabled: true
    }
  ]
};

const tradingParameters: TradingParameters = {
  securityId: "sec-1",
  lotSize: 100,
  tickSize: 0.01,
  contractMultiplier: 1,
  marginRequirementPct: 25,
  tradingHoursUtc: "14:30-21:00",
  circuitBreakerThresholdPct: 7,
  asOf: "2026-05-10T00:00:00Z"
};

const corporateActions: CorporateAction[] = [
  {
    corpActId: "ca-div-1",
    securityId: "sec-1",
    eventType: "Dividend",
    exDate: "2026-05-01T00:00:00Z",
    payDate: "2026-05-15T00:00:00Z",
    dividendPerShare: 0.24,
    currency: "USD",
    splitRatio: null,
    newSecurityId: null,
    distributionRatio: null,
    acquirerSecurityId: null,
    exchangeRatio: null,
    subscriptionPricePerShare: null,
    rightsPerShare: null
  },
  {
    corpActId: "ca-split-1",
    securityId: "sec-1",
    eventType: "StockSplit",
    exDate: "2026-06-01T00:00:00Z",
    payDate: null,
    dividendPerShare: null,
    currency: null,
    splitRatio: 4,
    newSecurityId: null,
    distributionRatio: null,
    acquirerSecurityId: null,
    exchangeRatio: null,
    subscriptionPricePerShare: null,
    rightsPerShare: null
  }
];

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason?: unknown) => void;
  const promise = new Promise<T>((promiseResolve, promiseReject) => {
    resolve = promiseResolve;
    reject = promiseReject;
  });

  return { promise, resolve, reject };
}

function createSecurityMasterServices(overrides: Partial<SecurityMasterServices> = {}): SecurityMasterServices {
  return {
    search: vi.fn().mockResolvedValue([]),
    getIdentity: vi.fn().mockResolvedValue(securityIdentity),
    getConflicts: vi.fn().mockResolvedValue([]),
    resolveConflict: vi.fn().mockResolvedValue(conflicts[0]),
    ...overrides
  };
}

function createSecurityMasterDrillInServices(
  overrides: Partial<SecurityMasterDrillInServices> = {}
): SecurityMasterDrillInServices {
  return {
    getCorporateActions: vi.fn().mockResolvedValue([] as CorporateAction[]),
    getTradingParameters: vi.fn().mockResolvedValue(tradingParameters),
    ...overrides
  };
}

const conflicts: SecurityMasterConflict[] = [
  {
    conflictId: "conflict-1",
    securityId: "sec-1",
    conflictKind: "IdentifierCollision",
    fieldPath: "identifiers.CUSIP",
    providerA: "Bloomberg",
    valueA: "sec-1",
    providerB: "Refinitiv",
    valueB: "sec-2",
    detectedAt: "2026-01-01T00:00:00Z",
    status: "Open"
  },
  {
    conflictId: "conflict-2",
    securityId: "sec-3",
    conflictKind: "IdentifierCollision",
    fieldPath: "identifiers.ISIN",
    providerA: "Bloomberg",
    valueA: "sec-3",
    providerB: "FactSet",
    valueB: "sec-3",
    detectedAt: "2026-01-02T00:00:00Z",
    status: "Resolved"
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
    resolutionNote: "Reviewed in governance panel."
  }
];

const trialBalanceLines: LedgerTrialBalanceLine[] = [
  {
    accountName: "Cash",
    accountType: "Asset",
    symbol: null,
    financialAccountId: "acct-cash",
    balance: 120500,
    entryCount: 12,
    security: null
  },
  {
    accountName: "Financing payable",
    accountType: "Liability",
    symbol: null,
    financialAccountId: "acct-financing",
    balance: -500,
    entryCount: 2,
    security: null
  }
];

describe("governance-screen view model", () => {
  it("derives the governance workstream and selected reconciliation run", () => {
    expect(resolveGovernanceWorkstream("/accounting/security-master")).toBe("security-master");
    expect(resolveGovernanceWorkstream("/accounting/reconciliation")).toBe("reconciliation");
    expect(resolveGovernanceWorkstream("/accounting")).toBe("ledger");
    expect(resolveGovernanceWorkstream("/reporting")).toBe("reporting");
    expect(resolveGovernanceWorkstream("/governance/security-master")).toBe("security-master");
    expect(resolveGovernanceWorkstream("/governance/reconciliation")).toBe("reconciliation");
    expect(resolveGovernanceWorkstream("/governance")).toBe("ledger");

    expect(resolveSelectedReconciliation(reconciliationQueue, "run-57")?.runId).toBe("run-57");
    expect(resolveSelectedReconciliation(reconciliationQueue, null)?.runId).toBe("run-42");
    expect(resolveSelectedReconciliation([], null)).toBeNull();
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

  it("derives canonical Accounting and Reporting loading states", () => {
    expect(buildGovernanceLoadingViewState("/accounting/reconciliation")).toMatchObject({
      role: "status",
      ariaBusy: true,
      ariaLive: "polite",
      titleId: "accounting-workspace-loading-title",
      detailId: "accounting-workspace-loading-detail",
      title: "Loading Accounting",
      detail: "Waiting for ledger, reconciliation, cash-flow, and Security Master summaries from the workstation bootstrap payload."
    });

    expect(buildGovernanceLoadingViewState("/reporting")).toMatchObject({
      titleId: "reporting-workspace-loading-title",
      detailId: "reporting-workspace-loading-detail",
      title: "Loading Reporting",
      detail: "Waiting for report-pack, governed export, and approval summaries from the workstation bootstrap payload."
    });
  });

  it("derives cash-flow evidence rows, route context, and variance posture", () => {
    const cashFlow: GovernanceCashFlowSummary = {
      totalCash: 120000,
      totalLedgerCash: 119750,
      netVariance: -250,
      totalFinancing: 1400,
      runsWithCashSignals: 4,
      runsWithCashVariance: 2,
      tone: "danger",
      summary: "Cash-flow coverage is available for 4 runs; 2 runs need variance review."
    };

    const state = buildGovernanceCashFlowViewState(cashFlow, "/reporting", "reporting");

    expect(state).toMatchObject({
      title: cashFlow.summary,
      description: "Reporting packet context at /reporting reuses the shared accounting/reporting cash-flow summary payload.",
      statusLabel: "Variance review",
      statusTone: "danger",
      ariaLabel: "Cash-flow evidence for Reporting packet context at /reporting",
      statusAriaLabel: "Cash-flow status Variance review. Net variance -$250."
    });
    expect(state.rows).toEqual(expect.arrayContaining([
      expect.objectContaining({ id: "portfolio-cash", value: "$120,000", tone: "default" }),
      expect.objectContaining({ id: "net-variance", value: "-$250", tone: "danger", ariaLabel: "Net variance: -$250" }),
      expect.objectContaining({ id: "variance-runs", value: "2", tone: "danger", ariaLabel: "Runs with variance: 2" })
    ]));
    expect(state.statusAnnouncement).toBe("Variance review: Cash-flow coverage is available for 4 runs; 2 runs need variance review.");
  });

  it("derives pending cash-flow state when the bootstrap payload is unavailable", () => {
    const state = buildGovernanceCashFlowViewState(null, "/accounting", "ledger");

    expect(state).toMatchObject({
      title: "Cash-flow evidence loading",
      statusLabel: "Pending",
      statusTone: "warning",
      rows: [],
      statusAnnouncement: "Cash-flow evidence is loading."
    });
  });

  it("derives calibration summary status presentation and KPI rows", () => {
    const summary: ReconciliationCalibrationSummary = {
      status: "ReviewRequired",
      summary: "Two routes need operator review before sign-off.",
      asOf: "2026-05-09T14:30:00Z",
      totalBreakCount: 8,
      activeBreakCount: 5,
      openBreakCount: 2,
      inReviewBreakCount: 3,
      resolvedBreakCount: 6,
      dismissedBreakCount: 0,
      criticalOpenBreakCount: 1,
      pendingSignoffCount: 3,
      signedOffCount: 4,
      missingCalibrationMetadataCount: 1,
      profiles: [
        {
          toleranceProfileId: "profile-cash",
          exceptionRoute: "cash",
          highestSeverity: "Critical",
          maxToleranceBand: 250,
          totalBreakCount: 8,
          openBreakCount: 2,
          inReviewBreakCount: 3,
          resolvedBreakCount: 6,
          dismissedBreakCount: 0,
          pendingSignoffCount: 3,
          signedOffCount: 4,
          lastUpdatedAt: "2026-05-09T14:00:00Z"
        }
      ]
    };

    const state = buildCalibrationSummaryViewState(summary, false, null);

    expect(state).toMatchObject({
      statusLabel: "Review required",
      statusTone: "warning",
      statusIcon: "alert",
      statusTextClassName: "text-warning",
      statusBannerClassName: "border-warning/30 bg-warning/5",
      profilesLabel: "1 tolerance profile",
      hasProfiles: true
    });
    expect(state.metricRows).toEqual([
      expect.objectContaining({ id: "total", label: "Total breaks", value: 8, tone: "default", ariaLabel: "Total breaks: 8" }),
      expect.objectContaining({ id: "open", label: "Open", value: 2, tone: "warning", ariaLabel: "Open: 2" }),
      expect.objectContaining({ id: "critical-open", label: "Critical open", value: 1, tone: "warning" }),
      expect.objectContaining({ id: "pending-signoff", label: "Pending sign-off", value: 3, tone: "warning" }),
      expect.objectContaining({ id: "signed-off", label: "Signed off", value: 4, tone: "default" }),
      expect.objectContaining({ id: "missing-metadata", label: "Missing metadata", value: 1, tone: "warning" })
    ]);
    expect(state.profileRows[0].ariaLabel).toContain("profile-cash");
  });

  it("derives trial-balance table rows, labels, and status announcements", () => {
    const state = buildGovernanceTrialBalanceViewState({
      runId: "run-42",
      rows: trialBalanceLines,
      loading: false,
      errorText: null
    });

    expect(state).toMatchObject({
      title: "Primary trial balance",
      description: "Primary basis ledger balances for run-42 grouped by account type. Values are basis per configured policy until accountant review.",
      tableLabel: "Primary trial balance lines for run-42",
      selectedBasis: "Primary",
      state: "ready",
      hasRows: true,
      statusAnnouncement: "2 trial balance lines loaded for run-42."
    });
    expect(state.basisOptions).toEqual(expect.arrayContaining([
      expect.objectContaining({ id: "Primary", rowCount: 2, rowCountLabel: "2 rows", isSelected: true }),
      expect.objectContaining({ id: "Gaap", rowCount: 0, rowCountLabel: "0 rows", isSelected: false })
    ]));
    expect(state.rows[0]).toMatchObject({
      rowId: "Primary-Cash-Asset-acct-cash",
      accountLabel: "Cash",
      accountTypeLabel: "Asset",
      basisLabel: "Primary basis",
      policyLabel: "legacy-v1/legacy-v1",
      balanceLabel: "$120,500",
      balanceTone: "success",
      entryCountLabel: "12",
      ariaLabel: "Cash Asset. Primary basis. Policy legacy-v1/legacy-v1. Balance $120,500. 12 entries",
      selectAriaLabel: "Inspect trial-balance account Cash for Asset",
      detailPanelId: "trial-balance-account-detail",
      isExpanded: true
    });
    expect(state.selectedRowId).toBe("Primary-Cash-Asset-acct-cash");
    expect(state.selectedDetail).toMatchObject({
      eyebrow: "Trial-balance detail",
      title: "Cash",
      subtitle: "Asset · acct-cash",
      statusLabel: "Debit / asset",
      statusVariant: "success",
      ariaLabel: "Trial-balance detail for Cash"
    });
    expect(state.rows[1]).toMatchObject({
      balanceLabel: "-$500",
      balanceTone: "danger",
      isExpanded: false
    });

    const selectedFinancing = buildGovernanceTrialBalanceViewState({
      runId: "run-42",
      rows: trialBalanceLines,
      selectedRowId: "Primary-Financing payable-Liability-acct-financing",
      loading: false,
      errorText: null
    });
    expect(selectedFinancing.selectedDetail).toMatchObject({
      title: "Financing payable",
      statusLabel: "Credit / payable",
      statusVariant: "danger"
    });
  });

  it("filters trial-balance rows by basis and builds a basis bridge", () => {
    const state = buildGovernanceTrialBalanceViewState({
      runId: "run-42",
      selectedBasis: "Gaap",
      rows: [
        {
          accountName: "Cash",
          accountType: "Asset",
          symbol: null,
          financialAccountId: "acct-cash",
          balance: 120500,
          entryCount: 12,
          security: null,
          accountingBasis: "Primary",
          accountingPolicyId: "legacy-v1",
          accountingPolicyVersion: "legacy-v1",
          ruleId: "direct-lending.daily-accrual",
          sourceEventId: "event-42"
        },
        {
          accountName: "Cash",
          accountType: "Asset",
          symbol: null,
          financialAccountId: "acct-cash",
          balance: 119500,
          entryCount: 10,
          security: null,
          accountingBasis: "Gaap",
          accountingPolicyId: "gaap-default-v1",
          accountingPolicyVersion: "v1",
          ruleId: "direct-lending.daily-accrual",
          sourceEventId: "event-42"
        },
        {
          accountName: "Accrued interest receivable",
          accountType: "Asset",
          symbol: null,
          financialAccountId: "acct-interest",
          balance: 1000,
          entryCount: 1,
          security: null,
          accountingBasis: "Gaap",
          accountingPolicyId: "gaap-default-v1",
          accountingPolicyVersion: "v1",
          ruleId: "direct-lending.daily-accrual",
          sourceEventId: "event-42"
        }
      ],
      loading: false,
      errorText: null
    });

    expect(state.selectedBasis).toBe("Gaap");
    expect(state.rows).toHaveLength(2);
    expect(state.rows[0]).toMatchObject({
      basisLabel: "GAAP basis",
      basisTone: "success",
      policyLabel: "gaap-default-v1/v1"
    });
    expect(state.basisOptions).toEqual(expect.arrayContaining([
      expect.objectContaining({ id: "Primary", rowCount: 1, isSelected: false }),
      expect.objectContaining({ id: "Gaap", rowCount: 2, isSelected: true })
    ]));
    expect(state.basisBridge).toMatchObject({
      title: "Basis bridge",
      fromBasis: "Primary",
      toBasis: "Gaap",
      hasRows: true
    });
    expect(state.basisBridge.rows).toEqual(expect.arrayContaining([
      expect.objectContaining({
        accountLabel: "Cash",
        primaryBalanceLabel: "$120,500",
        comparisonBalanceLabel: "$119,500",
        varianceLabel: "-$1,000",
        varianceTone: "danger",
        sourceLabel: "Source event-42 / Rule direct-lending.daily-accrual"
      })
    ]));
  });

  it("derives trial-balance loading, empty, and error states", () => {
    expect(buildGovernanceTrialBalanceViewState({
      runId: "run-42",
      rows: [],
      loading: true,
      errorText: null
    })).toMatchObject({
      state: "loading",
      loadingText: "Loading trial balance for run-42.",
      statusAnnouncement: "Loading trial balance for run-42."
    });

    expect(buildGovernanceTrialBalanceViewState({
      runId: "run-42",
      rows: [],
      loading: false,
      errorText: null
    })).toMatchObject({
      state: "empty",
      emptyTitle: "No trial balance lines",
      statusAnnouncement: "No trial balance lines returned for run-42."
    });

    expect(buildGovernanceTrialBalanceViewState({
      runId: "run-42",
      rows: trialBalanceLines,
      loading: false,
      errorText: "Ledger unavailable."
    })).toMatchObject({
      state: "error",
      errorText: "Ledger unavailable.",
      statusAnnouncement: "Trial balance failed for run-42: Ledger unavailable."
    });
  });

  it("derives search status, result count, and live announcement copy", () => {
    expect(buildSecuritySearchState({
      query: "",
      searching: false,
      results: null,
      searchError: null,
      identityLoading: false,
      identityError: null
    }).searchStatusText).toBe("Enter a ticker, ISIN, CUSIP, FIGI, or display name.");

    const searching = buildSecuritySearchState({
      query: " aapl ",
      searching: true,
      results: null,
      searchError: null,
      identityLoading: false,
      identityError: null
    });

    expect(searching.searchStatusText).toBe('Searching Security Master for "aapl"...');
    expect(searching.statusAnnouncement).toBe("Searching Security Master for aapl.");

    const queued = buildSecuritySearchState({
      query: "AAPL",
      searching: false,
      results: null,
      searchError: null,
      identityLoading: false,
      identityError: null
    });

    expect(queued.searchStatusText).toBe('Security Master search queued for "AAPL".');
    expect(queued.statusAnnouncement).toBe("Security Master search queued for AAPL.");

    const complete = buildSecuritySearchState({
      query: "AAPL",
      searching: false,
      results: [securityResult],
      searchError: null,
      identityLoading: false,
      identityError: null
    });

    expect(complete.hasResults).toBe(true);
    expect(complete.resultCount).toBe(1);
    expect(complete.resultsTableLabel).toBe("Security search results");
    expect(complete.resultColumns.map((column) => column.label)).toEqual(["Name", "Asset Class", "Primary ID", "Currency", "Status"]);
    expect(complete.resultRows[0]).toMatchObject({
      rowId: "security-result-sec-1",
      isSelected: false,
      detailPanelId: "security-master-identity-detail",
      isExpanded: false,
      selectAriaLabel: "Open identity drill-in for Apple Inc.",
      primaryIdentifierLabel: "Ticker: AAPL",
      statusTone: "success",
      ariaLabel: "Apple Inc., Equity, primary identifier Ticker: AAPL, currency USD, status Active."
    });
    expect(complete.searchStatusText).toBe('1 securities found for "AAPL".');
    expect(complete.statusAnnouncement).toBe("1 securities found for AAPL.");
  });

  it("derives selected Security Master search result rows", () => {
    const rows = buildSecuritySearchResultRows([securityResult], "sec-1");

    expect(rows[0]).toMatchObject({
      rowId: "security-result-sec-1",
      isSelected: true,
      detailPanelId: "security-master-identity-detail",
      isExpanded: true,
      selectAriaLabel: "Open identity drill-in for Apple Inc.",
      primaryIdentifierLabel: "Ticker: AAPL",
      statusTone: "success"
    });
    expect(rows[0].ariaLabel).toContain("selected");
    expect(buildSecuritySearchResultRows(null, null)).toEqual([]);
  });

  it("derives selectable corporate-action rows with a detail panel", () => {
    const state = buildCorporateActionsViewState("sec-1", corporateActions, "ca-split-1", false, null);

    expect(state).toMatchObject({
      tableLabel: "Corporate actions for sec-1",
      detailPanelId: "corporate-action-detail-panel",
      selectedRowId: "ca-split-1",
      hasRows: true,
      errorText: null,
      loadingText: null
    });
    expect(state.rows[1]).toMatchObject({
      rowId: "ca-split-1",
      eventTypeLabel: "Stock split",
      amountLabel: "4:1 split",
      selectAriaLabel: "Inspect corporate action Stock split for sec-1",
      detailPanelId: "corporate-action-detail-panel",
      isExpanded: true
    });
    expect(state.selectedDetail).toMatchObject({
      id: "corporate-action-detail-panel",
      title: "Stock split",
      ariaLabel: "Corporate action detail for Stock split on sec-1",
      statusLabel: "Pay date unavailable"
    });
    expect(state.selectedDetail?.fields).toEqual(expect.arrayContaining([
      { label: "Amount or ratio", value: "4:1 split", tone: "default" },
      { label: "Pay date", value: "—", tone: "warning" }
    ]));
  });

  it("keeps corporate-action loading, empty, and error states in the view model", () => {
    expect(buildCorporateActionsViewState("sec-1", null, null, true, null)).toMatchObject({
      loadingText: "Loading corporate actions...",
      statusAnnouncement: "Loading corporate actions for sec-1."
    });

    expect(buildCorporateActionsViewState("sec-1", [], null, false, null)).toMatchObject({
      hasRows: false,
      emptyText: "No corporate actions recorded for sec-1.",
      selectedDetail: null,
      detailEmptyAriaLabel: "No corporate action selected"
    });

    expect(buildCorporateActionsViewState("sec-1", [], null, false, "Corporate API offline")).toMatchObject({
      errorText: "Corporate API offline",
      statusAnnouncement: "Corporate actions error: Corporate API offline"
    });
  });

  it("ignores stale Security Master identity responses after a newer selection settles", async () => {
    const staleIdentity = deferred<SecurityIdentityDrillIn>();
    const latestIdentity = deferred<SecurityIdentityDrillIn>();
    const services = createSecurityMasterServices({
      getIdentity: vi.fn()
        .mockReturnValueOnce(staleIdentity.promise)
        .mockReturnValueOnce(latestIdentity.promise)
    });
    const drillInServices = createSecurityMasterDrillInServices();
    const latestDetail: SecurityIdentityDrillIn = {
      ...securityIdentity,
      securityId: "sec-2",
      displayName: "Microsoft Corp."
    };

    const { result } = renderHook(() => useSecurityMasterViewModel(true, services, drillInServices, 0));

    act(() => {
      void result.current.selectSecurity("sec-1");
    });
    act(() => {
      void result.current.selectSecurity("sec-2");
    });
    await act(async () => {
      latestIdentity.resolve(latestDetail);
      await latestIdentity.promise;
    });

    await waitFor(() => expect(result.current.identity?.securityId).toBe("sec-2"));

    await act(async () => {
      staleIdentity.resolve(securityIdentity);
      await staleIdentity.promise;
    });

    expect(result.current.identity?.securityId).toBe("sec-2");
    expect(result.current.identityLoading).toBe(false);
    expect(result.current.identityErrorText).toBeNull();
  });

  it("clears pending Security Master drill-in state when the workstream becomes inactive", async () => {
    const identity = deferred<SecurityIdentityDrillIn>();
    const corporateActions = deferred<CorporateAction[]>();
    const parameters = deferred<TradingParameters>();
    const services = createSecurityMasterServices({
      getIdentity: vi.fn().mockReturnValue(identity.promise)
    });
    const drillInServices = createSecurityMasterDrillInServices({
      getCorporateActions: vi.fn().mockReturnValue(corporateActions.promise),
      getTradingParameters: vi.fn().mockReturnValue(parameters.promise)
    });

    const { result, rerender } = renderHook(
      ({ active }) => useSecurityMasterViewModel(active, services, drillInServices, 0),
      { initialProps: { active: true } }
    );

    act(() => {
      void result.current.selectSecurity("sec-1");
    });
    await waitFor(() => expect(result.current.identityLoading).toBe(true));

    rerender({ active: false });

    await waitFor(() => expect(result.current.identityLoading).toBe(false));
    expect(result.current.selectedSecurityId).toBeNull();
    expect(result.current.identity).toBeNull();
    expect(result.current.corporateActionsLoading).toBe(false);
    expect(result.current.tradingParametersLoading).toBe(false);

    await act(async () => {
      identity.resolve(securityIdentity);
      corporateActions.resolve([]);
      parameters.resolve(tradingParameters);
      await Promise.all([identity.promise, corporateActions.promise, parameters.promise]);
    });

    expect(result.current.identity).toBeNull();
    expect(result.current.selectedSecurityId).toBeNull();
  });

  it("surfaces search failures and counts open conflicts for badges", () => {
    const failed = buildSecuritySearchState({
      query: "AAPL",
      searching: false,
      results: [],
      searchError: "Provider offline",
      identityLoading: false,
      identityError: null
    });

    expect(failed.searchErrorText).toBe("Security search failed: Provider offline");
    expect(failed.statusAnnouncement).toBe("Security search failed: Provider offline");
    expect(countOpenSecurityConflicts(conflicts)).toBe(1);
    expect(countOpenSecurityConflicts(null)).toBe(0);
    expect(buildSecurityConflictRefreshCommand(false, null)).toEqual({
      label: "Refresh conflicts",
      ariaLabel: "Refresh Security Master identifier conflicts",
      disabled: false,
      disabledReason: null,
      busy: false,
      busyLabel: null
    });
    expect(buildSecurityConflictRefreshCommand(true, null)).toMatchObject({
      label: "Refreshing...",
      disabled: true,
      disabledReason: "Identifier conflicts are already loading.",
      busy: true,
      busyLabel: "Refreshing..."
    });
    expect(buildSecurityConflictRefreshCommand(false, "Provider offline")).toMatchObject({
      label: "Retry conflicts",
      ariaLabel: "Retry loading Security Master identifier conflicts"
    });
  });

  it("derives Security Master master-detail page summary from selected state", () => {
    const state = buildSecurityMasterPageViewState({
      query: "AAPL",
      results: [securityResult],
      selectedSecurityId: "sec-1",
      selectedDisplayName: "Apple Inc.",
      selectedAssetClass: "Equity",
      selectedStatus: "Active",
      identity: securityIdentity,
      identityLoading: false,
      conflicts,
      conflictsLoading: false,
      corporateActions,
      tradingParameters
    });

    expect(state).toMatchObject({
      ariaLabel: "Security Master command deck",
      title: "Security Master command deck",
      detailTitle: "Security detail page",
      detailSubtitle: "sec-1 · Equity",
      detailStatusLabel: "Active",
      detailStatusBadgeVariant: "success"
    });
    expect(state.metrics).toEqual(expect.arrayContaining([
      expect.objectContaining({ id: "results", value: "1", tone: "success" }),
      expect.objectContaining({ id: "selected", value: "Apple Inc.", detail: "Security ID sec-1" }),
      expect.objectContaining({ id: "conflicts", value: "1", tone: "warning" })
    ]));
    expect(state.detailSections).toEqual(expect.arrayContaining([
      { id: "overview", label: "Overview", value: "1 identifier", active: true },
      { id: "schedules", label: "Schedules", value: "2 corporate actions" },
      { id: "controls", label: "Controls", value: "Trading set" },
      { id: "audit", label: "Audit", value: "1 conflict" }
    ]));
  });

  it("retries Security Master identifier conflicts through view-model command state", async () => {
    const retryConflicts = deferred<SecurityMasterConflict[]>();
    const services = createSecurityMasterServices({
      getConflicts: vi.fn()
        .mockRejectedValueOnce(new Error("Conflict API offline"))
        .mockReturnValueOnce(retryConflicts.promise)
    });
    const drillInServices = createSecurityMasterDrillInServices();

    const { result } = renderHook(() => useSecurityMasterViewModel(true, services, drillInServices, 0));

    await waitFor(() => expect(result.current.conflictsErrorText).toBe("Conflict API offline"));
    expect(result.current.conflictRefreshCommand).toMatchObject({
      label: "Retry conflicts",
      disabled: false
    });

    let retry!: Promise<void>;
    act(() => {
      retry = result.current.refreshConflicts();
    });

    await waitFor(() => expect(result.current.conflictRefreshCommand).toMatchObject({
      label: "Refreshing...",
      disabled: true,
      busy: true
    }));

    await act(async () => {
      retryConflicts.resolve(conflicts);
      await retry;
    });

    expect(result.current.conflictsErrorText).toBeNull();
    expect(result.current.conflictRows).toHaveLength(2);
    expect(result.current.conflictRefreshCommand).toMatchObject({
      label: "Refresh conflicts",
      disabled: false
    });
  });

  it("derives Security Master identity drill-in rows and accessible table labels", () => {
    const state = buildSecurityIdentityDrillInState(securityIdentity);

    expect(state).toMatchObject({
      panelId: "security-master-identity-detail",
      title: "Identity drill-in · Apple Inc.",
      subtitle: "sec-1 · v3 · Equity",
      description: "1 identifier · 1 alias · effective 2024-01-01 -> active",
      ariaLabel: "Security identity detail for Apple Inc.",
      statusLabel: "Active",
      statusBadgeVariant: "success",
      identifiersTableLabel: "Identifiers for Apple Inc.",
      aliasesTableLabel: "Aliases for Apple Inc."
    });
    expect(state?.summaryFields).toEqual(expect.arrayContaining([
      { label: "Security ID", value: "sec-1" },
      { label: "Effective", value: "2024-01-01 -> active" }
    ]));
    expect(state?.identifiers[0]).toMatchObject({
      rowId: "identifier-ticker-aapl",
      providerLabel: "Bloomberg",
      primaryLabel: "Primary",
      primaryBadgeVariant: "success",
      validRangeLabel: "2024-01-01 -> active",
      ariaLabel: "Ticker AAPL, Primary, provider Bloomberg, valid 2024-01-01 -> active"
    });
    expect(state?.aliases[0]).toMatchObject({
      rowId: "alias-alias-1",
      providerLabel: "—",
      enabledLabel: "Enabled",
      enabledBadgeVariant: "success",
      validRangeLabel: "2025-01-01 -> active",
      createdLabel: "2025-01-01",
      reasonText: "Market data source mapping",
      ariaLabel: "ProviderSymbol AAPL.OQ, Enabled, scope Collector, provider —, valid 2025-01-01 -> active"
    });
  });

  it("derives provider-specific conflict actions and row accessibility copy", () => {
    const rows = buildSecurityConflictRows(conflicts, "conflict-1");

    expect(rows[0]).toMatchObject({
      conflictId: "conflict-1",
      statusTone: "warning",
      isOpen: true,
      isResolving: true,
      providerASummary: "Bloomberg -> security sec-1",
      providerBSummary: "Refinitiv -> security sec-2",
      detectedLabel: "Detected 2026-01-01",
      resolutionStatusText: "Resolving identifier conflict conflict-1."
    });
    expect(rows[0].ariaLabel).toContain("Identifier conflict conflict-1 on identifiers.CUSIP: Open.");
    expect(rows[0].actions).toEqual([
      expect.objectContaining({
        resolution: "AcceptA",
        label: "Use Bloomberg",
        disabled: true,
        ariaLabel: "Resolve identifier conflict conflict-1 on identifiers.CUSIP with Bloomberg value sec-1"
      }),
      expect.objectContaining({
        resolution: "AcceptB",
        label: "Use Refinitiv",
        disabled: true,
        ariaLabel: "Resolve identifier conflict conflict-1 on identifiers.CUSIP with Refinitiv value sec-2"
      }),
      expect.objectContaining({
        resolution: "Dismiss",
        label: "Dismiss conflict",
        disabled: true,
        ariaLabel: "Dismiss identifier conflict conflict-1 on identifiers.CUSIP"
      })
    ]);
    expect(rows[1]).toMatchObject({
      conflictId: "conflict-2",
      statusTone: "neutral",
      isOpen: false,
      actions: []
    });
  });

  it("derives reconciliation break action state and live announcements", () => {
    const rows = buildReconciliationBreakRows(breakQueue, { breakId: "run-42:cash", command: "assign" });

    expect(rows[0]).toMatchObject({
      breakId: "run-42:cash",
      actionBusy: true,
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
      loading: false,
      loadError: null,
      action: { breakId: "run-42:cash", command: "assign" },
      actionError: null
    });

    expect(state.hasBreaks).toBe(true);
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
  });

  it("derives reconciliation detail presentation state", () => {
    expect(buildReconciliationDetailViewState(reconciliationQueue[0])).toMatchObject({
      eyebrow: "Reconciliation detail",
      title: "Paper Index Mean Reversion",
      description: "run-42 is currently BreaksOpen.",
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

  it("derives reporting profile selector rows and detail state", () => {
    const state = buildGovernanceReportingViewState({
      reporting: {
        profileCount: 2,
        recommendedProfiles: ["board"],
        reportPackTargets: ["board", "audit"],
        summary: "2 export/reporting profiles are available for governance workflows.",
        profiles: [
          {
            id: "excel",
            name: "Excel",
            targetTool: "Excel",
            format: "Xlsx",
            description: "Board-ready workbook export.",
            loaderScript: false,
            dataDictionary: true
          },
          {
            id: "board",
            name: "Board packet",
            targetTool: "Board",
            format: "Markdown",
            description: "Owner sign-off packet.",
            loaderScript: true,
            dataDictionary: false
          }
        ]
      },
      selectedProfileId: "board"
    });

    expect(state.countLabel).toBe("2 profiles");
    expect(state.targetSummary).toBe("Targets: board, audit.");
    expect(state.rows[1]).toMatchObject({
      id: "board",
      isSelected: true,
      formatLabel: "MARKDOWN",
      targetLabel: "Target - Board",
      recommendationLabel: "Recommended for current packet flow",
      selectAriaLabel: "Inspect reporting profile Board packet for Board Markdown"
    });
    expect(state.rows[1].badges.map((badge) => badge.label)).toEqual(["Recommended", "Dictionary missing", "Loader script"]);
    expect(state.selectedProfile?.fields).toEqual(expect.arrayContaining([
      expect.objectContaining({ label: "Data dictionary", value: "Missing", tone: "warning" }),
      expect.objectContaining({ label: "Loader script", value: "Available", tone: "success" })
    ]));
    expect(state.selectedExportProfileId).toBe("board");
    expect(state.exportCanRun).toBe(true);
    expect(state.exportAriaLabel).toBe("Run reporting export for Board packet");
    expect(state.backendLinks).toEqual([
      {
        id: "preview",
        label: "Preview report payload",
        href: "/api/export/preview",
        ariaLabel: "Open GET /api/export/preview for Preview report payload"
      },
      {
        id: "formats",
        label: "List export formats",
        href: "/api/export/formats",
        ariaLabel: "Open GET /api/export/formats for List export formats"
      }
    ]);
  });

  it("surfaces reporting profile empty state from the view model", () => {
    const state = buildGovernanceReportingViewState({
      reporting: {
        profileCount: 0,
        recommendedProfiles: [],
        reportPackTargets: [],
        profiles: [],
        summary: "No profiles loaded."
      },
      selectedProfileId: null
    });

    expect(state.hasRows).toBe(false);
    expect(state.emptyText).toBe("No reporting profiles available. Sync report-pack metadata before export review.");
    expect(state.statusDetail).toBe("No reporting profiles are configured for packet generation.");
    expect(state.nextAction).toBe("Sync reporting profile metadata before packet generation.");
    expect(state.exportCanRun).toBe(false);
    expect(state.exportAriaLabel).toBe("Run reporting export unavailable until a reporting profile is loaded");
  });

  it("formats reporting export command results for success and failure states", () => {
    expect(formatReportingExportResult({
      jobId: "export-1",
      success: true,
      status: "completed",
      profileId: "excel",
      symbols: [],
      filesGenerated: 2,
      totalRecords: 12,
      totalBytes: 2048,
      outputDirectory: "artifacts/exports/export-1",
      durationSeconds: 1.5,
      error: null,
      warnings: [],
      files: [],
      timestamp: "2026-01-01T00:00:00Z"
    })).toEqual({
      text: "Export export-1 completed with 2 file(s), 12 record(s), and 2 KB. Output artifacts/exports/export-1.",
      tone: "success",
      role: "status"
    });

    expect(formatReportingExportResult({
      jobId: null,
      success: false,
      status: "failed",
      profileId: "excel",
      symbols: null,
      filesGenerated: 0,
      totalRecords: 0,
      totalBytes: 0,
      outputDirectory: null,
      durationSeconds: 0,
      error: "Exporter unavailable",
      warnings: null,
      files: [],
      timestamp: "2026-01-01T00:00:00Z"
    })).toEqual({
      text: "Export excel failed: Exporter unavailable",
      tone: "danger",
      role: "alert"
    });
  });
});
