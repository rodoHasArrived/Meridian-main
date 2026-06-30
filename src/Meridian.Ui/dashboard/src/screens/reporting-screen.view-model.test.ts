import { describe, expect, it } from "vitest";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { renderHook, act, waitFor } from "@testing-library/react";
import { createApiErrorFromResponseBody } from "@/lib/api-errors";
import {
  buildRestatementReviewPanel,
  resolveReportPackProfileKeyCommand,
  useReportingScreenViewModel
} from "@/screens/reporting-screen.view-model";
import { reportingTaskModeLauncherLinks } from "@/screens/reporting-screen.task-mode-view-model";
import type { ExportAnalysisResult, GovernanceReportingSummary } from "@/types";

const reporting: GovernanceReportingSummary = {
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
      id: "csv",
      name: "CSV",
      targetTool: "Generic",
      format: "Csv",
      description: "Flat file export for bulk ingestion.",
      loaderScript: true,
      dataDictionary: false
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
  accessAudit: {
    evaluationScope: "CallerScoped",
    summary: "Reporting access policy hid 4 object(s) outside the caller's user, group, or company scope.",
    principalScopes: ["user:viewer.user", "group:ops-control", "company:company-alpha"],
    visibleTemplateCount: 2,
    hiddenTemplateCount: 1,
    visibleReportPackCount: 3,
    hiddenReportPackCount: 1,
    visibleScheduleCount: 1,
    hiddenScheduleCount: 0,
    visibleDeliveryAttemptCount: 2,
    hiddenDeliveryAttemptCount: 1,
    visibleStructuredExportCount: 5,
    hiddenStructuredExportCount: 1,
    denialReasons: [
      "Some report templates are private or restricted to other users, groups, or companies.",
      "Some delivery attempts are hidden because their report pack or template is outside the caller's report access scope."
    ]
  },
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
  ],
  recentRuns: [
    {
      runId: "investor-monthly-statement-20260501-v2",
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
      generatedReportWriterGrids: [
        {
          gridId: "sector-pivot",
          title: "Sector Pivot",
          kind: "Pivot",
          artifact: "report-writer://investor-monthly-statement-20260501-v2/grids/sector-pivot",
          dimensionCount: 2,
          metricCount: 2,
          formulaCount: 1
        }
      ],
      reportWriterDatasetSourceId: "portfolio-reporting-cuts",
      reportWriterDatasetSourceLabel: "Portfolio reporting cuts",
      reportWriterDatasetRowCount: 2,
      runSeriesId: "investor-monthly-statement-20260501",
      runAttemptOrdinal: 2,
      priorRunId: "investor-monthly-statement-20260501",
      retryReason: "corrected portfolio marks",
      latestGeneratedRunId: "investor-monthly-statement-20260501-v2",
      latestApprovedRunId: "investor-monthly-statement-20260501",
      isLatestGenerated: true,
      isLatestApproved: false,
      comparisonSummary: "1 changed, 1 added, 0 removed lines compared with investor-monthly-statement-20260501.",
      changedLineCount: 1,
      addedLineCount: 1,
      removedLineCount: 0,
      drilldownLinks: [
        {
          id: "investor-monthly-statement-20260501-v2:evidence",
          kind: "evidence",
          label: "Evidence bundle",
          href: "/api/fund-structure/report-packs/report-1/evidence-bundle",
          method: "GET",
          isBrowserNavigable: true,
          source: "ReportPackWorkflow"
        },
        {
          id: "investor-monthly-statement-20260501-v2:audit",
          kind: "audit",
          label: "Approval audit trail",
          href: "reporting-run://investor-monthly-statement-20260501-v2/audit",
          method: "GET",
          isBrowserNavigable: false,
          source: "ReportingOrchestration"
        }
      ],
      nextActions: [
        {
          id: "investor-monthly-statement-20260501-v2:approve",
          kind: "approval",
          label: "Approve reporting run",
          href: "reporting-run://investor-monthly-statement-20260501-v2/approval/approve",
          method: "POST",
          isEnabled: true,
          disabledReason: null,
          isBrowserNavigable: false
        }
      ]
    }
  ]
};

const restatedReporting: GovernanceReportingSummary = {
  ...reporting,
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
        evidenceLinks: [
          {
            evidenceId: "pricing-evidence-1",
            label: "Pricing override",
            route: "/reporting/evidence?subject=pricing",
            source: "pricing",
            capturedAtUtc: "2026-05-28T11:59:00Z"
          }
        ]
      },
      lineProvenance: [
        {
          lineKey: "nav.total",
          sourceKind: "ledger",
          sourceId: "ledger-entry-nav",
          evidenceId: "evidence-nav-total",
          runId: "run-restated-1",
          ledgerEntryId: "ledger-entry-nav",
          reconciliationCaseId: null,
          reportValue: "1249500",
          sourceSessionId: null,
          reconciliationRunId: null,
          reconciliationOutcome: "matched",
          financialRecordExplorerId: "ledger",
          financialRecordHref: "/api/workstation/financial-record-explorers/ledger?lineKey=nav.total&sourceId=ledger-entry-nav&evidenceId=evidence-nav-total&runId=run-restated-1"
        }
      ],
      publication: {
        manifestId: "manifest-restated-1",
        retainedManifestPath: "vault/report-packs/manifest-restated-1.json",
        evidenceHash: "sha256:restated123",
        signedOffBy: "reporting-ops",
        signedOffAt: "2026-05-28T15:20:00Z",
        signedOffRole: "Controller",
        signOffReason: "Approved NAV correction package.",
        signOffContext: "Authenticated actor 'reporting-ops' with role 'Controller' approved publication via HumanOperator.",
        actionOrigin: "HumanOperator",
        evidenceLinks: [
          {
            evidenceId: "evidence-nav-total",
            label: "Publication manifest",
            route: "/reporting/manifests/manifest-restated-1",
            source: "reporting",
            capturedAtUtc: "2026-05-28T15:20:00Z"
          }
        ]
      }
    }
  ]
};

function createDeferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason?: unknown) => void;
  const promise = new Promise<T>((promiseResolve, promiseReject) => {
    resolve = promiseResolve;
    reject = promiseReject;
  });

  return { promise, resolve, reject };
}

function buildExportResult(profileId: string, jobId = `export-${profileId}`): ExportAnalysisResult {
  return {
    jobId,
    success: true,
    status: "completed",
    profileId,
    symbols: [],
    filesGenerated: 1,
    totalRecords: 20,
    totalBytes: 1200,
    outputDirectory: "exports",
    durationSeconds: 1,
    error: null,
    warnings: [],
    files: [],
    timestamp: "2026-05-01T00:00:00Z"
  };
}

describe("useReportingScreenViewModel", () => {
  it("keeps Reporting task-mode routing outside the broad reporting view model", () => {
    const viewModelSource = readFileSync(resolve(process.cwd(), "src/screens/reporting-screen.view-model.ts"), "utf8");
    const taskModeSource = readFileSync(resolve(process.cwd(), "src/screens/reporting-screen.task-mode-view-model.ts"), "utf8");

    expect(taskModeSource).toContain("const reportingTaskModeDefinitions");
    expect(taskModeSource).toContain("export const reportingTaskModeLauncherLinks");
    expect(taskModeSource).toContain("export function buildReportingTaskMode");
    expect(taskModeSource).toContain("export function isReportPackRoute");
    expect(viewModelSource).not.toContain("const reportingTaskModeDefinitions");
    expect(viewModelSource).not.toContain("function normalizeReportingPathname");
    expect(reportingTaskModeLauncherLinks.map((mode) => [mode.id, mode.href])).toEqual([
      ["report-builder", "/reporting/report-builder"],
      ["run-status", "/reporting/run-status"],
      ["delivery-evidence", "/reporting/report-packs"],
      ["exports", "/reporting/exports"],
      ["governance", "/reporting/governance"]
    ]);
  });

  it("returns profile rows from reporting data", () => {
    const { result } = renderHook(() => useReportingScreenViewModel(reporting));
    expect(result.current.hasRows).toBe(true);
    expect(result.current.rows).toHaveLength(2);
    expect(result.current.rows[0].name).toBe("Excel");
  });

  it("marks recommended profiles", () => {
    const { result } = renderHook(() => useReportingScreenViewModel(reporting));
    const excelRow = result.current.rows.find((r) => r.id === "excel");
    expect(excelRow?.isRecommended).toBe(true);
    expect(excelRow?.badges.some((b) => b.label === "Recommended")).toBe(true);
  });

  it("marks profiles with loader script and data dictionary badges", () => {
    const { result } = renderHook(() => useReportingScreenViewModel(reporting));
    const excelRow = result.current.rows.find((r) => r.id === "excel");
    const csvRow = result.current.rows.find((r) => r.id === "csv");
    expect(excelRow?.badges.some((b) => b.label === "Dictionary")).toBe(true);
    expect(csvRow?.badges.some((b) => b.label === "Loader")).toBe(true);
  });

  it("surfaces report-pack distribution recipients", () => {
    const { result } = renderHook(() => useReportingScreenViewModel(reporting));
    expect(result.current.hasPackTargets).toBe(true);
    expect(result.current.packTargets.map((target) => target.label)).toEqual(["Board reporting committee", "Compliance archive"]);
    expect(result.current.packTargets[0]).toMatchObject({
      channel: "Board portal",
      stateLabel: "Pending approval",
      pendingItemsLabel: "1 pending",
      pendingSummary: "1 report pack still needs approval before Board reporting committee delivery."
    });
    expect(result.current.packTargets[0].ariaLabel).toBe("Board reporting committee report-pack distribution: 1 report pack still needs approval before Board reporting committee delivery.");
    expect(result.current.packTargetsEmptyState).toEqual({
      text: "No report-pack recipients loaded. Configure distribution records before approving this packet.",
      ariaLabel: "No report-pack recipients loaded"
    });
  });

  it("surfaces template metadata and recent run status projections", () => {
    const { result } = renderHook(() => useReportingScreenViewModel(reporting));

    expect(result.current.templateRows).toHaveLength(1);
    expect(result.current.templateRows[0]).toMatchObject({
      id: "investor-monthly-statement:1.0.0",
      templateName: "investor-monthly-statement",
      name: "Investor Monthly Statement",
      family: "InvestorStatement",
      version: "1.0.0",
      versionNumber: 1,
      sectionSummary: "2 sections",
      statusLabel: "Approved",
      statusVariant: "success",
      sourceLabel: "Built-in",
      approvalSummary: "Built-in approved template for InvestorStatement.",
      accessMode: "CompanyWide",
      accessSummary: "Company-wide access",
      isAccessible: true,
      authoringHref: "/api/fund-structure/reporting/templates/investor-monthly-statement/versions/1",
      actionLabel: "Draft revision",
      actionAriaLabel: "Draft a revision of Investor Monthly Statement",
      canRunOnDemand: true,
      runActionLabel: "Run report",
      runActionAriaLabel: "Run Investor Monthly Statement report on demand",
      runDisabledReason: null,
      lifecycleActions: [],
      hasLifecycleActions: false,
      writerGrids: [],
      hasWriterGrids: false,
      writerGridSummary: "No report-writer grids"
    });
    expect(result.current.runStatusRows[0]).toMatchObject({
      id: "investor-monthly-statement-20260501-v2",
      status: "InReview",
      runSeriesLabel: "investor-monthly-statement-20260501",
      runAttemptLabel: "Version 2",
      latestGeneratedLabel: "Latest generated",
      latestApprovedLabel: "Latest approved: investor-monthly-statement-20260501",
      priorRunLabel: "investor-monthly-statement-20260501",
      retryReasonLabel: "corrected portfolio marks",
      comparisonSummary: "1 changed, 1 added, 0 removed lines compared with investor-monthly-statement-20260501.",
      changedLineLabel: "1 changed · 1 added · 0 removed",
      lineageSummary: "2/2 sections linked",
      generatedGridLabel: "1 generated grid with 1 formula",
      generatedGridNames: ["Sector Pivot (Pivot, 2d/2m/1f)"],
      hasGeneratedGrids: true,
      generatedGridArtifacts: [
        expect.objectContaining({
          label: "Sector Pivot (Pivot)",
          jsonHref: "/api/fund-structure/reporting/runs/investor-monthly-statement-20260501-v2/report-writer-grids/sector-pivot",
          csvHref: "/api/fund-structure/reporting/runs/investor-monthly-statement-20260501-v2/report-writer-grids/sector-pivot?format=csv",
          pdfHref: "/api/fund-structure/reporting/runs/investor-monthly-statement-20260501-v2/report-writer-grids/sector-pivot?format=pdf",
          xlsHref: "/api/fund-structure/reporting/runs/investor-monthly-statement-20260501-v2/report-writer-grids/sector-pivot?format=xls",
          xlsxHref: "/api/fund-structure/reporting/runs/investor-monthly-statement-20260501-v2/report-writer-grids/sector-pivot?format=xlsx"
        })
      ],
      datasetSourceLabel: "Portfolio reporting cuts (2 rows)",
      auditSummary: "RunGenerated → ApprovalTransition",
      hasDrilldownLinks: true,
      hasNextActions: true,
      drilldownLinks: [
        expect.objectContaining({
          kind: "evidence",
          label: "Evidence bundle",
          interactionLabel: "Open service",
          ariaLabel: "Open Evidence bundle service reference"
        }),
        expect.objectContaining({
          kind: "audit",
          label: "Approval audit trail",
          interactionLabel: "Service reference",
          ariaLabel: "Approval audit trail service reference retained for diagnostics"
        })
      ],
      nextActions: [
        expect.objectContaining({
          kind: "approval",
          label: "Approve reporting run",
          method: "POST",
          interactionLabel: "Service reference",
          ariaLabel: "Approve reporting run service reference retained for diagnostics"
        })
      ]
    });
    expect(result.current.hasRunStatusRows).toBe(true);
  });

  it("surfaces aggregate report access audit posture", () => {
    const { result } = renderHook(() => useReportingScreenViewModel(reporting));

    expect(result.current.accessAudit).toMatchObject({
      evaluationScope: "CallerScoped",
      postureLabel: "Scoped",
      postureVariant: "warning",
      hiddenTotalLabel: "4 hidden",
      scopeLabel: "user:viewer.user · group:ops-control · company:company-alpha",
      hasDenialReasons: true
    });
    expect(result.current.accessAudit.countRows).toEqual([
      expect.objectContaining({ id: "templates", visibleLabel: "2 visible", hiddenLabel: "1 hidden", hasHidden: true }),
      expect.objectContaining({ id: "report-packs", visibleLabel: "3 visible", hiddenLabel: "1 hidden", hasHidden: true }),
      expect.objectContaining({ id: "schedules", visibleLabel: "1 visible", hiddenLabel: "0 hidden", hasHidden: false }),
      expect.objectContaining({ id: "deliveries", visibleLabel: "2 visible", hiddenLabel: "1 hidden", hasHidden: true }),
      expect.objectContaining({ id: "structured-exports", visibleLabel: "5 visible", hiddenLabel: "1 hidden", hasHidden: true })
    ]);
  });

  it("surfaces report-template access posture and disables inaccessible runs", () => {
    const restrictedReporting: GovernanceReportingSummary = {
      ...reporting,
      templates: [
        {
          ...reporting.templates![0],
          accessMode: "Private",
          accessSummary: "Private user-locked access owned by report.owner.",
          isAccessible: false
        }
      ]
    };

    const { result } = renderHook(() => useReportingScreenViewModel(restrictedReporting));

    expect(result.current.templateRows[0]).toMatchObject({
      accessMode: "Private",
      accessSummary: "Private user-locked access owned by report.owner.",
      isAccessible: false,
      canRunOnDemand: false,
      lifecycleActions: [],
      hasLifecycleActions: false,
      runDisabledReason: "Current user does not have access to this report template."
    });
  });

  it("summarizes configured reporting schedule delivery targets", () => {
    const scheduledReporting: GovernanceReportingSummary = {
      ...reporting,
      reportWriterDatasetSources: [
        {
          sourceId: "portfolio-reporting-cuts",
          label: "Portfolio reporting cuts",
          description: "Portfolio source rows.",
          rowCount: 2,
          fields: [],
          rows: [],
          tags: ["portfolio-cuts"]
        }
      ],
      schedules: [
        {
          scheduleId: "sched-investor",
          templateId: "investor-monthly-statement",
          cronExpression: "0 8 1 * *",
          nextAsOfDate: "2026-06-01",
          dueAtUtc: "2026-06-01T08:00:00Z",
          maxRetries: 2,
          requestedBy: "fund-controller",
          state: "Active",
          createdAtUtc: "2026-05-01T08:00:00Z",
          updatedAtUtc: "2026-05-03T08:00:00Z",
          lastRunAtUtc: null,
          lastRunId: null,
          runCount: 0,
          description: "Monthly investor statement close packet.",
          datasetSourceId: "portfolio-reporting-cuts",
          deliveryTargets: [
            {
              distributionId: "board-reporting-committee",
              formats: ["Pdf", "Xlsx", "Csv"],
              deliveryMode: "SecurePortal",
              note: "Board package."
            }
          ]
        }
      ]
    };

    const { result } = renderHook(() => useReportingScreenViewModel(scheduledReporting));

    expect(result.current.scheduleRows[0]).toMatchObject({
      id: "sched-investor",
      deliveryTargetLabel: "board-reporting-committee via SecurePortal (Pdf/Xlsx/Csv)",
      datasetSourceLabel: "Portfolio reporting cuts (2)",
      lastRunLabel: "Not run"
    });
  });

  it("surfaces lifecycle actions for custom report-template versions", () => {
    const lifecycleReporting: GovernanceReportingSummary = {
      ...reporting,
      templates: [
        ...(reporting.templates ?? []),
        {
          templateId: "custom-exposure-draft",
          family: "CustomReport",
          name: "Custom Exposure Draft",
          version: "2",
          sections: ["exposures"],
          lifecycleStatus: "Draft",
          isBuiltIn: false,
          isLatestApproved: false,
          approvalSummary: "Draft by reporting.ops.",
          authoringRoute: "/api/fund-structure/reporting/templates/custom-exposure-draft/versions/2"
        },
        {
          templateId: "custom-exposure-review",
          family: "CustomReport",
          name: "Custom Exposure Review",
          version: "3",
          sections: ["exposures"],
          lifecycleStatus: "InReview",
          isBuiltIn: false,
          isLatestApproved: false,
          approvalSummary: "Submitted by reporting.ops.",
          authoringRoute: "/api/fund-structure/reporting/templates/custom-exposure-review/versions/3"
        }
      ]
    };

    const { result } = renderHook(() => useReportingScreenViewModel(lifecycleReporting));

    expect(result.current.templateRows.find((row) => row.templateName === "investor-monthly-statement")).toMatchObject({
      lifecycleActions: [],
      hasLifecycleActions: false
    });
    expect(result.current.templateRows.find((row) => row.templateName === "custom-exposure-draft")).toMatchObject({
      id: "custom-exposure-draft:2",
      versionNumber: 2,
      statusLabel: "Draft",
      lifecycleActions: [
        expect.objectContaining({
          kind: "submit",
          label: "Submit",
          ariaLabel: "Submit Custom Exposure Draft template version 2 for review",
          targetStatus: "InReview"
        })
      ],
      hasLifecycleActions: true
    });
    expect(result.current.templateRows.find((row) => row.templateName === "custom-exposure-review")).toMatchObject({
      id: "custom-exposure-review:3",
      versionNumber: 3,
      statusLabel: "InReview",
      lifecycleActions: [
        expect.objectContaining({ kind: "approve", label: "Approve", targetStatus: "Approved" }),
        expect.objectContaining({ kind: "reject", label: "Reject", targetStatus: "Rejected" })
      ],
      hasLifecycleActions: true
    });
  });

  it("summarizes report-writer grids on governed template rows", () => {
    const withGridTemplate: GovernanceReportingSummary = {
      ...reporting,
      templates: [
        ...(reporting.templates ?? []),
        {
          templateId: "custom-exposure-grid",
          family: "CustomReport",
          name: "Custom Exposure Grid",
          version: "1",
          sections: ["exposures"],
          lifecycleStatus: "Approved",
          isBuiltIn: false,
          isLatestApproved: true,
          approvalSummary: "Approved by controller.admin (APP-GRID-1).",
          authoringRoute: "/api/fund-structure/reporting/templates/custom-exposure-grid/versions/1",
          reportWriterGrids: [
            {
              gridId: "sector-pivot",
              title: "Sector Pivot",
              kind: "Pivot",
              dimensionCount: 1,
              metricCount: 2,
              formulaCount: 1,
              rowFields: ["sector"],
              columnFields: [],
              metrics: [
                { name: "marketValue", sourceField: "marketValue", function: "Sum", label: "Market value" },
                { name: "pnl", sourceField: "pnl", function: "Sum", label: "P&L" }
              ],
              formulas: [
                { name: "returnPct", expression: "{pnl} / {marketValue} * 100", label: "Return %" }
              ],
              topN: null,
              sortBy: "pnl",
              sortDescending: true
            }
          ]
        }
      ]
    };

    const { result } = renderHook(() => useReportingScreenViewModel(withGridTemplate));

    expect(result.current.templateRows.find((row) => row.templateName === "custom-exposure-grid")).toMatchObject({
      id: "custom-exposure-grid:1",
      sectionSummary: "1 section; 1 report-writer grid with 3 metrics",
      sourceLabel: "Custom",
      canRunOnDemand: true,
      runDisabledReason: null,
      hasWriterGrids: true,
      writerGridSummary: "1 grid ready for no-code layout"
    });
    expect(result.current.templateRows.find((row) => row.templateName === "custom-exposure-grid")?.writerGrids[0]).toMatchObject({
      title: "Sector Pivot",
      kind: "Pivot",
      topNLabel: "Pivot",
      sortLabel: "Descending by pnl",
      rowFields: [expect.objectContaining({ label: "sector" })],
      metrics: [
        expect.objectContaining({ label: "Market value", detail: "Sum(marketValue)" }),
        expect.objectContaining({ label: "P&L", detail: "Sum(pnl)" })
      ],
      formulas: [expect.objectContaining({ label: "Return %", detail: "{pnl} / {marketValue} * 100" })]
    });
  });

  it("builds a route-specific report-pack approval task panel", () => {
    const { result } = renderHook(() => useReportingScreenViewModel(reporting, undefined, "/reporting/report-packs"));

    expect(result.current.taskMode).toMatchObject({
      id: "report-pack-approval",
      label: "Delivery Evidence",
      routeLabel: "Delivery Evidence"
    });
    expect(result.current.workflowTaskPanel).toMatchObject({
      regionLabel: "Report-pack approval task",
      title: "Report-pack approval",
      statusLabel: "Evidence review",
      targetsLabel: "Report-pack distribution recipients",
      hasTargets: true,
      profileListLabel: "Report-pack export profiles",
      hasProfiles: true,
      actionListLabel: "Selected report-pack export actions",
      hasActions: true
    });
    expect(result.current.workflowTaskPanel?.actions.map((action) => action.label)).toEqual([
      "Preview export",
      "Run export"
    ]);
    expect(result.current.workflowTaskPanel?.actions.map((action) => action.describedById)).toEqual([
      "reporting-action-excel-preview-report-pack-task-status",
      "reporting-action-excel-run-report-pack-task-status"
    ]);
    expect(result.current.workflowTaskPanel?.actions[1]).toMatchObject({
      id: "run",
      isDisabled: true,
      disabledReason: "Excel export requires loader automation evidence before running governed export analysis. Preview remains available.",
      statusBadgeLabel: "Gated",
      statusBadgeAriaLabel: "Excel export analysis is gated by missing evidence"
    });
    expect(result.current.workflowTaskPanel?.targets.map((target) => target.label)).toEqual([
      "Board reporting committee",
      "Compliance archive"
    ]);
    expect(result.current.workflowTaskPanel?.profiles.map((profile) => profile.readinessLabel)).toEqual([
      "Dictionary only",
      "Loader only"
    ]);
    expect(result.current.workflowTaskPanel?.profiles[0]).toMatchObject({
      id: "excel",
      isSelected: true,
      isExpanded: true,
      controlsId: "report-pack-profile-selected-summary report-pack-profile-actions report-pack-profile-backend-links",
      descriptionId: "report-pack-profile-excel-description",
      tabIndex: 0
    });
    expect(result.current.workflowTaskPanel?.profiles[1]).toMatchObject({
      id: "csv",
      isSelected: false,
      isExpanded: false,
      tabIndex: -1
    });
    expect(result.current.selectedProfile?.title).toBe("Excel");
    expect(result.current.rows.find((row) => row.id === "excel")?.isSelected).toBe(true);
    expect(result.current.workflowTaskPanel?.selectedSummary).toBe(
      "Excel is selected for report-pack approval using Xlsx output to Excel."
    );
    expect(result.current.workflowTaskPanel?.backendLinks.map((link) => link.href)).toEqual([
      "/api/fund-structure/report-packs",
      "/api/fund-structure/report-packs/{reportId}/evidence-bundle",
      "/api/export/preview?profile=excel",
      "/api/export/analysis"
    ]);
    expect(result.current.workflowTaskPanel?.backendLinks.map((link) => link.interactionLabel)).toEqual([
      "Open service",
      "Service reference",
      "Open service",
      "Service reference"
    ]);
    expect(result.current.workflowTaskPanel?.backendLinks.find((link) => link.id === "evidence-bundle")).toMatchObject({
      isBrowserNavigable: false,
      ariaLabel: "Evidence bundle route service reference retained for diagnostics"
    });
    expect(result.current.workflowTaskPanel?.backendLinks.find((link) => link.id === "export-run")).toMatchObject({
      isBrowserNavigable: false,
      ariaLabel: "Excel export analysis service reference retained for diagnostics"
    });
  });

  it("links the report-pack approval task to a retained evidence bundle when a report id is loaded", () => {
    const reportId = "1457ccde-fbb0-4b4d-94e7-9b65be8d86bf";
    const withReportId: GovernanceReportingSummary = {
      ...reporting,
      workflowRecords: [
        {
          reportId,
          fundProfileId: "fund-alpha",
          fundAccountId: "account-alpha",
          period: "2026-05",
          templateId: { name: "monthly-board-pack", version: 1 },
          state: "Published",
          version: 1,
          createdAt: "2026-05-27T10:00:00Z",
          createdBy: "reporter",
          updatedAt: "2026-05-28T12:00:00Z",
          auditTrail: [],
          restatement: null,
          lineProvenance: [],
          publication: null
        }
      ]
    };

    const { result } = renderHook(() => useReportingScreenViewModel(withReportId, undefined, "/reporting/report-packs"));

    expect(result.current.workflowTaskPanel?.backendLinks.find((link) => link.id === "evidence-bundle")).toMatchObject({
      label: "Evidence bundle export",
      href: `/api/fund-structure/report-packs/${reportId}/evidence-bundle`,
      isBrowserNavigable: true,
      interactionLabel: "Open service",
      ariaLabel: "Open Evidence bundle export service reference"
    });
  });

  it("projects report-pack restatement metadata from shared workflow records", () => {
    const { result } = renderHook(() => useReportingScreenViewModel(restatedReporting, undefined, "/reporting/report-packs"));

    expect(result.current.workflowTaskPanel?.restatementReview).toMatchObject({
      regionLabel: "Report-pack restatement review",
      title: "Restatement review",
      statusLabel: "Restated",
      statusVariant: "warning",
      summaryText: "pricing-correction approved by fund-controller.",
      evidenceSummary: "1 evidence link",
      hasChangedLines: true
    });
    expect(result.current.workflowTaskPanel?.restatementReview.fields).toEqual(expect.arrayContaining([
      expect.objectContaining({ label: "Report ID", value: "report-restated-1" }),
      expect.objectContaining({ label: "Prior version", value: "report-published-1" }),
      expect.objectContaining({ label: "Changed lines", value: "1" })
    ]));
    expect(result.current.workflowTaskPanel?.restatementReview.changedLines).toEqual([
      expect.objectContaining({
        lineKey: "nav.total",
        valueBridge: "1250000 -> 1249500",
        evidenceLabel: "1 evidence link",
        evidenceHref: "/reporting/evidence?subject=pricing"
      })
    ]);
  });

  it("projects report-pack publication metadata from shared backend fields", () => {
    const { result } = renderHook(() => useReportingScreenViewModel(restatedReporting, undefined, "/reporting/report-packs"));

    expect(result.current.workflowTaskPanel?.publicationReview).toMatchObject({
      regionLabel: "Report-pack publication review",
      title: "Publication review",
      statusLabel: "Restated",
      statusVariant: "success",
      summaryText: "manifest-restated-1 signed off by reporting-ops at 2026-05-28T15:20:00Z.",
      evidenceSummary: "1 evidence link / 1 provenance line",
      hasLineProvenance: true
    });
    expect(result.current.workflowTaskPanel?.publicationReview.fields).toEqual(expect.arrayContaining([
      expect.objectContaining({ label: "Signed off by", value: "reporting-ops" }),
      expect.objectContaining({ label: "Signed-off role", value: "Controller" }),
      expect.objectContaining({ label: "Sign-off reason", value: "Approved NAV correction package." }),
      expect.objectContaining({ label: "Sign-off context", value: "Authenticated actor 'reporting-ops' with role 'Controller' approved publication via HumanOperator." }),
      expect.objectContaining({ label: "Evidence hash", value: "sha256:restated123" }),
      expect.objectContaining({ label: "Manifest path", value: "vault/report-packs/manifest-restated-1.json" }),
      expect.objectContaining({ label: "Publication time", value: "2026-05-28T15:20:00Z" }),
      expect.objectContaining({ label: "Line provenance", value: "1" })
    ]));
    expect(result.current.workflowTaskPanel?.publicationReview.lineProvenanceRows).toEqual(expect.arrayContaining([
      expect.objectContaining({
        lineKey: "nav.total",
        sourceLabel: "ledger · ledger-entry-nav · ledger=ledger-entry-nav · recon=matched",
        valueLabel: "value 1249500",
        evidenceHref: "/reporting/manifests/manifest-restated-1",
        financialRecordHref: "/api/workstation/financial-record-explorers/ledger?lineKey=nav.total&sourceId=ledger-entry-nav&evidenceId=evidence-nav-total&runId=run-restated-1"
      })
    ]));
  });

  it("builds an empty restatement review state when no restated workflow record is loaded", () => {
    const state = buildRestatementReviewPanel([]);

    expect(state).toMatchObject({
      statusLabel: "No restatements",
      statusVariant: "outline",
      hasChangedLines: false,
      evidenceSummary: "0 evidence links"
    });
    expect(state.fields).toEqual(expect.arrayContaining([
      expect.objectContaining({ label: "Workflow records", value: "0" }),
      expect.objectContaining({ label: "Restated records", value: "0" })
    ]));
  });

  it("lets operators clear the default report-pack profile selection", () => {
    const { result } = renderHook(() => useReportingScreenViewModel(reporting, undefined, "/reporting/report-packs"));

    expect(result.current.selectedProfile?.title).toBe("Excel");

    act(() => { result.current.selectProfile("excel"); });

    expect(result.current.selectedProfile).toBeNull();
    expect(result.current.workflowTaskPanel?.selectedSummary).toBe(
      "Select a profile to enable packet preview and export actions."
    );
    expect(result.current.workflowTaskPanel?.hasActions).toBe(false);
    expect(result.current.workflowTaskPanel?.actionsEmptyText).toBe(
      "Select a report-pack profile before previewing or running export analysis."
    );
    expect(result.current.workflowTaskPanel?.profiles.map((profile) => ({
      id: profile.id,
      tabIndex: profile.tabIndex,
      isExpanded: profile.isExpanded
    }))).toEqual([
      { id: "excel", tabIndex: 0, isExpanded: false },
      { id: "csv", tabIndex: -1, isExpanded: false }
    ]);
  });

  it("moves report-pack profile selection with VM-owned adjacent commands", () => {
    const { result } = renderHook(() => useReportingScreenViewModel(reporting, undefined, "/reporting/report-packs"));

    act(() => { result.current.selectAdjacentReportPackProfile("next"); });

    expect(result.current.selectedProfile?.title).toBe("CSV");
    expect(result.current.workflowTaskPanel?.selectedProfileId).toBe("csv");
    expect(result.current.workflowTaskPanel?.profiles.map((profile) => ({
      id: profile.id,
      tabIndex: profile.tabIndex,
      isExpanded: profile.isExpanded
    }))).toEqual([
      { id: "excel", tabIndex: -1, isExpanded: false },
      { id: "csv", tabIndex: 0, isExpanded: true }
    ]);

    act(() => { result.current.selectAdjacentReportPackProfile("last"); });
    expect(result.current.selectedProfile?.title).toBe("CSV");

    act(() => { result.current.selectAdjacentReportPackProfile("previous"); });
    expect(result.current.selectedProfile?.title).toBe("Excel");
  });

  it("resolves report-pack profile keyboard commands", () => {
    expect(resolveReportPackProfileKeyCommand("ArrowRight")).toBe("next");
    expect(resolveReportPackProfileKeyCommand("ArrowDown")).toBe("next");
    expect(resolveReportPackProfileKeyCommand("ArrowLeft")).toBe("previous");
    expect(resolveReportPackProfileKeyCommand("ArrowUp")).toBe("previous");
    expect(resolveReportPackProfileKeyCommand("Home")).toBe("first");
    expect(resolveReportPackProfileKeyCommand("End")).toBe("last");
    expect(resolveReportPackProfileKeyCommand("Enter")).toBeNull();
  });

  it("prefers a fully wired report-pack profile when no profile is recommended", () => {
    const noRecommendation: GovernanceReportingSummary = {
      ...reporting,
      recommendedProfiles: [],
      profiles: [
        {
          id: "csv",
          name: "CSV",
          targetTool: "Generic",
          format: "Csv",
          description: "Flat file export for bulk ingestion.",
          loaderScript: true,
          dataDictionary: false
        },
        {
          id: "board-packet",
          name: "Board Packet",
          targetTool: "Board portal",
          format: "Markdown",
          description: "Governed board packet.",
          loaderScript: true,
          dataDictionary: true
        }
      ]
    };

    const { result } = renderHook(() => useReportingScreenViewModel(noRecommendation, undefined, "/reporting/report-packs"));

    expect(result.current.selectedProfile?.title).toBe("Board Packet");
    expect(result.current.rows.find((row) => row.id === "board-packet")?.isSelected).toBe(true);
  });

  it("keeps report-pack approval empty-state copy in the view model", () => {
    const emptyReporting: GovernanceReportingSummary = {
      profileCount: 0,
      recommendedProfiles: [],
      profiles: [],
      reportPackTargets: [],
      summary: "No reporting profiles configured."
    };

    const { result } = renderHook(() => useReportingScreenViewModel(emptyReporting, undefined, "/reporting/report-packs"));

    expect(result.current.workflowTaskPanel).toMatchObject({
      statusLabel: "Recipients missing",
      hasTargets: false,
      targetsEmptyText: "No report-pack recipients loaded. Configure distribution records before approving this packet.",
      targetsEmptyAriaLabel: "No report-pack distribution recipients",
      hasProfiles: false,
      profilesEmptyText: "No export profiles are configured. Add a governed profile before report-pack approval.",
      profilesEmptyAriaLabel: "No report-pack export profiles"
    });
  });

  it("scopes report-pack preview backend links to the selected profile", () => {
    const { result } = renderHook(() => useReportingScreenViewModel(reporting, undefined, "/reporting/report-packs"));

    act(() => { result.current.selectProfile("csv"); });

    expect(result.current.workflowTaskPanel?.selectedSummary).toBe(
      "CSV is selected for report-pack approval using Csv output to Generic."
    );
    expect(result.current.workflowTaskPanel?.backendLinks.find((link) => link.id === "export-preview")).toMatchObject({
      href: "/api/export/preview?profile=csv",
      isBrowserNavigable: true,
      interactionLabel: "Open service",
      ariaLabel: "Open CSV export preview service reference"
    });
    expect(result.current.workflowTaskPanel?.actions[0]).toMatchObject({
      id: "preview",
      href: "/api/export/preview?profile=csv",
      ariaLabel: "Preview CSV export"
    });
  });

  it("derives report queue summary chips in the view model", () => {
    const { result } = renderHook(() => useReportingScreenViewModel(reporting));

    expect(result.current.taskMode).toMatchObject({
      id: "daily-reporting-cockpit",
      label: "Daily Reporting Cockpit",
      routeLabel: "Daily Reporting Cockpit"
    });
    expect(result.current.recommendedCountLabel).toBe("1");
    expect(result.current.packTargetCountLabel).toBe("2");
    expect(result.current.workbenchChips).toEqual([
      { label: "Profiles", value: "2 profiles" },
      { label: "Recipients", value: "2" },
      { label: "Recommended", value: "1" },
      { label: "Export route", value: "/api/export/analysis" }
    ]);
    expect(result.current.queueChips).toEqual([
      { label: "Visible", value: "2 of 2" },
      { label: "Recommended", value: "1" },
      { label: "Recipients", value: "2" },
      { label: "List", value: "Export profiles" }
    ]);
    const exportRoute = renderHook(() => useReportingScreenViewModel(reporting, undefined, "/reporting/exports"));
    expect(exportRoute.result.current.taskMode).toMatchObject({
      id: "exports",
      label: "Exports",
      routeLabel: "Exports"
    });
    const reportBuilderRoute = renderHook(() => useReportingScreenViewModel(reporting, undefined, "/reporting/report-builder"));
    expect(reportBuilderRoute.result.current.taskMode).toMatchObject({
      id: "report-builder",
      label: "Report Builder",
      routeLabel: "Report Builder"
    });
    const runStatusRoute = renderHook(() => useReportingScreenViewModel(reporting, undefined, "/reporting/run-status"));
    expect(runStatusRoute.result.current.taskMode).toMatchObject({
      id: "run-status",
      label: "Run Status",
      routeLabel: "Run Status"
    });
    const governanceRoute = renderHook(() => useReportingScreenViewModel(reporting, undefined, "/reporting/governance"));
    expect(governanceRoute.result.current.taskMode).toMatchObject({
      id: "governance",
      label: "Governance",
      routeLabel: "Governance"
    });
    expect(result.current.packTargetChips).toEqual([
      { label: "Recipients", value: "2" },
      { label: "Inspector", value: "No profile selected" }
    ]);
    expect(result.current.workbenchActions).toEqual([
      {
        id: "evidence",
        label: "Evidence",
        href: "/reporting/evidence?subjectKind=report-pack&subjectId=current",
        ariaLabel: "Open current report-pack evidence"
      }
    ]);
    expect(result.current.statusBadgeLabel).toBe("Waiting");
    expect(result.current.statusBadgeVariant).toBe("outline");
    expect(result.current.rows.map((row) => ({
      id: row.id,
      controlsId: row.controlsId,
      isExpanded: row.isExpanded
    }))).toEqual([
      { id: "excel", controlsId: "reporting-profile-detail", isExpanded: false },
      { id: "csv", controlsId: "reporting-profile-detail", isExpanded: false }
    ]);
  });

  it("shows no profile selected state initially", () => {
    const { result } = renderHook(() => useReportingScreenViewModel(reporting));
    expect(result.current.selectedProfile).toBeNull();
    expect(result.current.statusTitle).toBe("No profile selected");
  });

  it("keeps missing reporting data recovery copy scoped to Reporting", () => {
    const { result } = renderHook(() => useReportingScreenViewModel(null));

    expect(result.current.statusDetail).toBe("Reporting data is unavailable. Check the Reporting workspace API connection.");
  });

  it("updates selected profile on selectProfile call", () => {
    const { result } = renderHook(() => useReportingScreenViewModel(reporting));
    act(() => { result.current.selectProfile("excel"); });
    expect(result.current.selectedProfile).not.toBeNull();
    expect(result.current.selectedProfile?.title).toBe("Excel");
    expect(result.current.selectedProfile?.fields[0]).toMatchObject({
      label: "Profile ID",
      value: "excel"
    });
    expect(result.current.selectedProfile?.readinessSummary).toContain("Data dictionary is present");
    expect(result.current.selectedProfile?.actions[0]).toMatchObject({
      id: "preview",
      label: "Preview export",
      href: "/api/export/preview?profile=excel",
      ariaLabel: "Preview Excel export",
      describedById: "reporting-action-excel-preview-profile-detail-status",
      statusText: "Opens the current export preview in a new browser tab.",
      descriptionText: "Opens the current export preview in a new browser tab.",
      statusBadgeLabel: "GET",
      statusBadgeAriaLabel: "Excel export preview uses GET",
      statusBadgeVariant: "outline",
      isDisabled: false,
      variant: "outline",
      method: "GET",
      profileId: "excel"
    });
    expect(result.current.selectedProfile?.actions[1]).toMatchObject({
      id: "run",
      label: "Run export",
      variant: "default",
      isDisabled: true,
      disabledReason: "Excel export requires loader automation evidence before running governed export analysis. Preview remains available.",
      statusBadgeLabel: "Gated",
      statusBadgeAriaLabel: "Excel export analysis is gated by missing evidence",
      descriptionText: "Excel export requires loader automation evidence before running governed export analysis. Preview remains available."
    });
    expect(result.current.rows.find((r) => r.id === "excel")?.isSelected).toBe(true);
    expect(result.current.rows.find((r) => r.id === "excel")?.isExpanded).toBe(true);
    expect(result.current.rows.find((r) => r.id === "csv")?.isExpanded).toBe(false);
    expect(result.current.statusBadgeLabel).toBe("Selected");
    expect(result.current.statusBadgeVariant).toBe("default");
    expect(result.current.workbenchActions).toEqual([
      {
        id: "evidence",
        label: "Profile evidence",
        href: "/reporting/evidence?subjectKind=report-pack&subjectId=excel",
        ariaLabel: "Open Excel report-pack evidence"
      }
    ]);
  });

  it("derives loader and dictionary readiness for fully wired profiles", () => {
    const fullyWired: GovernanceReportingSummary = {
      ...reporting,
      profiles: [
        {
          id: "board-packet",
          name: "Board Packet",
          targetTool: "Board portal",
          format: "Markdown",
          description: "Governed board packet.",
          loaderScript: true,
          dataDictionary: true
        }
      ],
      recommendedProfiles: ["board-packet"]
    };

    const { result } = renderHook(() => useReportingScreenViewModel(fullyWired));
    act(() => { result.current.selectProfile("board-packet"); });

    expect(result.current.selectedProfile?.readinessSummary).toBe(
      "Loader and dictionary evidence are ready for governed packet generation."
    );
    expect(result.current.selectedProfile?.actions[1]).toMatchObject({
      id: "run",
      href: "/api/export/analysis",
      method: "POST",
      profileId: "board-packet",
      isRunning: false,
      isDisabled: false,
      disabledReason: null,
      statusBadgeLabel: "POST"
    });
  });

  it("runs selected export through the view model command state", async () => {
    let releaseExport!: (value: ExportAnalysisResult) => void;
    const runExport = () => new Promise<ExportAnalysisResult>((resolve) => {
      releaseExport = resolve;
    });
    const { result } = renderHook(() => useReportingScreenViewModel(reporting, { runExport }));

    act(() => { result.current.selectProfile("excel"); });

    act(() => {
      void result.current.runExport("excel", "Excel");
    });

    await waitFor(() => {
      expect(result.current.runningProfileId).toBe("excel");
      expect(result.current.exportStatus?.text).toBe("Starting Excel export…");
      expect(result.current.exportStatus?.fields).toEqual([
        { label: "Profile", value: "Excel", tone: "default", className: "text-foreground" },
        { label: "State", value: "Running", tone: "warning", className: "text-warning" }
      ]);
      expect(result.current.exportStatus?.artifacts).toEqual([]);
      expect(result.current.selectedProfile?.actions[1]).toMatchObject({
        label: "Running export…",
        isDisabled: true,
        isRunning: true,
        busyLabel: "Running export…",
        disabledReason: "Excel export is already running.",
        statusBadgeLabel: "Running",
        statusBadgeAriaLabel: "Excel export is running",
        statusBadgeVariant: "warning",
        descriptionText: "Excel export is running. Wait for the result before starting another export.",
        describedById: "reporting-action-excel-run-profile-detail-status",
        statusText: "Excel export is running. Wait for the result before starting another export."
      });
    });

    await act(async () => {
      releaseExport({
        jobId: "export-1",
        success: true,
        status: "completed",
        profileId: "excel",
        symbols: [],
        filesGenerated: 1,
        totalRecords: 20,
        totalBytes: 1200,
        outputDirectory: "exports",
        durationSeconds: 1,
        error: null,
        warnings: [],
        files: [
          {
            path: "excel/export-1.xlsx",
            symbol: "SPY",
            format: "xlsx",
            sizeBytes: 1200,
            recordCount: 20
          }
        ],
        timestamp: "2026-05-01T00:00:00Z"
      });
    });

    await waitFor(() => {
      expect(result.current.runningProfileId).toBeNull();
      expect(result.current.exportStatus).toMatchObject({
        text: "Excel export completed — 1 file generated.",
        tone: "success",
        ariaLabel: "Reporting export status",
        className: "border-success/30 bg-success/10 text-success",
        fields: expect.arrayContaining([
          expect.objectContaining({ label: "Job ID", value: "export-1", tone: "default", className: "text-foreground" }),
          expect.objectContaining({ label: "Requested", value: "excel", tone: "default", className: "text-foreground" }),
          expect.objectContaining({ label: "Output", value: "exports", tone: "default", className: "text-foreground" }),
          expect.objectContaining({ label: "Records", value: "20", tone: "default", className: "text-foreground" }),
          expect.objectContaining({ label: "Bytes", value: "1.17 KB", tone: "default", className: "text-foreground" })
        ]),
        warnings: [],
        artifacts: [
          {
            label: "SPY xlsx",
            value: "excel/export-1.xlsx · 20 records · 1.17 KB",
            tone: "default",
            className: "text-foreground"
          }
        ]
      });
    });
  });

  it("ignores stale export results after the operator selects another profile", async () => {
    let releaseExport!: (value: ExportAnalysisResult) => void;
    const calls: string[] = [];
    const signals: AbortSignal[] = [];
    const runExport = (profileId: string, options?: { signal?: AbortSignal }) => {
      calls.push(profileId);
      if (options?.signal) {
        signals.push(options.signal);
      }
      return new Promise<ExportAnalysisResult>((resolve) => {
        releaseExport = resolve;
      });
    };
    const { result } = renderHook(() => useReportingScreenViewModel(reporting, { runExport }));

    act(() => { result.current.selectProfile("excel"); });

    let exportPromise: Promise<void> = Promise.resolve();
    act(() => {
      exportPromise = result.current.runExport("excel", "Excel");
    });

    await waitFor(() => {
      expect(result.current.runningProfileId).toBe("excel");
      expect(result.current.exportStatus?.text).toBe("Starting Excel export…");
    });

    act(() => { result.current.selectProfile("csv"); });

    expect(result.current.selectedProfile?.title).toBe("CSV");
    expect(result.current.runningProfileId).toBeNull();
    expect(result.current.exportStatus).toBeNull();
    expect(signals).toHaveLength(1);
    expect(signals[0].aborted).toBe(true);

    await act(async () => {
      releaseExport({
        jobId: "export-stale",
        success: true,
        status: "completed",
        profileId: "excel",
        symbols: [],
        filesGenerated: 1,
        totalRecords: 20,
        totalBytes: 1200,
        outputDirectory: "exports",
        durationSeconds: 1,
        error: null,
        warnings: [],
        files: [],
        timestamp: "2026-05-01T00:00:00Z"
      });
      await exportPromise;
    });

    expect(calls).toEqual(["excel"]);
    expect(result.current.selectedProfile?.title).toBe("CSV");
    expect(result.current.runningProfileId).toBeNull();
    expect(result.current.exportStatus).toBeNull();
  });

  it("aborts older same-profile export commands and keeps only the newest result", async () => {
    const firstExport = createDeferred<ExportAnalysisResult>();
    const secondExport = createDeferred<ExportAnalysisResult>();
    const signals: AbortSignal[] = [];
    const runExport = (_profileId: string, options?: { signal?: AbortSignal }) => {
      if (options?.signal) {
        signals.push(options.signal);
      }

      return signals.length === 1 ? firstExport.promise : secondExport.promise;
    };
    const { result } = renderHook(() => useReportingScreenViewModel(reporting, { runExport }));

    let firstPromise: Promise<void> = Promise.resolve();
    act(() => {
      firstPromise = result.current.runExport("csv", "CSV");
    });

    await waitFor(() => {
      expect(result.current.runningProfileId).toBe("csv");
      expect(result.current.exportStatus?.text).toBe("Starting CSV export…");
    });

    let secondPromise: Promise<void> = Promise.resolve();
    act(() => {
      secondPromise = result.current.runExport("csv", "CSV");
    });

    expect(signals).toHaveLength(2);
    expect(signals[0].aborted).toBe(true);
    expect(signals[1].aborted).toBe(false);

    await act(async () => {
      firstExport.resolve(buildExportResult("csv", "stale-export"));
      await Promise.resolve();
    });

    expect(result.current.exportStatus?.text).toBe("Starting CSV export…");
    expect(result.current.runningProfileId).toBe("csv");

    await act(async () => {
      secondExport.resolve(buildExportResult("csv", "fresh-export"));
      await secondPromise;
      await firstPromise;
    });

    expect(result.current.runningProfileId).toBeNull();
    expect(result.current.exportStatus).toMatchObject({
      text: "CSV export completed — 1 file generated.",
      fields: expect.arrayContaining([
        expect.objectContaining({ label: "Job ID", value: "fresh-export" })
      ])
    });
  });

  it("warns when the export service resolves a different profile id", async () => {
    const { result } = renderHook(() => useReportingScreenViewModel(reporting, {
      runExport: async () => ({
        jobId: "export-2",
        success: true,
        status: "completed",
        profileId: "python-pandas",
        symbols: null,
        filesGenerated: 0,
        totalRecords: 0,
        totalBytes: 0,
        outputDirectory: null,
        durationSeconds: 0,
        error: null,
        warnings: ["No source files matched the selected range."],
        files: [],
        timestamp: "2026-05-01T00:00:00Z"
      })
    }));

    await act(async () => {
      await result.current.runExport("excel", "Excel");
    });

    expect(result.current.exportStatus?.warnings).toEqual([
      "Requested profile excel resolved as python-pandas.",
      "No source files matched the selected range."
    ]);
    expect(result.current.exportStatus?.fields).toEqual(expect.arrayContaining([
      expect.objectContaining({ label: "Requested", value: "excel", tone: "default", className: "text-foreground" }),
      expect.objectContaining({ label: "Profile", value: "python-pandas", tone: "default", className: "text-foreground" }),
      expect.objectContaining({ label: "Symbols", value: "All configured symbols", tone: "muted", className: "text-muted-foreground" })
    ]));
  });

  it("reports export command failures from the view model", async () => {
    const { result } = renderHook(() => useReportingScreenViewModel(reporting, {
      runExport: async () => {
        throw new Error("Disk full");
      }
    }));

    await act(async () => {
      await result.current.runExport("csv", "CSV");
    });

    expect(result.current.runningProfileId).toBeNull();
    expect(result.current.exportStatus).toMatchObject({
      text: "CSV export failed. Disk full",
      tone: "danger",
      className: "border-danger/35 bg-danger/10 text-danger",
      fields: expect.arrayContaining([
        expect.objectContaining({ label: "Profile", value: "CSV", tone: "default", className: "text-foreground" }),
        expect.objectContaining({ label: "Failure", value: "Disk full", tone: "warning", className: "text-warning" })
      ]),
      warnings: [],
      artifacts: []
    });
  });

  it("surfaces structured API validation detail for export failures", async () => {
    const { result } = renderHook(() => useReportingScreenViewModel(reporting, {
      runExport: async () => {
        throw createApiErrorFromResponseBody(
          "/api/export/analysis",
          400,
          JSON.stringify({
            title: "Validation failed",
            detail: "One or more validation errors occurred.",
            errors: {
              profileId: ["Profile is required."],
              approvalReason: ["Approval reason must cite packet evidence."]
            }
          })
        );
      }
    }));

    await act(async () => {
      await result.current.runExport("csv", "CSV");
    });

    expect(result.current.exportStatus).toMatchObject({
      text: "CSV export failed. One or more validation errors occurred.",
      tone: "danger",
      fields: expect.arrayContaining([
        expect.objectContaining({ label: "Profile", value: "CSV" }),
        expect.objectContaining({ label: "Failure", value: "One or more validation errors occurred." })
      ]),
      warnings: [
        "Meridian service returned 400. Open diagnostics for technical details.",
        "Validation failed",
        "profileId: Profile is required.",
        "approvalReason: Approval reason must cite packet evidence."
      ],
      artifacts: []
    });
  });

  it("deselects profile when same id clicked again", () => {
    const { result } = renderHook(() => useReportingScreenViewModel(reporting));
    act(() => { result.current.selectProfile("excel"); });
    act(() => { result.current.selectProfile("excel"); });
    expect(result.current.selectedProfile).toBeNull();
  });

  it("returns empty state when reporting is null", () => {
    const { result } = renderHook(() => useReportingScreenViewModel(null));
    expect(result.current.hasRows).toBe(false);
    expect(result.current.statusDetail).toContain("unavailable");
    expect(result.current.queueChips.find((chip) => chip.label === "Recommended")?.value).toBe("0");
    expect(result.current.packTargetChips.find((chip) => chip.label === "Recipients")?.value).toBe("0");
    expect(result.current.loadingState).toEqual({
      role: "status",
      ariaBusy: true,
      ariaLive: "polite",
      titleId: "reporting-loading-title",
      detailId: "reporting-loading-detail",
      title: "Loading Reporting",
      detail: "Waiting for governed report-pack and export evidence.",
      badgeLabel: "Loading",
      routeLabel: "Daily Reporting Cockpit"
    });
  });

  it("derives route-aware loading state for report packs", () => {
    const { result } = renderHook(() => useReportingScreenViewModel(null, undefined, "/reporting/report-packs"));

    expect(result.current.loadingState.routeLabel).toBe("Delivery Evidence");
  });

  it("count label reflects profile count", () => {
    const { result } = renderHook(() => useReportingScreenViewModel(reporting));
    expect(result.current.countLabel).toBe("2 profiles");
  });
});
