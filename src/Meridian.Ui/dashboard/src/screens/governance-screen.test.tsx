import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { GovernanceScreen } from "@/screens/governance-screen";
import * as api from "@/lib/api";
import type { GovernanceWorkspaceResponse, SecurityMasterConflict } from "@/types";

vi.mock("@/lib/api", async () => {
  const actual = await vi.importActual<typeof import("@/lib/api")>("@/lib/api");
  return {
    ...actual,
    getSecurityConflicts: vi.fn().mockResolvedValue([]),
    resolveSecurityConflict: vi.fn().mockResolvedValue({}),
    getReconciliationBreakQueue: vi.fn().mockResolvedValue([]),
    getRunTrialBalance: vi.fn().mockResolvedValue([]),
    searchSecurities: vi.fn().mockResolvedValue([]),
    getSecurityDetail: vi.fn(),
    getSecurityIdentity: vi.fn(),
    getSecurityHistory: vi.fn(),
    getSecurityEconomicDefinition: vi.fn(),
    reviewReconciliationBreak: vi.fn(),
    resolveReconciliationBreak: vi.fn()
  };
});

const securityCoverageOpen = {
  portfolioResolved: 1,
  portfolioMissing: 1,
  portfolioPartial: 0,
  ledgerResolved: 1,
  ledgerMissing: 1,
  ledgerPartial: 0,
  hasIssues: true,
  tone: "warning" as const,
  summary: "2 references linked, 0 partial, 2 unresolved.",
  resolvedReferences: [
    {
      source: "portfolio",
      symbol: "AAPL",
      accountName: null,
      securityId: "security-aapl",
      displayName: "Apple Inc.",
      assetClass: "Equity",
      subType: "CommonShare",
      currency: "USD",
      status: "Active",
      primaryIdentifier: "AAPL",
      coverageStatus: "Resolved" as const,
      coverageReason: null,
      matchedIdentifierKind: "Ticker",
      matchedIdentifierValue: "AAPL",
      matchedProvider: "Polygon",
      securityDetailUrl: "/workstation/governance/security-master?securityId=security-aapl"
    }
  ],
  reviewReferences: [
    {
      source: "portfolio",
      symbol: "TSLA",
      accountName: null,
      securityId: null,
      displayName: "TSLA",
      assetClass: null,
      subType: null,
      currency: null,
      status: null,
      primaryIdentifier: "TSLA",
      coverageStatus: "Missing" as const,
      coverageReason: "Portfolio position is missing a Security Master match.",
      matchedIdentifierKind: null,
      matchedIdentifierValue: null,
      matchedProvider: null,
      securityDetailUrl: null
    }
  ],
  missingReferences: [
    {
      source: "portfolio",
      symbol: "TSLA",
      accountName: null,
      securityId: null,
      displayName: "TSLA",
      assetClass: null,
      subType: null,
      currency: null,
      status: null,
      primaryIdentifier: "TSLA",
      coverageStatus: "Missing" as const,
      coverageReason: "Portfolio position is missing a Security Master match.",
      matchedIdentifierKind: null,
      matchedIdentifierValue: null,
      matchedProvider: null,
      securityDetailUrl: null
    }
  ]
};

const resolvedCoverage = {
  portfolioResolved: 2,
  portfolioMissing: 0,
  portfolioPartial: 0,
  ledgerResolved: 2,
  ledgerMissing: 0,
  ledgerPartial: 0,
  hasIssues: false,
  tone: "success" as const,
  summary: "4 references mapped with no unresolved symbols.",
  resolvedReferences: [
    {
      source: "portfolio",
      symbol: "MSFT",
      accountName: null,
      securityId: "security-msft",
      displayName: "Microsoft Corp.",
      assetClass: "Equity",
      subType: "CommonShare",
      currency: "USD",
      status: "Active",
      primaryIdentifier: "MSFT",
      coverageStatus: "Resolved" as const,
      coverageReason: null,
      matchedIdentifierKind: "Ticker",
      matchedIdentifierValue: "MSFT",
      matchedProvider: "Polygon",
      securityDetailUrl: "/workstation/governance/security-master?securityId=security-msft"
    }
  ],
  reviewReferences: [],
  missingReferences: []
};

const data: GovernanceWorkspaceResponse = {
  metrics: [
    { id: "m1", label: "Open Breaks", value: "2", delta: "+1", tone: "warning" },
    { id: "m2", label: "Timing Drift", value: "1", delta: "0%", tone: "warning" },
    { id: "m3", label: "Security Gaps", value: "1", delta: "0%", tone: "warning" },
    { id: "m4", label: "Audit Ready", value: "4", delta: "+2", tone: "success" }
  ],
  reconciliationQueue: [
    {
      runId: "run-42",
      strategyName: "Paper Index Mean Reversion",
      mode: "paper",
      status: "Running",
      lastUpdated: "3m ago",
      auditReference: "audit-run-42",
      ledgerReference: "ledger-run-42",
      portfolioId: "portfolio-run-42",
      breakCount: 2,
      openBreakCount: 1,
      reconciliationStatus: "BreaksOpen",
      securityCoverage: securityCoverageOpen,
      cashFlow: {
        cashBalance: 120000,
        ledgerCashBalance: 120500,
        cashVariance: 500,
        financing: 1400,
        realizedPnl: 900,
        unrealizedPnl: 250,
        journalEntryCount: 12,
        tone: "warning",
        summary: "Cash and ledger balances diverge."
      },
      latestReconciliation: {
        reconciliationRunId: "recon-42",
        breakCount: 2,
        openBreakCount: 1,
        matchCount: 8,
        hasTimingDrift: false,
        securityIssueCount: 1,
        hasSecurityCoverageIssues: true,
        securityCoverageIssues: [
          {
            source: "portfolio",
            symbol: "TSLA",
            accountName: null,
            reason: "Missing Security Master coverage.",
            securityId: null,
            displayName: null,
            assetClass: null,
            subType: null,
            coverageStatus: "Missing",
            coverageReason: "Portfolio position is missing a Security Master match.",
            currency: null,
            matchedIdentifierKind: null,
            matchedIdentifierValue: null,
            matchedProvider: null
          }
        ],
        lastUpdated: "2m ago",
        tone: "warning"
      }
    },
    {
      runId: "run-57",
      strategyName: "Intraday Vol Carry",
      mode: "paper",
      status: "Paused",
      lastUpdated: "7m ago",
      auditReference: "audit-run-57",
      ledgerReference: "ledger-run-57",
      portfolioId: "portfolio-run-57",
      breakCount: 1,
      openBreakCount: 0,
      reconciliationStatus: "Resolved",
      securityCoverage: resolvedCoverage,
      cashFlow: {
        cashBalance: 98000,
        ledgerCashBalance: 98000,
        cashVariance: 0,
        financing: 300,
        realizedPnl: 400,
        unrealizedPnl: 125,
        journalEntryCount: 8,
        tone: "success",
        summary: "Cash and ledger balances are aligned."
      },
      latestReconciliation: {
        reconciliationRunId: "recon-57",
        breakCount: 1,
        openBreakCount: 0,
        matchCount: 12,
        hasTimingDrift: false,
        securityIssueCount: 0,
        hasSecurityCoverageIssues: false,
        securityCoverageIssues: [],
        lastUpdated: "6m ago",
        tone: "success"
      }
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
  },
  workspace: {
    totalRuns: 2,
    reconciledRuns: 2,
    ledgerReadyRuns: 2,
    openBreaks: 1,
    securityIssues: 1
  }
};

describe("GovernanceScreen", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(api.getSecurityConflicts).mockResolvedValue([]);
    vi.mocked(api.getReconciliationBreakQueue).mockResolvedValue([]);
    vi.mocked(api.getRunTrialBalance).mockResolvedValue([]);
    vi.mocked(api.searchSecurities).mockResolvedValue([]);
  });

  it("renders reconciliation, cash-flow, and reporting summaries", () => {
    render(
      <MemoryRouter initialEntries={["/governance"]}>
        <GovernanceScreen data={data} />
      </MemoryRouter>
    );

    expect(screen.getByText("Reconciliation queue")).toBeInTheDocument();
    expect(screen.getByText("Reporting profiles")).toBeInTheDocument();
    expect(screen.getByText("Cash-flow coverage is available for 4 runs; 1 run needs variance review.")).toBeInTheDocument();
    expect(screen.getByText("Paper Index Mean Reversion")).toBeInTheDocument();
  });

  it("renders security coverage review refs and loads detail/history drill-ins from deep links", async () => {
    vi.mocked(api.searchSecurities).mockResolvedValue([
      {
        securityId: "security-aapl",
        displayName: "Apple Inc.",
        status: "Active",
        classification: {
          assetClass: "Equity",
          subType: "CommonShare",
          primaryIdentifierKind: "Ticker",
          primaryIdentifierValue: "AAPL"
        },
        economicDefinition: {
          currency: "USD",
          version: 4,
          effectiveFrom: "2026-01-01T00:00:00Z",
          effectiveTo: null,
          subType: "CommonShare",
          assetFamily: "Equity",
          issuerType: "Corporate"
        }
      }
    ]);
    vi.mocked(api.getSecurityDetail).mockResolvedValue({
      securityId: "security-aapl",
      displayName: "Apple Inc.",
      status: "Active",
      classification: {
        assetClass: "Equity",
        subType: "CommonShare",
        primaryIdentifierKind: "Ticker",
        primaryIdentifierValue: "AAPL"
      },
      economicDefinition: {
        currency: "USD",
        version: 4,
        effectiveFrom: "2026-01-01T00:00:00Z",
        effectiveTo: null,
        subType: "CommonShare",
        assetFamily: "Equity",
        issuerType: "Corporate"
      }
    });
    vi.mocked(api.getSecurityIdentity).mockResolvedValue({
      securityId: "security-aapl",
      displayName: "Apple Inc.",
      assetClass: "Equity",
      status: "Active",
      version: 4,
      effectiveFrom: "2026-01-01T00:00:00Z",
      effectiveTo: null,
      identifiers: [
        { kind: "Ticker", value: "AAPL", isPrimary: true, validFrom: "2026-01-01T00:00:00Z", validTo: null, provider: "Polygon" }
      ],
      aliases: [
        { provider: "Polygon", value: "AAPL", validFrom: "2026-01-01T00:00:00Z", validTo: null }
      ]
    });
    vi.mocked(api.getSecurityHistory).mockResolvedValue([
      {
        globalSequence: 10,
        securityId: "security-aapl",
        streamVersion: 4,
        eventType: "SecurityCreated",
        eventTimestamp: "2026-01-01T00:00:00Z",
        actor: "ops.gov",
        correlationId: null,
        causationId: null,
        payload: {},
        metadata: {}
      }
    ]);
    vi.mocked(api.getSecurityEconomicDefinition).mockResolvedValue({
      currency: "USD",
      version: 4,
      effectiveFrom: "2026-01-01T00:00:00Z",
      effectiveTo: null,
      subType: "CommonShare",
      assetFamily: "Equity",
      issuerType: "Corporate"
    });

    render(
      <MemoryRouter initialEntries={["/governance/security-master?securityId=security-aapl&query=AAPL"]}>
        <GovernanceScreen data={data} />
      </MemoryRouter>
    );

    expect(screen.getByText("Security coverage queue")).toBeInTheDocument();
    expect(screen.getAllByText("Portfolio position is missing a Security Master match.").length).toBeGreaterThan(0);

    await waitFor(() => expect(screen.getAllByText("Apple Inc.").length).toBeGreaterThan(0));
    expect(screen.getAllByText("Ticker: AAPL").length).toBeGreaterThan(0);
    expect(screen.getByText("SecurityCreated")).toBeInTheDocument();
    expect(api.getSecurityDetail).toHaveBeenCalledWith("security-aapl");
    expect(api.getSecurityHistory).toHaveBeenCalledWith("security-aapl");
  });

  it("exposes unresolved coverage deep links for symbol review", () => {
    render(
      <MemoryRouter initialEntries={["/governance/security-master"]}>
        <GovernanceScreen data={data} />
      </MemoryRouter>
    );

    const deepLink = screen.getByRole("link", { name: /open search deep link/i });
    expect(deepLink).toHaveAttribute("href", "/governance/security-master?query=TSLA");
  });

  it("refreshes conflicts after resolving an identifier conflict", async () => {
    const user = userEvent.setup();
    const openConflict: SecurityMasterConflict = {
      conflictId: "conflict-1",
      securityId: "security-aapl",
      conflictKind: "Identifier",
      fieldPath: "Ticker",
      providerA: "Polygon",
      valueA: "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
      providerB: "Databento",
      valueB: "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
      detectedAt: "2026-01-01T00:00:00Z",
      status: "Open"
    };

    vi.mocked(api.getSecurityConflicts)
      .mockResolvedValueOnce([openConflict])
      .mockResolvedValueOnce([{ ...openConflict, status: "Resolved" }]);
    vi.mocked(api.resolveSecurityConflict).mockResolvedValue({ ...openConflict, status: "Resolved" });

    render(
      <MemoryRouter initialEntries={["/governance/security-master"]}>
        <GovernanceScreen data={data} />
      </MemoryRouter>
    );

    await screen.findByText("Identifier conflicts");
    await user.click(await screen.findByRole("button", { name: "Accept A" }));

    await waitFor(() => {
      expect(api.resolveSecurityConflict).toHaveBeenCalledWith({
        conflictId: "conflict-1",
        resolution: "AcceptA",
        resolvedBy: "operator"
      });
      expect(api.getSecurityConflicts).toHaveBeenCalledTimes(2);
    });
  });

  it("renders reconciliation detail on deep-link routes and updates selection", async () => {
    const user = userEvent.setup();

    render(
      <MemoryRouter initialEntries={["/governance/reconciliation"]}>
        <GovernanceScreen data={data} />
      </MemoryRouter>
    );

    expect(screen.getByText("Reconciliation Detail")).toBeInTheDocument();
    expect(screen.getByText(/Open reconciliation breaks remain on this run/)).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: /Intraday Vol Carry/i }));

    expect(screen.getByText(/Historical breaks have been worked through/)).toBeInTheDocument();
  });
});
