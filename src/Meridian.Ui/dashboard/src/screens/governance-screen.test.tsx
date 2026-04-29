import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import * as api from "@/lib/api";
import { GovernanceScreen } from "@/screens/governance-screen";
import { renderWithRouter, waitForAsyncEffects } from "@/test/render";
import type { GovernanceWorkspaceResponse, SecurityMasterConflict } from "@/types";

vi.mock("@/lib/api", async () => {
  const actual = await vi.importActual<typeof import("@/lib/api")>("@/lib/api");
  return {
    ...actual,
    searchSecurities: vi.fn().mockResolvedValue([]),
    getSecurityIdentity: vi.fn().mockResolvedValue(null),
    getSecurityConflicts: vi.fn().mockResolvedValue([]),
    getReconciliationBreakQueue: vi.fn().mockResolvedValue([]),
    resolveReconciliationBreak: vi.fn(),
    reviewReconciliationBreak: vi.fn(),
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

    expect(screen.getByText("Reconciliation queue")).toBeInTheDocument();
    expect(screen.getByText("Reporting profiles")).toBeInTheDocument();
    expect(screen.getByText("Cash-flow coverage is available for 4 runs; 1 run needs variance review.")).toBeInTheDocument();
    expect(screen.getByText("Paper Index Mean Reversion")).toBeInTheDocument();
  });

  it("adapts the hero copy for security-master deep links", async () => {
    await renderGovernanceScreen(data, "/accounting/security-master");

    expect(screen.getByText("Security coverage")).toBeInTheDocument();
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
    expect(screen.getByText("Aliases")).toBeInTheDocument();
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

    expect(await screen.findByRole("alert")).toHaveTextContent("Break action failed: Ledger write rejected");
  });
});
