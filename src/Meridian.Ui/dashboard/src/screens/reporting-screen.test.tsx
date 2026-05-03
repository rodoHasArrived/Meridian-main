import { beforeEach, describe, expect, it, vi } from "vitest";
import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ReportingScreen } from "@/screens/reporting-screen";
import { renderWithRouter } from "@/test/render";
import type { GovernanceWorkspaceResponse } from "@/types";

const governance: GovernanceWorkspaceResponse = {
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
    summary: "Cash is balanced."
  },
  reporting: {
    profileCount: 2,
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
      },
      {
        id: "audit-pack",
        name: "Audit Pack",
        targetTool: "Audit portal",
        format: "Markdown",
        description: "Audit evidence packet.",
        loaderScript: true,
        dataDictionary: true
      }
    ],
    reportPackTargets: ["board", "audit"],
    summary: "2 export profiles available."
  }
};

describe("ReportingScreen", () => {
  const fetchMock = vi.fn();

  beforeEach(() => {
    fetchMock.mockReset();
    fetchMock.mockResolvedValue({
      ok: true,
      text: async () => JSON.stringify({
        jobId: "export-1",
        success: true,
        status: "completed",
        profileId: "audit-pack",
        symbols: [],
        filesGenerated: 2,
        totalRecords: 20,
        totalBytes: 1200,
        outputDirectory: "exports",
        durationSeconds: 1,
        error: null,
        warnings: [],
        timestamp: "2026-05-01T00:00:00Z"
      })
    });
    vi.stubGlobal("fetch", fetchMock);
  });

  it("renders loading copy when governance reporting data is unavailable", () => {
    renderWithRouter(<ReportingScreen data={null} />, { initialEntries: ["/reporting"] });

    expect(screen.getByText("Loading Reporting")).toBeInTheDocument();
    expect(screen.getByText(/waiting for governed report-pack/i)).toBeInTheDocument();
  });

  it("renders report-pack targets with accessible row labels", () => {
    renderWithRouter(<ReportingScreen data={governance} />, { initialEntries: ["/reporting"] });

    expect(screen.getByRole("list", { name: "Report-pack targets" })).toBeInTheDocument();
    expect(screen.getByLabelText("board report-pack target")).toBeInTheDocument();
    expect(screen.getByLabelText("audit report-pack target")).toBeInTheDocument();
  });

  it("updates selected profile detail and profile-scoped actions", async () => {
    const user = userEvent.setup();
    renderWithRouter(<ReportingScreen data={governance} />, { initialEntries: ["/reporting"] });

    const auditButton = screen.getByRole("button", { name: /select audit pack export profile/i });
    await user.click(auditButton);

    expect(auditButton).toHaveAttribute("aria-pressed", "true");
    expect(screen.getByRole("complementary", { name: /audit pack selected/i })).toBeInTheDocument();
    expect(screen.getByRole("status", { name: /audit pack readiness/i })).toHaveTextContent(
      "Loader and dictionary evidence are ready"
    );

    const preview = screen.getByRole("link", { name: "Preview Audit Pack export payload" });
    const run = screen.getByRole("button", { name: "Run Audit Pack export analysis" });

    expect(preview).toHaveAttribute("href", "/api/export/preview?profile=audit-pack");
    expect(run).toBeEnabled();
  });

  it("posts selected profile when running export analysis", async () => {
    const user = userEvent.setup();
    renderWithRouter(<ReportingScreen data={governance} />, { initialEntries: ["/reporting"] });

    await user.click(screen.getByRole("button", { name: /select audit pack export profile/i }));
    await user.click(screen.getByRole("button", { name: "Run Audit Pack export analysis" }));

    expect(fetchMock).toHaveBeenCalledWith(
      "/api/export/analysis",
      expect.objectContaining({
        method: "POST",
        body: JSON.stringify({ profileId: "audit-pack" })
      })
    );
    await waitFor(() => {
      expect(screen.getByRole("status", { name: "Reporting export status" })).toHaveTextContent(
        "Audit Pack export completed: 2 files generated."
      );
    });
  });

  it("renders explicit empty states for missing reporting profiles and pack targets", () => {
    const emptyGovernance: GovernanceWorkspaceResponse = {
      ...governance,
      reporting: {
        profileCount: 0,
        recommendedProfiles: [],
        profiles: [],
        reportPackTargets: [],
        summary: "No reporting profiles configured."
      }
    };

    renderWithRouter(<ReportingScreen data={emptyGovernance} />, { initialEntries: ["/reporting"] });

    expect(screen.getByText(/no export profiles are configured/i)).toBeInTheDocument();
    expect(screen.getByRole("status", { name: "No report-pack targets" })).toHaveTextContent(
      "Configure report-pack targets in the governance policy."
    );
  });
});
