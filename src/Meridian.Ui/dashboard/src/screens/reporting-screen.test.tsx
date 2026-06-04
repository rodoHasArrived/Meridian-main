import { beforeEach, describe, expect, it, vi } from "vitest";
import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ReportingScreen } from "@/screens/reporting-screen";
import { renderWithRouter } from "@/test/render";
import type { AccountingWorkspaceResponse } from "@/types";

const accounting: AccountingWorkspaceResponse = {
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
    reportPackDistributions: [
      {
        distributionId: "board-reporting-committee",
        recipient: "Board reporting committee",
        recipientRole: "Board",
        channel: "Board portal",
        state: "Pending approval",
        pendingItems: 1,
        pendingSummary: "1 report pack still needs approval before Board reporting committee delivery.",
        owner: "fund-controller",
        dueAtUtc: "2026-05-03T20:00:00Z",
        lastSentAtUtc: null,
        route: "/reporting/report-packs?recipient=board"
      },
      {
        distributionId: "compliance-archive",
        recipient: "Compliance archive",
        recipientRole: "Compliance",
        channel: "Retained evidence vault",
        state: "No package queued",
        pendingItems: 0,
        pendingSummary: "No governed report pack is queued for Compliance archive.",
        owner: "compliance-reviewer",
        dueAtUtc: null,
        lastSentAtUtc: null,
        route: "/reporting/evidence?subject=report-pack"
      }
    ],
    summary: "2 export profiles available.",
    templates: [
      {
        templateId: "investor-monthly-statement",
        family: "InvestorStatement",
        name: "Investor Monthly Statement",
        version: "1.0.0",
        sections: ["cover", "performance"],
        lifecycleStatus: "Approved",
        isBuiltIn: true,
        isLatestApproved: true,
        approvalSummary: "Built-in approved template for InvestorStatement.",
        authoringRoute: "/api/fund-structure/reporting/templates/investor-monthly-statement/versions/1"
      }
    ]
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

  it("renders loading copy when reporting data is unavailable", () => {
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

  it("renders report-pack distribution recipients with accessible row labels", () => {
    renderWithRouter(<ReportingScreen data={accounting} />, { initialEntries: ["/reporting"] });

    expect(screen.getByRole("list", { name: "Report-pack distribution recipients" })).toBeInTheDocument();
    expect(screen.getByLabelText(
      "Board reporting committee report-pack distribution: 1 report pack still needs approval before Board reporting committee delivery."
    )).toBeInTheDocument();
    expect(screen.getByLabelText(
      "Compliance archive report-pack distribution: No governed report pack is queued for Compliance archive."
    )).toBeInTheDocument();
  });

  it("renders template designer lifecycle controls for governed versions", () => {
    renderWithRouter(<ReportingScreen data={accounting} />, { initialEntries: ["/reporting"] });

    expect(screen.getByText("Investor Monthly Statement")).toBeInTheDocument();
    expect(screen.getByText("Approved")).toBeInTheDocument();
    expect(screen.getByText("Built-in")).toBeInTheDocument();
    expect(screen.getByText("Built-in approved template for InvestorStatement.")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Draft a revision of Investor Monthly Statement" })).toHaveAttribute(
      "href",
      "/api/fund-structure/reporting/templates/investor-monthly-statement/versions/1"
    );
  });

  it("renders typed report run drilldown links and next action references", () => {
    const accountingWithRunLinks: AccountingWorkspaceResponse = {
      ...accounting,
      reporting: {
        ...accounting.reporting,
        recentRuns: [
          {
            runId: "investor-monthly-statement-20260501",
            templateId: "investor-monthly-statement",
            family: "InvestorStatement",
            status: "InReview",
            trigger: "Scheduled",
            attemptCount: 1,
            sectionCount: 2,
            lineageLinkedSections: 2,
            artifacts: ["manifest.json"],
            auditActions: ["RunGenerated", "ApprovalTransition"],
            failureReason: null,
            drilldownLinks: [
              {
                id: "investor-monthly-statement-20260501:evidence",
                kind: "evidence",
                label: "Evidence bundle",
                href: "/api/fund-structure/report-packs/report-1/evidence-bundle",
                method: "GET",
                isBrowserNavigable: true,
                source: "ReportPackWorkflow"
              },
              {
                id: "investor-monthly-statement-20260501:audit",
                kind: "audit",
                label: "Approval audit trail",
                href: "reporting-run://investor-monthly-statement-20260501/audit",
                method: "GET",
                isBrowserNavigable: false,
                source: "ReportingOrchestration"
              }
            ],
            nextActions: [
              {
                id: "investor-monthly-statement-20260501:approve",
                kind: "approval",
                label: "Approve reporting run",
                href: "reporting-run://investor-monthly-statement-20260501/approval/approve",
                method: "POST",
                isEnabled: true,
                disabledReason: null,
                isBrowserNavigable: false
              }
            ]
          }
        ]
      }
    };

    renderWithRouter(<ReportingScreen data={accountingWithRunLinks} />, { initialEntries: ["/reporting"] });

    expect(screen.getByRole("link", {
      name: "GET /api/fund-structure/report-packs/report-1/evidence-bundle for Evidence bundle"
    })).toHaveAttribute("href", "/api/fund-structure/report-packs/report-1/evidence-bundle");
    expect(screen.getByRole("group", {
      name: "Reference-only GET reporting-run://investor-monthly-statement-20260501/audit for Approval audit trail"
    })).toHaveTextContent("Approval audit trail");
    expect(screen.getByRole("group", {
      name: "POST reporting-run://investor-monthly-statement-20260501/approval/approve for Approve reporting run"
    })).toHaveTextContent("Approve reporting run");
  });

  it("renders the VM-owned no-target state with warning token classes", () => {
    const missingTargets: AccountingWorkspaceResponse = {
      ...accounting,
      reporting: {
        ...accounting.reporting,
        reportPackDistributions: []
      }
    };

    renderWithRouter(<ReportingScreen data={missingTargets} />, { initialEntries: ["/reporting"] });

    const emptyState = screen.getByRole("status", { name: "No report-pack recipients loaded" });
    expect(emptyState).toHaveTextContent(
      "No report-pack recipients loaded. Configure distribution records before approving this packet."
    );
    expect(emptyState).toHaveClass("border-warning/30", "bg-warning/10", "text-warning");
    expect(screen.queryByRole("list", { name: "Report-pack distribution recipients" })).not.toBeInTheDocument();
  });

  it("renders the dedicated report-pack approval workflow panel", async () => {
    const user = userEvent.setup();
    renderWithRouter(<ReportingScreen data={accounting} />, { initialEntries: ["/reporting/report-packs"] });

    const task = screen.getByRole("region", { name: "Report-pack approval task" });
    expect(task).toBeInTheDocument();
    expect(within(task).getByText("Report-pack approval")).toBeInTheDocument();
    expect(within(task).getByRole("list", { name: "Report-pack distribution recipients" })).toBeInTheDocument();
    expect(within(task).getByRole("list", { name: "Selected report-pack export actions" })).toBeInTheDocument();
    const excelPreviewLink = within(task).getByRole("link", { name: "Preview Excel export payload" });
    expect(excelPreviewLink).toHaveAttribute("href", "/api/export/preview?profile=excel");
    expect(excelPreviewLink).toHaveAttribute("aria-describedby", "reporting-action-excel-preview-report-pack-task-status");
    expect(within(task).getByText("Opens the current export payload preview in a new browser tab.")).toHaveAttribute(
      "id",
      "reporting-action-excel-preview-report-pack-task-status"
    );
    const excelProfile = within(task).getByRole("button", { name: "Select Excel for report-pack approval" });
    const auditProfile = within(task).getByRole("button", { name: "Select Audit Pack for report-pack approval" });
    expect(excelProfile).toHaveAttribute(
      "aria-controls",
      "report-pack-profile-selected-summary report-pack-profile-actions report-pack-profile-backend-links"
    );
    expect(excelProfile).toHaveAttribute("aria-expanded", "true");
    expect(excelProfile).toHaveAttribute("aria-describedby", "report-pack-profile-excel-description");
    expect(excelProfile).toHaveAttribute("tabindex", "0");
    expect(auditProfile).toHaveAttribute("aria-expanded", "false");
    expect(auditProfile).toHaveAttribute("tabindex", "-1");
    expect(within(task).getByLabelText("Excel export preview uses GET")).toHaveTextContent("GET");
    const gatedExcelExport = within(task).getByRole("button", {
      name: "Run Excel export analysis unavailable until required evidence is attached"
    });
    expect(gatedExcelExport).toBeDisabled();
    expect(gatedExcelExport).toHaveAttribute(
      "title",
      "Excel export requires loader automation evidence before running a governed POST export. Preview remains available."
    );
    expect(within(task).getByLabelText("Excel export analysis is gated by missing evidence")).toHaveTextContent("Gated");
    expect(within(task).getByRole("link", { name: "GET /api/fund-structure/report-packs for Report-pack catalog" })).toHaveAttribute(
      "href",
      "/api/fund-structure/report-packs"
    );
    expect(within(task).queryByRole("link", { name: "POST /api/export/analysis for Excel export analysis" })).toBeNull();
    expect(within(task).getByRole("group", { name: "Reference-only POST /api/export/analysis for Excel export analysis" })).toHaveTextContent(
      "Reference"
    );

    excelProfile.focus();
    expect(excelProfile).toHaveFocus();
    await user.keyboard("{ArrowRight}");

    expect(auditProfile).toHaveFocus();
    expect(auditProfile).toHaveAttribute("aria-pressed", "true");
    expect(auditProfile).toHaveAttribute("aria-expanded", "true");
    expect(excelProfile).toHaveAttribute("tabindex", "-1");
    expect(auditProfile).toHaveAttribute("tabindex", "0");

    expect(within(task).getByRole("status", { name: "Selected report-pack profile" })).toHaveTextContent(
      "Audit Pack is selected for report-pack approval using Markdown output to Audit portal."
    );
    expect(within(task).getByRole("link", { name: "GET /api/export/preview?profile=audit-pack for Audit Pack export preview" })).toHaveAttribute(
      "href",
      "/api/export/preview?profile=audit-pack"
    );
    const auditPreviewLink = within(task).getByRole("link", { name: "Preview Audit Pack export payload" });
    expect(auditPreviewLink).toHaveAttribute("href", "/api/export/preview?profile=audit-pack");
    expect(auditPreviewLink).toHaveAttribute("aria-describedby", "reporting-action-audit-pack-preview-report-pack-task-status");
    expect(within(task).getByRole("group", { name: "Reference-only POST /api/export/analysis for Audit Pack export analysis" })).toHaveTextContent(
      "Reference"
    );
    expect(within(task).getByRole("button", { name: "Run Audit Pack export analysis" })).toBeEnabled();

    await user.click(within(task).getByRole("button", { name: "Run Audit Pack export analysis" }));

    expect(fetchMock).toHaveBeenCalledWith(
      "/api/export/analysis",
      expect.objectContaining({
        method: "POST",
        body: JSON.stringify({ profileId: "audit-pack" })
      })
    );
  });

  it("renders report-pack restatement review from shared workflow records", () => {
    const restatedAccounting: AccountingWorkspaceResponse = {
      ...accounting,
      reporting: {
        ...accounting.reporting,
        workflowRecords: [
          {
            reportId: "report-restated-1",
            fundProfileId: "fund-alpha",
            fundAccountId: "account-alpha",
            period: "2026-05",
            templateId: { name: "monthly-board-pack", version: 1 },
            state: "Restated",
            version: 2,
            createdAt: "2026-05-27T10:00:00Z",
            createdBy: "reporter",
            updatedAt: "2026-05-28T12:00:00Z",
            auditTrail: [
              {
                at: "2026-05-28T12:00:00Z",
                actor: "approver",
                action: "restated",
                fromState: "Published",
                toState: "Restated",
                note: "pricing-correction"
              }
            ],
            restatement: {
              reasonCode: "pricing-correction",
              approver: "fund-controller",
              priorVersionReportId: "report-published-1",
              changedLines: [
                {
                  lineKey: "nav.total",
                  previousValue: "1250000",
                  currentValue: "1249500",
                  evidenceLinks: [
                    {
                      evidenceId: "pricing-evidence-1",
                      label: "Pricing override",
                      route: "/reporting/evidence?subject=pricing",
                      source: "pricing",
                      capturedAtUtc: "2026-05-28T11:59:00Z"
                    }
                  ]
                }
              ],
              evidenceLinks: null
            },
            lineProvenance: [],
            publication: {
              manifestId: "manifest-restated-1",
              retainedManifestPath: "vault/report-packs/manifest-restated-1.json",
              evidenceHash: "sha256:restated123",
              signedOffBy: "reporting-ops",
              signedOffAt: "2026-05-28T15:20:00Z",
              evidenceLinks: [
                {
                  evidenceId: "publication-evidence-1",
                  label: "Publication manifest",
                  route: "/reporting/manifests/manifest-restated-1",
                  source: "reporting",
                  capturedAtUtc: "2026-05-28T15:20:00Z"
                }
              ]
            }
          }
        ]
      }
    };

    renderWithRouter(<ReportingScreen data={restatedAccounting} />, { initialEntries: ["/reporting/report-packs"] });

    const restatement = screen.getByRole("region", { name: "Report-pack restatement review" });
    expect(within(restatement).getByText("Restatement review")).toBeInTheDocument();
    expect(within(restatement).getByText("pricing-correction approved by fund-controller.")).toBeInTheDocument();
    expect(within(restatement).getByText("report-restated-1")).toBeInTheDocument();
    expect(within(restatement).getByRole("list", { name: "Restatement changed lines" })).toBeInTheDocument();
    expect(within(restatement).getByLabelText("nav.total changed from 1250000 to 1249500 with 1 evidence link")).toHaveTextContent(
      "1250000 -> 1249500"
    );
    expect(within(restatement).getByRole("link", { name: "Open evidence for nav.total" })).toHaveAttribute(
      "href",
      "/reporting/evidence?subject=pricing"
    );

    const publication = screen.getByRole("region", { name: "Report-pack publication review" });
    expect(within(publication).getByText("Publication review")).toBeInTheDocument();
    expect(within(publication).getByText("manifest-restated-1 signed off by reporting-ops at 2026-05-28T15:20:00Z.")).toBeInTheDocument();
    expect(within(publication).getByText("sha256:restated123")).toBeInTheDocument();
    expect(within(publication).getByText("vault/report-packs/manifest-restated-1.json")).toBeInTheDocument();
  });

  it("keeps report-pack task and inspector action descriptions uniquely identified", () => {
    const { container } = renderWithRouter(<ReportingScreen data={accounting} />, {
      initialEntries: ["/reporting/report-packs"]
    });

    const task = screen.getByRole("region", { name: "Report-pack approval task" });
    expect(within(task).getByRole("link", { name: "Preview Excel export payload" })).toHaveAttribute(
      "aria-describedby",
      "reporting-action-excel-preview-report-pack-task-status"
    );
    expect(screen.getByRole("link", { name: "Open Excel report-pack evidence" })).toBeInTheDocument();
    expect(screen.getByRole("list", { name: "Excel export actions" }))
      .toHaveTextContent("Opens the current export payload preview in a new browser tab.");

    const ids = Array.from(container.querySelectorAll<HTMLElement>("[id]")).map((element) => element.id);
    const duplicateIds = ids.filter((id, index) => ids.indexOf(id) !== index);
    expect(duplicateIds).toEqual([]);
    expect(container.querySelector("#reporting-action-excel-preview-profile-detail-status")).toBeInTheDocument();
    expect(container.querySelector("#reporting-action-excel-preview-report-pack-task-status")).toBeInTheDocument();
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

    renderWithRouter(<ReportingScreen data={accounting} />, { initialEntries: ["/reporting/report-packs"] });

    const task = screen.getByRole("region", { name: "Report-pack approval task" });
    await user.click(within(task).getByRole("button", { name: "Select Audit Pack for report-pack approval" }));
    await user.click(within(task).getByRole("button", { name: "Run Audit Pack export analysis" }));

    const runningButton = within(task).getByRole("button", { name: "Run Audit Pack export analysis" });
    expect(runningButton).toBeDisabled();
    expect(runningButton).toHaveAttribute("aria-busy", "true");
    expect(runningButton).toHaveAttribute("title", "Audit Pack export is already running.");
    expect(runningButton).toHaveTextContent("Running export…");
    expect(within(task).getByLabelText("Audit Pack export is running")).toHaveTextContent("Running");
    expect(within(task).getByText("Audit Pack export is running. Wait for the result before starting another export.")).toBeInTheDocument();

    releaseFetch();
    await waitFor(() => {
      expect(screen.getByRole("status", { name: "Reporting export status" })).toHaveTextContent(
        "Audit Pack export completed — 1 file generated."
      );
    });
  });

  it("updates selected profile detail and profile-scoped actions", async () => {
    const user = userEvent.setup();
    renderWithRouter(<ReportingScreen data={accounting} />, { initialEntries: ["/reporting"] });

    const profileTable = screen.getByRole("table", { name: "Export profiles" });
    const auditRow = within(profileTable).getByRole("row", { name: /select audit pack export profile/i });
    await user.click(auditRow);

    expect(auditRow).toHaveAttribute("aria-selected", "true");
    expect(auditRow).toHaveAttribute("aria-controls", "reporting-profile-detail");
    expect(auditRow).toHaveAttribute("aria-expanded", "true");
    expect(within(profileTable).getByRole("row", { name: /select excel export profile/i })).toHaveAttribute("aria-expanded", "false");
    const inspector = screen.getByRole("region", { name: /audit pack selected/i });
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

    renderWithRouter(<ReportingScreen data={accounting} />, { initialEntries: ["/reporting"] });

    await user.click(screen.getByRole("row", { name: /select audit pack export profile/i }));
    const inspector = screen.getByRole("region", { name: /audit pack selected/i });
    await user.click(within(inspector).getByRole("button", { name: "Run Audit Pack export analysis" }));

    const runningButton = within(inspector).getByRole("button", { name: "Run Audit Pack export analysis" });
    expect(runningButton).toBeDisabled();
    expect(runningButton).toHaveAttribute("aria-busy", "true");
    expect(runningButton).toHaveAttribute("title", "Audit Pack export is already running.");
    expect(runningButton).toHaveTextContent("Running export…");
    expect(within(inspector).getByLabelText("Audit Pack export is running")).toHaveTextContent("Running");
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
    renderWithRouter(<ReportingScreen data={accounting} />, { initialEntries: ["/reporting"] });

    await user.click(screen.getByRole("row", { name: /select audit pack export profile/i }));
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

  it("renders structured backend validation detail for export failures", async () => {
    const user = userEvent.setup();
    fetchMock.mockResolvedValueOnce({
      ok: false,
      status: 400,
      text: async () => JSON.stringify({
        title: "Validation failed",
        detail: "One or more validation errors occurred.",
        errors: {
          profileId: ["Profile is required."],
          approvalReason: ["Approval reason must cite packet evidence."]
        }
      })
    });

    renderWithRouter(<ReportingScreen data={accounting} />, { initialEntries: ["/reporting"] });

    await user.click(screen.getByRole("row", { name: /select audit pack export profile/i }));
    await user.click(screen.getByRole("button", { name: "Run Audit Pack export analysis" }));

    const exportStatus = await screen.findByRole("status", { name: "Reporting export status" });
    expect(within(exportStatus).getByText("Audit Pack export failed. One or more validation errors occurred.")).toBeInTheDocument();
    expect(within(exportStatus).getByText("Endpoint returned 400 for /api/export/analysis.")).toBeInTheDocument();
    expect(within(exportStatus).getByText("Validation failed")).toBeInTheDocument();
    expect(within(exportStatus).getByText("profileId: Profile is required.")).toBeInTheDocument();
    expect(within(exportStatus).getByText("approvalReason: Approval reason must cite packet evidence.")).toBeInTheDocument();
  });

  it("renders explicit empty states for missing reporting profiles and recipients", () => {
    const emptyAccounting: AccountingWorkspaceResponse = {
      ...accounting,
      reporting: {
        profileCount: 0,
        recommendedProfiles: [],
        profiles: [],
        reportPackDistributions: [],
        summary: "No reporting profiles configured."
      }
    };

    renderWithRouter(<ReportingScreen data={emptyAccounting} />, { initialEntries: ["/reporting"] });

    expect(screen.getByText(/no export profiles are configured/i)).toBeInTheDocument();
    expect(screen.getByRole("status", { name: "No report-pack recipients loaded" })).toHaveTextContent(
      "No report-pack recipients loaded. Configure distribution records before approving this packet."
    );
  });

  it("renders report-pack approval task empty recipients and profiles as accessible status panels", () => {
    const emptyAccounting: AccountingWorkspaceResponse = {
      ...accounting,
      reporting: {
        profileCount: 0,
        recommendedProfiles: [],
        profiles: [],
        reportPackDistributions: [],
        summary: "No reporting profiles configured."
      }
    };

    renderWithRouter(<ReportingScreen data={emptyAccounting} />, { initialEntries: ["/reporting/report-packs"] });

    const task = screen.getByRole("region", { name: "Report-pack approval task" });
    expect(within(task).getByRole("status", { name: "No report-pack distribution recipients" })).toHaveTextContent(
      "No report-pack recipients loaded. Configure distribution records before approving this packet."
    );
    expect(within(task).getByRole("status", { name: "No report-pack export profiles" })).toHaveTextContent(
      "No export profiles are configured. Add a governed profile before report-pack approval."
    );
    expect(within(task).queryByRole("list", { name: "Report-pack export profiles" })).not.toBeInTheDocument();
  });
});
