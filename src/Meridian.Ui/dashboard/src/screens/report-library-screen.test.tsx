import { screen } from "@testing-library/react";
import { ReportLibraryScreen } from "@/screens/report-library-screen";
import { renderWithRouter, waitForAsyncEffects } from "@/test/render";
import type { AccountingWorkspaceResponse } from "@/types";

const data: AccountingWorkspaceResponse = {
  metrics: [],
  reconciliationQueue: [],
  breakQueue: [],
  cashFlow: {
    totalCash: 0,
    totalLedgerCash: 0,
    netVariance: 0,
    totalFinancing: 0,
    runsWithCashSignals: 0,
    runsWithCashVariance: 0,
    tone: "success",
    summary: "No cash-flow signals."
  },
  reporting: {
    profileCount: 0,
    recommendedProfiles: [],
    profiles: [],
    summary: "No reporting profiles.",
    templates: [
      { templateId: "trial-balance-pack", family: "Financial Statements", name: "Trial Balance Pack", version: "1.0", sections: ["summary"] },
      { templateId: "investor-statement", family: "Investor Reporting", name: "Investor Statement", version: "2.1", sections: ["summary", "detail"] }
    ],
    recentRuns: [
      {
        runId: "run-tb-1",
        templateId: "trial-balance-pack",
        family: "Financial Statements",
        status: "Approved",
        trigger: "Scheduled",
        attemptCount: 1,
        sectionCount: 1,
        lineageLinkedSections: 1,
        artifacts: [],
        auditActions: [],
        failureReason: null,
        asOfDate: "2026-06-30",
        drilldownLinks: [{
          id: "drilldown-1",
          kind: "ReportPack",
          label: "Open",
          href: "/reporting/report-packs?runId=run-tb-1",
          method: "GET",
          isBrowserNavigable: true,
          source: "ReportPack"
        }]
      }
    ],
    dailyWork: []
  }
};

async function renderScreen() {
  const result = renderWithRouter(<ReportLibraryScreen data={data} />, { initialEntries: ["/reporting/library"] });
  await waitForAsyncEffects();
  return result;
}

describe("ReportLibraryScreen", () => {
  it("renders a loading state while reporting workspace data is unavailable", async () => {
    renderWithRouter(<ReportLibraryScreen data={null} />, { initialEntries: ["/reporting/library"] });
    await waitForAsyncEffects();

    expect(screen.getByRole("status", { name: "Loading Report Library" })).toBeInTheDocument();
  });

  it("renders the reporting hub with family cards", async () => {
    await renderScreen();

    expect(screen.getByRole("region", { name: "Daily reporting cockpit" })).toBeInTheDocument();
    expect(screen.getAllByText("Financial Statements").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Investor Reporting").length).toBeGreaterThan(0);
  });

  it("renders standard report cards and links runnable templates to the parameters screen", async () => {
    await renderScreen();

    expect(screen.getByRole("heading", { name: "Report Library" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Trial Balance" })).toBeInTheDocument();
    expect(screen.getByText("Account-balance proof with debit, credit, and ending balance support.")).toBeInTheDocument();
    expect(screen.getByText("Ledger, chart of accounts, accounting basis, period close posture")).toBeInTheDocument();
    expect(screen.getAllByText("Controller").length).toBeGreaterThan(0);
    const trialBalanceLink = screen.getAllByRole("link", { name: "Run Trial Balance Pack" })[0];
    expect(trialBalanceLink).toHaveAttribute("href", "/reporting/run?templateId=trial-balance-pack%3A1.0");

    const investorLink = screen.getAllByRole("link", { name: "Run Investor Statement" })[0];
    expect(investorLink).toHaveAttribute("href", "/reporting/run?templateId=investor-statement%3A2.1");
  });
});
