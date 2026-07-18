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

  it("keeps the library focused on available templates instead of duplicating the reporting cockpit", async () => {
    await renderScreen();

    expect(screen.queryByRole("region", { name: "Daily reporting cockpit" })).not.toBeInTheDocument();
    expect(screen.getByRole("region", { name: "Available report templates" })).toBeInTheDocument();
    expect(screen.getByText("Browse standard report catalog")).toBeInTheDocument();
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

  it("routes draft templates to builder review instead of offering an unauthorized run", async () => {
    const draftData: AccountingWorkspaceResponse = {
      ...data,
      reporting: {
        ...data.reporting,
        templates: [{
          ...data.reporting.templates![0],
          lifecycleStatus: "Draft"
        }],
        recentRuns: []
      }
    };

    renderWithRouter(<ReportLibraryScreen data={draftData} />, { initialEntries: ["/reporting/library"] });
    await waitForAsyncEffects();

    const reviewLinks = screen.getAllByRole("link", { name: "Review Trial Balance Pack" });
    expect(reviewLinks.length).toBeGreaterThan(0);
    for (const link of reviewLinks) {
      expect(link).toHaveAttribute("href", "/reporting/report-builder?templateId=trial-balance-pack%3A1.0");
    }
    expect(screen.queryByRole("link", { name: "Run Trial Balance Pack" })).not.toBeInTheDocument();
  });

  it("humanizes template and run statuses without presenting a missing as-of date as ready data", async () => {
    const reviewData: AccountingWorkspaceResponse = {
      ...data,
      reporting: {
        ...data.reporting,
        templates: [{
          ...data.reporting.templates![0],
          lifecycleStatus: "INREVIEW"
        }],
        recentRuns: [{
          ...data.reporting.recentRuns![0],
          status: "AwaitingApproval",
          asOfDate: null
        }]
      }
    };

    renderWithRouter(<ReportLibraryScreen data={reviewData} />, { initialEntries: ["/reporting/library"] });
    await waitForAsyncEffects();

    expect(screen.getByText("In review")).toBeInTheDocument();
    expect(screen.getByText("Latest: Awaiting approval · No as-of date retained")).toBeInTheDocument();
    expect(screen.queryByText("INREVIEW")).not.toBeInTheDocument();
    expect(screen.queryByText(/As of As-of date unavailable/i)).not.toBeInTheDocument();
  });
});
