import { beforeEach, describe, expect, it, vi } from "vitest";
import { screen, waitFor, within } from "@testing-library/react";
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
        files: [
          {
            path: "audit/export-1.md",
            symbol: "SPY",
            format: "markdown",
            sizeBytes: 1200,
            recordCount: 20
          }
        ],
        timestamp: "2026-05-01T00:00:00Z"
      })
    });
    vi.stubGlobal("fetch", fetchMock);
  });

  it("renders loading copy when governance reporting data is unavailable", () => {
    renderWithRouter(<ReportingScreen data={null} />, { initialEntries: ["/reporting"] });

    const loading = screen.getByRole("status", { name: "Loading Reporting" });
    expect(loading).toHaveAttribute("aria-busy", "true");
    expect(loading).toHaveAttribute("aria-live", "polite");
    expect(loading).toHaveClass("border-[var(--state-pending-bd)]", "bg-[var(--state-pending-bg)]");
    expect(screen.getByText(/waiting for governed report-pack and export evidence/i)).toBeInTheDocument();
    expect(screen.getByLabelText("Route Reporting")).toBeInTheDocument();
  });

  it("renders route-aware loading copy for report packs", () => {
    renderWithRouter(<ReportingScreen data={null} />, { initialEntries: ["/reporting/report-packs"] });

    const loading = screen.getByRole("status", { name: "Loading Reporting" });
    expect(loading).toHaveAttribute("aria-busy", "true");
    expect(screen.getByLabelText("Route Report packs")).toBeInTheDocument();
  });

  it("renders report-pack targets with accessible row labels", () => {
    renderWithRouter(<ReportingScreen data={governance} />, { initialEntries: ["/reporting"] });

    expect(screen.getByRole("list", { name: "Report-pack targets" })).toBeInTheDocument();
    expect(screen.getByLabelText("board report-pack target")).toBeInTheDocument();
    expect(screen.getByLabelText("audit report-pack target")).toBeInTheDocument();
  });

  it("renders the VM-owned no-target state with warning token classes", () => {
    const missingTargets: GovernanceWorkspaceResponse = {
      ...governance,
      reporting: {
        ...governance.reporting,
        reportPackTargets: []
      }
    };

    renderWithRouter(<ReportingScreen data={missingTargets} />, { initialEntries: ["/reporting"] });

    const emptyState = screen.getByRole("status", { name: "No report-pack targets loaded" });
    expect(emptyState).toHaveTextContent(
      "No report-pack targets loaded. Configure governed targets in the governance policy before approving this packet."
    );
    expect(emptyState).toHaveClass("border-warning/30", "bg-warning/10", "text-warning");
    expect(screen.queryByRole("list", { name: "Report-pack targets" })).not.toBeInTheDocument();
  });

  it("renders the dedicated report-pack approval workflow panel", async () => {
    const user = userEvent.setup();
    renderWithRouter(<ReportingScreen data={governance} />, { initialEntries: ["/reporting/report-packs"] });

    const task = screen.getByRole("region", { name: "Report-pack approval task" });
    expect(task).toBeInTheDocument();
    expect(within(task).getByText("Report-pack approval")).toBeInTheDocument();
    expect(within(task).getByRole("list", { name: "Report-pack approval targets" })).toBeInTheDocument();
    expect(within(task).getByRole("list", { name: "Selected report-pack export actions" })).toBeInTheDocument();
    expect(within(task).getByRole("link", { name: "Preview Excel export payload" })).toHaveAttribute(
      "href",
      "/api/export/preview?profile=excel"
    );
    expect(within(task).getByRole("button", { name: "Run Excel export analysis" })).toBeEnabled();
    expect(within(task).getByRole("link", { name: "GET /api/fund-structure/report-packs for Report-pack catalog" })).toHaveAttribute(
      "href",
      "/api/fund-structure/report-packs"
    );
    expect(within(task).queryByRole("link", { name: "POST /api/export/analysis for Excel export analysis" })).toBeNull();
    expect(within(task).getByRole("group", { name: "Reference-only POST /api/export/analysis for Excel export analysis" })).toHaveTextContent(
      "Reference"
    );

    await user.click(within(task).getByRole("button", { name: "Select Audit Pack for report-pack approval" }));

    expect(within(task).getByRole("status", { name: "Selected report-pack profile" })).toHaveTextContent(
      "Audit Pack is selected for report-pack approval using Markdown output to Audit portal."
    );
    expect(within(task).getByRole("link", { name: "GET /api/export/preview?profile=audit-pack for Audit Pack export preview" })).toHaveAttribute(
      "href",
      "/api/export/preview?profile=audit-pack"
    );
    expect(within(task).getByRole("link", { name: "Preview Audit Pack export payload" })).toHaveAttribute(
      "href",
      "/api/export/preview?profile=audit-pack"
    );
    expect(within(task).getByRole("group", { name: "Reference-only POST /api/export/analysis for Audit Pack export analysis" })).toHaveTextContent(
      "Reference"
    );

    await user.click(within(task).getByRole("button", { name: "Run Audit Pack export analysis" }));

    expect(fetchMock).toHaveBeenCalledWith(
      "/api/export/analysis",
      expect.objectContaining({
        method: "POST",
        body: JSON.stringify({ profileId: "audit-pack" })
      })
    );
  });

  it("surfaces VM-owned running export feedback in the report-pack task", async () => {
    const user = userEvent.setup();
    let releaseFetch!: () => void;
    fetchMock.mockImplementationOnce(
      () =>
        new Promise((resolve) => {
          releaseFetch = () =>
            resolve({
              ok: true,
              text: async () => JSON.stringify({
                jobId: "export-running",
                success: true,
                status: "completed",
                profileId: "excel",
                symbols: [],
                filesGenerated: 1,
                totalRecords: 1,
                totalBytes: 1,
                outputDirectory: "exports",
                durationSeconds: 1,
                error: null,
                warnings: [],
                files: [],
                timestamp: "2026-05-01T00:00:00Z"
              })
            });
        })
    );

    renderWithRouter(<ReportingScreen data={governance} />, { initialEntries: ["/reporting/report-packs"] });

    const task = screen.getByRole("region", { name: "Report-pack approval task" });
    await user.click(within(task).getByRole("button", { name: "Run Excel export analysis" }));

    const runningButton = within(task).getByRole("button", { name: "Run Excel export analysis" });
    expect(runningButton).toBeDisabled();
    expect(runningButton).toHaveAttribute("aria-busy", "true");
    expect(runningButton).toHaveAttribute("title", "Excel export is already running.");
    expect(runningButton).toHaveTextContent("Running export…");
    expect(within(task).getByText("Excel export is running. Wait for the result before starting another export.")).toBeInTheDocument();

    releaseFetch();
    await waitFor(() => {
      expect(screen.getByRole("status", { name: "Reporting export status" })).toHaveTextContent(
        "Excel export completed — 1 file generated."
      );
    });
  });

  it("updates selected profile detail and profile-scoped actions", async () => {
    const user = userEvent.setup();
    renderWithRouter(<ReportingScreen data={governance} />, { initialEntries: ["/reporting"] });

    const auditButton = screen.getByRole("button", { name: /select audit pack export profile/i });
    await user.click(auditButton);

    expect(auditButton).toHaveAttribute("aria-pressed", "true");
    expect(auditButton).toHaveAttribute("aria-controls", "reporting-profile-detail");
    expect(auditButton).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByRole("button", { name: /select excel export profile/i })).toHaveAttribute("aria-expanded", "false");
    const inspector = screen.getByRole("complementary", { name: /audit pack selected/i });
    expect(inspector).toBeInTheDocument();
    expect(screen.getByRole("status", { name: /audit pack readiness/i })).toHaveTextContent(
      "Loader and dictionary evidence are ready"
    );
    expect(within(inspector).getByText("Profile ID")).toBeInTheDocument();
    expect(within(inspector).getByText("audit-pack")).toBeInTheDocument();

    const preview = screen.getByRole("link", { name: "Preview Audit Pack export payload" });
    const run = screen.getByRole("button", { name: "Run Audit Pack export analysis" });

    expect(preview).toHaveAttribute("href", "/api/export/preview?profile=audit-pack");
    expect(run).toBeEnabled();
  });

  it("surfaces VM-owned running export feedback in the selected profile inspector", async () => {
    const user = userEvent.setup();
    let releaseFetch!: () => void;
    fetchMock.mockImplementationOnce(
      () =>
        new Promise((resolve) => {
          releaseFetch = () =>
            resolve({
              ok: true,
              text: async () => JSON.stringify({
                jobId: "export-running",
                success: true,
                status: "completed",
                profileId: "audit-pack",
                symbols: [],
                filesGenerated: 1,
                totalRecords: 1,
                totalBytes: 1,
                outputDirectory: "exports",
                durationSeconds: 1,
                error: null,
                warnings: [],
                files: [],
                timestamp: "2026-05-01T00:00:00Z"
              })
            });
        })
    );

    renderWithRouter(<ReportingScreen data={governance} />, { initialEntries: ["/reporting"] });

    await user.click(screen.getByRole("button", { name: /select audit pack export profile/i }));
    const inspector = screen.getByRole("complementary", { name: /audit pack selected/i });
    await user.click(within(inspector).getByRole("button", { name: "Run Audit Pack export analysis" }));

    const runningButton = within(inspector).getByRole("button", { name: "Run Audit Pack export analysis" });
    expect(runningButton).toBeDisabled();
    expect(runningButton).toHaveAttribute("aria-busy", "true");
    expect(runningButton).toHaveAttribute("title", "Audit Pack export is already running.");
    expect(runningButton).toHaveTextContent("Running export…");
    expect(within(inspector).getByText("Audit Pack export is running. Wait for the result before starting another export.")).toBeInTheDocument();

    releaseFetch();
    await waitFor(() => {
      expect(screen.getByRole("status", { name: "Reporting export status" })).toHaveTextContent(
        "Audit Pack export completed — 1 file generated."
      );
    });
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
        "Audit Pack export completed — 2 files generated."
      );
    });
    const exportStatus = screen.getByRole("status", { name: "Reporting export status" });
    expect(within(exportStatus).getByText("Job ID")).toBeInTheDocument();
    expect(within(exportStatus).getByText("export-1")).toBeInTheDocument();
    expect(within(exportStatus).getByText("Output")).toBeInTheDocument();
    expect(within(exportStatus).getByText("exports")).toBeInTheDocument();
    expect(within(exportStatus).getByText("Records")).toBeInTheDocument();
    expect(within(exportStatus).getByText("20")).toBeInTheDocument();
    expect(within(exportStatus).getByText("SPY markdown")).toBeInTheDocument();
    expect(within(exportStatus).getByText(/audit\/export-1\.md/)).toBeInTheDocument();
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
    expect(screen.getByRole("status", { name: "No report-pack targets loaded" })).toHaveTextContent(
      "No report-pack targets loaded. Configure governed targets in the governance policy before approving this packet."
    );
  });

  it("renders report-pack approval task empty targets and profiles as accessible status panels", () => {
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

    renderWithRouter(<ReportingScreen data={emptyGovernance} />, { initialEntries: ["/reporting/report-packs"] });

    const task = screen.getByRole("region", { name: "Report-pack approval task" });
    expect(within(task).getByRole("status", { name: "No report-pack approval targets" })).toHaveTextContent(
      "No report-pack targets loaded. Configure governed targets before approving this packet."
    );
    expect(within(task).getByRole("status", { name: "No report-pack export profiles" })).toHaveTextContent(
      "No export profiles are configured. Add a governed profile before report-pack approval."
    );
    expect(within(task).queryByRole("list", { name: "Report-pack export profiles" })).not.toBeInTheDocument();
  });
});
