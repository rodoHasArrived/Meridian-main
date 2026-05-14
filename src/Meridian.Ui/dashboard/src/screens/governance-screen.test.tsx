import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import * as api from "@/lib/api";
import { GovernanceScreen } from "@/screens/governance-screen";
import { renderWithRouter, waitForAsyncEffects } from "@/test/render";
import type { CorporateAction, GovernanceWorkspaceResponse, LedgerTrialBalanceLine, SecurityMasterConflict } from "@/types";

vi.mock("@/lib/api", async () => {
  const actual = await vi.importActual<typeof import("@/lib/api")>("@/lib/api");
  return {
    ...actual,
    searchSecurities: vi.fn().mockResolvedValue([]),
    getSecurityIdentity: vi.fn().mockResolvedValue(null),
    getOperatorOverrides: vi.fn().mockResolvedValue({
      securityId: "sec-1",
      values: {},
      updatedBy: "",
      updatedAt: ""
    }),
    patchOperatorOverrides: vi.fn(),
    getSecurityConflicts: vi.fn().mockResolvedValue([]),
    getReconciliationBreakQueue: vi.fn().mockResolvedValue([]),
    getReconciliationCalibrationSummary: vi.fn().mockResolvedValue({
      asOf: "2026-01-01T00:00:00Z",
      status: "Ready",
      summary: "Calibration metadata is available for reconciliation workflows.",
      totalBreakCount: 1,
      activeBreakCount: 1,
      openBreakCount: 1,
      inReviewBreakCount: 0,
      resolvedBreakCount: 0,
      dismissedBreakCount: 0,
      criticalOpenBreakCount: 0,
      pendingSignoffCount: 0,
      signedOffCount: 0,
      missingCalibrationMetadataCount: 0,
      profiles: []
    }),
    resolveReconciliationBreak: vi.fn(),
    reviewReconciliationBreak: vi.fn(),
    runAnalysisExport: vi.fn(),
    getRunTrialBalance: vi.fn().mockResolvedValue([]),
    getCorporateActions: vi.fn().mockResolvedValue([]),
    getTradingParameters: vi.fn().mockResolvedValue(null),
    resolveSecurityConflict: vi.fn()
  };
});

const data: GovernanceWorkspaceResponse = {
  metrics: [
    { id: "m1", label: "Open Breaks", value: "2", delta: "+1", tone: "warning" },
    { id: "m2", label: "Timing Drift", value: "1", delta: "0%", tone: "warning" },
    { id: "m3", label: "Security Gaps", value: "0", delta: "0%", tone: "success" },
    { id: "m4", label: "Audit Ready", value: "4", delta: "+2", tone: "success" }
  ],
  reconciliationQueue: [
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
  ],
  breakQueue: [
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
    }
  ],
  cashFlow: {
    totalCash: 120000,
    totalLedgerCash: 120500,
    netVariance: 500,
    totalFinancing: 1400,
    runsWithCashSignals: 4,
    runsWithCashVariance: 1,
    tone: "warning",
    summary: "Cash-flow coverage is available for 4 runs; 1 run needs variance review."
  },
  reporting: {
    profileCount: 4,
    recommendedProfiles: ["excel"],
    profiles: [
      {
        id: "excel",
        name: "Excel",
        targetTool: "Excel",
        format: "Xlsx",
        description: "Board-ready workbook export.",
        loaderScript: false,
        dataDictionary: true
      }
    ],
    reportPackTargets: ["board"],
    summary: "4 export/reporting profiles are available for governance workflows."
  }
};

const securityConflict: SecurityMasterConflict = {
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

async function renderGovernanceScreen(
  screenData: GovernanceWorkspaceResponse = data,
  initialEntry = "/accounting"
) {
  const result = renderWithRouter(<GovernanceScreen data={screenData} />, { initialEntries: [initialEntry] });
  await waitForAsyncEffects();
  return result;
}

describe("GovernanceScreen", () => {
  it("renders reconciliation, cash-flow, and reporting summaries", async () => {
    await renderGovernanceScreen();

    expect(screen.getByRole("region", { name: "Accounting workbench context" })).toBeInTheDocument();
    expect(screen.getByText("Reconciliation queue")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Open Accounting reconciliation workstream" })).toHaveAttribute(
      "href",
      "/accounting/reconciliation"
    );
    expect(screen.getByRole("table", { name: "Reconciliation runs" })).toHaveTextContent("Paper Index Mean Reversion");
    expect(screen.getByRole("row", { name: /Paper Index Mean Reversion.*BreaksOpen.*1 open/i })).not.toHaveAttribute(
      "aria-controls"
    );
    expect(screen.getByText("Reporting profiles")).toBeInTheDocument();
    expect(screen.getByText("Cash-flow coverage is available for 4 runs; 1 run needs variance review.")).toBeInTheDocument();
    expect(screen.getByRole("region", { name: "Cash-flow evidence for Ledger context at /accounting" })).toBeInTheDocument();
    expect(screen.getByLabelText("Cash-flow status Variance review. Net variance $500.")).toHaveTextContent("Variance review");
    expect(screen.getByLabelText("Runs with variance: 1")).toHaveTextContent("1");
    expect(screen.getByText("Paper Index Mean Reversion")).toBeInTheDocument();
  });

  it("renders reconciliation strong panels with view-model presentation state", async () => {
    await renderGovernanceScreen(data, "/accounting/reconciliation");

    expect(screen.getAllByRole("table", { name: "Reconciliation runs" })).toHaveLength(1);
    expect(screen.queryByRole("link", { name: "Open Accounting reconciliation workstream" })).not.toBeInTheDocument();
    expect(screen.getByRole("region", { name: "Reconciliation detail for Paper Index Mean Reversion" })).toBeInTheDocument();
    const selectedRun = screen.getByRole("row", { name: "Inspect reconciliation run Paper Index Mean Reversion" });
    expect(selectedRun).toHaveAttribute("aria-selected", "true");
    expect(selectedRun).toHaveAttribute("aria-expanded", "true");
    expect(selectedRun).toHaveAttribute("aria-controls", "reconciliation-run-detail-panel");
    expect(screen.getByRole("table", { name: "Reconciliation runs" })).toBeInTheDocument();
    expect(screen.getByLabelText("Open breaks: 1")).toHaveTextContent("1");
    expect(screen.getByLabelText("Reconciliation narrative for Paper Index Mean Reversion")).toHaveTextContent(
      "Open reconciliation breaks remain on this run."
    );
  });

  it("updates reconciliation detail queue selection with accessible expanded state", async () => {
    const user = userEvent.setup();
    await renderGovernanceScreen(data, "/accounting/reconciliation");

    const nextRun = screen.getByRole("row", { name: "Inspect reconciliation run Intraday Vol Carry" });
    expect(nextRun).not.toHaveAttribute("aria-selected");
    expect(nextRun).toHaveAttribute("aria-expanded", "false");

    await user.click(nextRun);

    expect(nextRun).toHaveAttribute("aria-selected", "true");
    expect(nextRun).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByRole("region", { name: "Reconciliation detail for Intraday Vol Carry" })).toBeInTheDocument();
  });

  it("supports keyboard selection from the reconciliation detail queue table", async () => {
    const user = userEvent.setup();
    await renderGovernanceScreen(data, "/accounting/reconciliation");

    const nextRun = screen.getByRole("row", { name: "Inspect reconciliation run Intraday Vol Carry" });
    nextRun.focus();

    await user.keyboard("{Enter}");

    expect(nextRun).toHaveAttribute("aria-selected", "true");
    expect(screen.getByRole("region", { name: "Reconciliation detail for Intraday Vol Carry" })).toBeInTheDocument();
  });

  it("renders reconciliation detail queue empty state when no runs are available", async () => {
    await renderGovernanceScreen({ ...data, reconciliationQueue: [] }, "/accounting/reconciliation");

    expect(screen.getByText("No reconciliation runs are available for this accounting scope.")).toBeInTheDocument();
    expect(screen.getByRole("region", { name: "No reconciliation run selected" })).toHaveTextContent(
      "Reconciliation evidence is unavailable until the workspace payload includes at least one run."
    );
  });

  it("renders trial-balance rows with accessible table evidence", async () => {
    const user = userEvent.setup();
    vi.mocked(api.getRunTrialBalance).mockResolvedValueOnce(trialBalanceLines);

    await renderGovernanceScreen(data, "/accounting");

    const table = await screen.findByRole("table", { name: "Primary trial balance lines for run-42" });
    expect(table).toBeInTheDocument();
    const cashRow = screen.getByRole("row", { name: "Inspect trial-balance account Cash for Asset" });
    const financingRow = screen.getByRole("row", { name: "Inspect trial-balance account Financing payable for Liability" });
    expect(cashRow).toHaveAttribute("aria-selected", "true");
    expect(cashRow).toHaveAttribute("aria-expanded", "true");
    expect(cashRow).toHaveAttribute("aria-controls", "trial-balance-account-detail");
    expect(screen.getByRole("region", { name: "Trial-balance detail for Cash" })).toHaveTextContent("$120,500");
    expect(financingRow).toHaveAttribute("aria-expanded", "false");
    expect(screen.getByText("-$500")).toHaveClass("text-danger");

    await user.click(financingRow);

    expect(screen.getByRole("region", { name: "Trial-balance detail for Financing payable" })).toHaveTextContent("Credit / payable");
    expect(financingRow).toHaveAttribute("aria-selected", "true");
  });

  it("renders a useful trial-balance empty state instead of a blank table", async () => {
    vi.mocked(api.getRunTrialBalance).mockResolvedValueOnce([]);

    await renderGovernanceScreen(data, "/accounting");

    expect(await screen.findByText("No trial balance lines")).toBeInTheDocument();
    expect(screen.queryByRole("table", { name: "Primary trial balance lines for run-42" })).not.toBeInTheDocument();
  });

  it("runs ledger reporting export through the POST mutation instead of a GET link", async () => {
    const user = userEvent.setup();
    vi.mocked(api.runAnalysisExport).mockResolvedValueOnce({
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
    });

    await renderGovernanceScreen(data, "/accounting");

    await user.click(screen.getByRole("button", { name: "Run reporting export for Excel" }));

    expect(api.runAnalysisExport).toHaveBeenCalledWith("excel");
    expect(await screen.findByText("Export export-1 completed with 2 file(s), 12 record(s), and 2 KB. Output artifacts/exports/export-1.")).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Run reporting export" })).not.toBeInTheDocument();
  });

  it("renders reporting profile detail state and updates selected profile", async () => {
    const user = userEvent.setup();
    const reportingData: GovernanceWorkspaceResponse = {
      ...data,
      reporting: {
        ...data.reporting,
        profileCount: 2,
        recommendedProfiles: ["board"],
        reportPackTargets: ["board", "audit"],
        profiles: [
          ...data.reporting.profiles,
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
      }
    };

    await renderGovernanceScreen(reportingData, "/reporting");

    expect(screen.getByText("Report packet posture")).toBeInTheDocument();
    expect(screen.getByText(/Targets: board, audit\./)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Inspect reporting profile Excel for Excel Xlsx" })).toHaveAttribute("aria-pressed", "true");

    await user.click(screen.getByRole("button", { name: "Inspect reporting profile Board packet for Board Markdown" }));

    expect(screen.getByText("Selected reporting profile - Board packet")).toBeInTheDocument();
    expect(screen.getByText("MARKDOWN - Board")).toBeInTheDocument();
    expect(screen.getByText("Dictionary missing")).toBeInTheDocument();
    expect(screen.getAllByText("Loader script").length).toBeGreaterThan(0);

    const detailPanel = screen.getByTestId("reporting-profile-detail");
    expect(detailPanel).toHaveClass("min-w-0", "overflow-hidden");
    expect(detailPanel.querySelector("dl > div")).toHaveClass("grid", "min-w-0");
  });

  it("adapts the hero copy for security-master deep links", async () => {
    await renderGovernanceScreen(data, "/accounting/security-master");

    expect(screen.getAllByText("Security coverage").length).toBeGreaterThan(0);
  });

  it("announces security search failures as alerts", async () => {
    const user = userEvent.setup();
    vi.mocked(api.searchSecurities).mockRejectedValueOnce(new Error("Provider offline"));

    await renderGovernanceScreen(data, "/accounting/security-master");

    await user.type(screen.getByLabelText("Search securities"), "AAPL");

    expect(await screen.findByRole("alert")).toHaveTextContent("Security search failed: Provider offline");
  });

  it("accepts and renders alias rows inside identity drill-in for accounting workflows", async () => {
    const user = userEvent.setup();
    vi.mocked(api.searchSecurities).mockResolvedValueOnce([
      {
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
      }
    ]);
    vi.mocked(api.getSecurityIdentity).mockResolvedValueOnce({
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
          provider: "Nasdaq",
          scope: "Collector",
          reason: "Market data source mapping",
          createdBy: "ops.gov",
          createdAt: "2025-01-01T00:00:00Z",
          validFrom: "2025-01-01T00:00:00Z",
          validTo: null,
          isEnabled: true
        }
      ]
    });
    await renderGovernanceScreen(data, "/accounting/security-master");

    await user.type(screen.getByPlaceholderText("Search securities…"), "AAPL");
    const securityRow = await screen.findByRole("row", { name: "Open identity drill-in for Apple Inc." });
    expect(securityRow).toHaveAttribute("aria-controls", "security-master-identity-detail");
    expect(securityRow).toHaveAttribute("aria-expanded", "false");
    await user.click(securityRow);

    expect(securityRow).toHaveAttribute("aria-expanded", "true");
    expect(securityRow).toHaveAttribute("aria-selected", "true");
    expect(await screen.findByText(/Identity drill-in · Apple Inc\./i)).toBeInTheDocument();
    expect(screen.getByRole("region", { name: "Security identity detail for Apple Inc." })).toBeInTheDocument();
    expect(screen.getByRole("table", { name: "Identifiers for Apple Inc." })).toBeInTheDocument();
    expect(screen.getByRole("row", {
      name: "Ticker AAPL, Primary, provider Bloomberg, valid 2024-01-01 -> active"
    })).toBeInTheDocument();
    expect(screen.getByRole("table", { name: "Aliases for Apple Inc." })).toBeInTheDocument();
    expect(screen.getByText("AAPL.OQ")).toBeInTheDocument();
    expect(screen.getByText("Collector")).toBeInTheDocument();
  });

  it("selects Security Master search rows with keyboard-expanded detail linkage", async () => {
    const user = userEvent.setup();
    vi.mocked(api.getOperatorOverrides).mockResolvedValueOnce({
      securityId: "sec-1",
      values: {
        issuer: "Apple Inc.",
        couponRate: "5.25",
        finalMaturity: "2032-06-30"
      },
      updatedBy: "ops",
      updatedAt: "2026-05-12T10:00:00Z"
    });
    vi.mocked(api.searchSecurities).mockResolvedValueOnce([
      {
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
      }
    ]);
    vi.mocked(api.getSecurityIdentity).mockResolvedValueOnce({
      securityId: "sec-1",
      displayName: "Apple Inc.",
      assetClass: "Equity",
      status: "Active",
      version: 3,
      effectiveFrom: "2024-01-01T00:00:00Z",
      effectiveTo: null,
      identifiers: [],
      aliases: []
    });

    await renderGovernanceScreen(data, "/accounting/security-master");

    await user.type(screen.getByPlaceholderText("Search securities…"), "AAPL");
    const securityRow = await screen.findByRole("row", { name: "Open identity drill-in for Apple Inc." });

    securityRow.focus();
    await user.keyboard("[Enter]");

    expect(securityRow).toHaveAttribute("aria-controls", "security-master-identity-detail");
    expect(securityRow).toHaveAttribute("aria-expanded", "true");
    expect(await screen.findByRole("region", { name: "Security identity detail for Apple Inc." })).toBeInTheDocument();
    expect(screen.getByRole("region", { name: "Security detail page" })).toHaveTextContent("sec-1 · Equity");
    expect(screen.getByRole("toolbar", { name: "Security detail sections for Apple Inc." })).toHaveTextContent("Schedules");
    expect(screen.getByText("Security details")).toBeInTheDocument();
    expect(await screen.findByText("2 hidden overrides")).toBeInTheDocument();
    expect(screen.queryByText("Coupon Rate (%)")).not.toBeInTheDocument();
    expect(screen.queryByText("Final Maturity")).not.toBeInTheDocument();
    expect(screen.queryByText("S&P Rating")).not.toBeInTheDocument();
  });

  it("renders corporate actions as selectable dense evidence with a detail panel", async () => {
    const user = userEvent.setup();
    vi.mocked(api.searchSecurities).mockResolvedValueOnce([
      {
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
      }
    ]);
    vi.mocked(api.getSecurityIdentity).mockResolvedValueOnce({
      securityId: "sec-1",
      displayName: "Apple Inc.",
      assetClass: "Equity",
      status: "Active",
      version: 3,
      effectiveFrom: "2024-01-01T00:00:00Z",
      effectiveTo: null,
      identifiers: [],
      aliases: []
    });
    vi.mocked(api.getCorporateActions).mockResolvedValueOnce(corporateActions);

    await renderGovernanceScreen(data, "/accounting/security-master");

    await user.type(screen.getByPlaceholderText("Search securities…"), "AAPL");
    await user.click(await screen.findByRole("row", { name: "Open identity drill-in for Apple Inc." }));

    const table = await screen.findByRole("table", { name: "Corporate actions for sec-1" });
    expect(table).toBeInTheDocument();
    const dividendRow = screen.getByRole("row", { name: "Inspect corporate action Dividend for sec-1" });
    const splitRow = screen.getByRole("row", { name: "Inspect corporate action Stock split for sec-1" });
    expect(dividendRow).toHaveAttribute("aria-selected", "true");
    expect(dividendRow).toHaveAttribute("aria-controls", "corporate-action-detail-panel");
    expect(screen.getByRole("region", { name: "Corporate action detail for Dividend on sec-1" })).toHaveTextContent("0.24 USD / share");

    splitRow.focus();
    await user.keyboard("{Enter}");

    expect(splitRow).toHaveAttribute("aria-selected", "true");
    expect(splitRow).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByRole("region", { name: "Corporate action detail for Stock split on sec-1" })).toHaveTextContent("4:1 split");
    expect(screen.getByRole("region", { name: "Corporate action detail for Stock split on sec-1" })).toHaveTextContent("Pay date unavailable");
  });

  it("renders provider-specific security conflict actions", async () => {
    const user = userEvent.setup();
    vi.mocked(api.getSecurityConflicts).mockResolvedValueOnce([securityConflict]);
    vi.mocked(api.resolveSecurityConflict).mockResolvedValueOnce({
      ...securityConflict,
      status: "Resolved"
    });

    await renderGovernanceScreen(data, "/accounting/security-master");

    expect(await screen.findByRole("group", { name: /Identifier conflict conflict-1/i })).toBeInTheDocument();
    expect(screen.getByText("Bloomberg -> security sec-1")).toBeInTheDocument();
    expect(screen.getByText("Refinitiv -> security sec-2")).toBeInTheDocument();

    const useBloomberg = screen.getByRole("button", {
      name: "Resolve identifier conflict conflict-1 on identifiers.CUSIP with Bloomberg value sec-1"
    });
    expect(useBloomberg).toHaveTextContent("Use Bloomberg");

    await user.click(useBloomberg);

    expect(api.resolveSecurityConflict).toHaveBeenCalledWith({
      conflictId: "conflict-1",
      resolution: "AcceptA",
      resolvedBy: "operator"
    });
  });

  it("recovers Security Master conflict loading failures with a retry command", async () => {
    const user = userEvent.setup();
    vi.mocked(api.getSecurityConflicts)
      .mockRejectedValueOnce(new Error("Conflict API offline"))
      .mockResolvedValueOnce([securityConflict]);

    await renderGovernanceScreen(data, "/accounting/security-master");

    expect(await screen.findByRole("alert")).toHaveTextContent("Conflict API offline");
    const retry = screen.getByRole("button", { name: "Retry loading Security Master identifier conflicts" });
    expect(retry).toHaveTextContent("Retry conflicts");

    await user.click(retry);

    expect(await screen.findByRole("group", { name: /Identifier conflict conflict-1/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Refresh Security Master identifier conflicts" })).toHaveTextContent("Refresh conflicts");
  });

  it("renders reconciliation detail on deep-link routes and updates selection", async () => {
    const user = userEvent.setup();

    await renderGovernanceScreen(data, "/accounting/reconciliation");

    expect(screen.getByRole("region", { name: "Reconciliation detail for Paper Index Mean Reversion" })).toBeInTheDocument();
    expect(screen.getByLabelText("Reconciliation narrative for Paper Index Mean Reversion")).toHaveTextContent(
      /Open reconciliation breaks remain on this run/
    );
    expect(screen.getByRole("link", { name: "Open break checklist for Paper Index Mean Reversion; 1 open break" }))
      .toHaveAttribute("href", "#reconciliation-break-queue");
    expect(screen.getByRole("link", { name: "Review audit packet for Paper Index Mean Reversion" }))
      .toHaveAttribute("href", "/api/workstation/runs/run-42/review-packet");
    expect(screen.getByRole("region", { name: "Reconciliation break checklist" }))
      .toHaveAttribute("id", "reconciliation-break-queue");

    await user.click(screen.getByRole("row", { name: "Inspect reconciliation run Intraday Vol Carry" }));

    expect(screen.getByText(/Historical breaks have been worked through/)).toBeInTheDocument();
  });

  it("assigns reconciliation breaks through the view model workflow", async () => {
    const user = userEvent.setup();
    const updatedBreak = {
      ...data.breakQueue[0],
      status: "InReview" as const,
      assignedTo: "ops.gov",
      reviewedBy: "ops.gov",
      reviewedAt: "2026-01-01T00:05:00Z"
    };

    vi.mocked(api.getReconciliationBreakQueue).mockResolvedValueOnce(data.breakQueue);
    vi.mocked(api.reviewReconciliationBreak).mockResolvedValueOnce(updatedBreak);

    await renderGovernanceScreen(data, "/accounting/reconciliation");

    await user.click(await screen.findByRole("button", { name: "Assign reconciliation break run-42:cash" }));

    expect(api.reviewReconciliationBreak).toHaveBeenCalledWith({
      breakId: "run-42:cash",
      assignedTo: "ops.gov",
      reviewedBy: "ops.gov"
    });
    expect(await screen.findByText("InReview")).toBeInTheDocument();
  });

  it("surfaces view-model disabled reasons for reconciliation queue actions", async () => {
    const user = userEvent.setup();
    const resolvedBreak = {
      ...data.breakQueue[0],
      breakId: "run-42:resolved",
      status: "Resolved" as const,
      resolvedBy: "ops.gov",
      resolvedAt: "2026-01-01T00:10:00Z",
      resolutionNote: "Matched ledger adjustment."
    };
    const dismissedBreak = {
      ...data.breakQueue[0],
      breakId: "run-42:dismissed",
      status: "Dismissed" as const,
      resolvedBy: "ops.gov",
      resolvedAt: "2026-01-01T00:12:00Z",
      resolutionNote: "Vendor duplicate ignored."
    };

    vi.mocked(api.getReconciliationBreakQueue).mockResolvedValueOnce([
      data.breakQueue[0],
      resolvedBreak,
      dismissedBreak
    ]);

    await renderGovernanceScreen(data, "/accounting/reconciliation");

    expect(await screen.findByRole("button", { name: "Assign reconciliation break run-42:resolved" }))
      .toHaveAttribute("title", "Only open breaks can be assigned; this break is Resolved.");
    expect(screen.getByRole("button", { name: "Resolve reconciliation break run-42:resolved" }))
      .toHaveAttribute("title", "This break is already resolved.");
    expect(screen.getByRole("button", { name: "Dismiss reconciliation break run-42:dismissed" }))
      .toHaveAttribute("title", "This break is already dismissed.");

    await user.click(screen.getByRole("button", { name: "Resolve reconciliation break run-42:cash" }));

    expect(screen.getByRole("button", { name: "Resolve reconciliation break run-42:cash" }))
      .toHaveAttribute("title", "Enter the rationale or cancel the open queue action before choosing another action.");
    expect(screen.getByRole("button", { name: "Confirm resolve for reconciliation break run-42:cash" }))
      .toHaveAttribute("title", "Enter an operator rationale before confirming this queue action.");
  });

  it("announces reconciliation break action failures", async () => {
    const user = userEvent.setup();

    vi.mocked(api.getReconciliationBreakQueue).mockResolvedValueOnce(data.breakQueue);
    vi.mocked(api.resolveReconciliationBreak).mockRejectedValueOnce(new Error("Ledger write rejected"));

    await renderGovernanceScreen(data, "/accounting/reconciliation");

    await user.click(await screen.findByRole("button", { name: "Resolve reconciliation break run-42:cash" }));

    // The inline rationale form appears; fill in the rationale and submit
    const rationaleInput = await screen.findByLabelText(/resolve rationale/i);
    await user.type(rationaleInput, "Reviewed cash mismatch");
    await user.click(screen.getByRole("button", { name: /confirm resolve/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Break action failed: Ledger write rejected");
  });
});
