import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import * as api from "@/lib/api";
import { GovernanceScreen } from "@/screens/governance-screen";
import { renderWithRouter, waitForAsyncEffects } from "@/test/render";
import type { GovernanceWorkspaceResponse, LedgerTrialBalanceLine, SecurityMasterConflict } from "@/types";

vi.mock("@/lib/api", async () => {
  const actual = await vi.importActual<typeof import("@/lib/api")>("@/lib/api");
  return {
    ...actual,
    searchSecurities: vi.fn().mockResolvedValue([]),
    getSecurityIdentity: vi.fn().mockResolvedValue(null),
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

    expect(screen.getByRole("region", { name: "Governance workbench context" })).toBeInTheDocument();
    expect(screen.getByText("Reconciliation queue")).toBeInTheDocument();
    expect(screen.getByText("Reporting profiles")).toBeInTheDocument();
    expect(screen.getByText("Cash-flow coverage is available for 4 runs; 1 run needs variance review.")).toBeInTheDocument();
    expect(screen.getByRole("region", { name: "Cash-flow evidence for Ledger context at /accounting" })).toBeInTheDocument();
    expect(screen.getByLabelText("Cash-flow status Variance review. Net variance $500.")).toHaveTextContent("Variance review");
    expect(screen.getByLabelText("Runs with variance: 1")).toHaveTextContent("1");
    expect(screen.getByText("Paper Index Mean Reversion")).toBeInTheDocument();
  });

  it("renders trial-balance rows with accessible table evidence", async () => {
    vi.mocked(api.getRunTrialBalance).mockResolvedValueOnce(trialBalanceLines);

    await renderGovernanceScreen(data, "/accounting");

    const table = await screen.findByRole("table", { name: "Trial balance lines for run-42" });
    expect(table).toBeInTheDocument();
    expect(screen.getByRole("row", { name: "Cash Asset. Balance $120,500. 12 entries" })).toBeInTheDocument();
    expect(screen.getByRole("row", { name: "Financing payable Liability. Balance -$500. 2 entries" })).toBeInTheDocument();
    expect(screen.getByText("-$500")).toHaveClass("text-danger");
  });

  it("renders a useful trial-balance empty state instead of a blank table", async () => {
    vi.mocked(api.getRunTrialBalance).mockResolvedValueOnce([]);

    await renderGovernanceScreen(data, "/accounting");

    expect(await screen.findByRole("status")).toHaveTextContent("No trial balance lines");
    expect(screen.queryByRole("table", { name: "Trial balance lines for run-42" })).not.toBeInTheDocument();
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
    const securityRow = await screen.findByText("Apple Inc.");
    await user.click(securityRow);

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

  it("renders reconciliation detail on deep-link routes and updates selection", async () => {
    const user = userEvent.setup();

    await renderGovernanceScreen(data, "/accounting/reconciliation");

    expect(screen.getByText("Reconciliation Detail")).toBeInTheDocument();
    expect(screen.getByText(/Open reconciliation breaks remain on this run/)).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: /Intraday Vol Carry/i }));

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
