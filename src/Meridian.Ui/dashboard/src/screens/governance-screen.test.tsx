import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { GovernanceScreen } from "@/screens/governance-screen";
import type { GovernanceWorkspaceResponse } from "@/types";

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

describe("GovernanceScreen", () => {
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

  it("adapts the hero copy for security-master deep links", () => {
    render(
      <MemoryRouter initialEntries={["/governance/security-master"]}>
        <GovernanceScreen data={data} />
      </MemoryRouter>
    );

    expect(screen.getByText("Security coverage")).toBeInTheDocument();
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
