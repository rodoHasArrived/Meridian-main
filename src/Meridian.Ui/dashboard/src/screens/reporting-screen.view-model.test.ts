import { describe, expect, it } from "vitest";
import { renderHook, act, waitFor } from "@testing-library/react";
import { useReportingScreenViewModel } from "@/screens/reporting-screen.view-model";
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
  reportPackTargets: ["board", "audit"],
  summary: "2 export profiles available."
};

describe("useReportingScreenViewModel", () => {
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

  it("surfaces pack targets", () => {
    const { result } = renderHook(() => useReportingScreenViewModel(reporting));
    expect(result.current.hasPackTargets).toBe(true);
    expect(result.current.packTargets.map((target) => target.label)).toEqual(["board", "audit"]);
    expect(result.current.packTargets[0].ariaLabel).toBe("board report-pack target");
    expect(result.current.packTargetsEmptyState).toEqual({
      text: "No report-pack targets loaded. Configure governed targets in the governance policy before approving this packet.",
      ariaLabel: "No report-pack targets loaded"
    });
  });

  it("builds a route-specific report-pack approval task panel", () => {
    const { result } = renderHook(() => useReportingScreenViewModel(reporting, undefined, "/reporting/report-packs"));

    expect(result.current.workflowTaskPanel).toMatchObject({
      regionLabel: "Report-pack approval task",
      title: "Report-pack approval",
      statusLabel: "Evidence review",
      targetsLabel: "Report-pack approval targets",
      hasTargets: true,
      profileListLabel: "Report-pack export profiles",
      hasProfiles: true,
      actionListLabel: "Selected report-pack export actions",
      hasActions: true
    });
    expect(result.current.workflowTaskPanel?.actions.map((action) => action.label)).toEqual([
      "Preview payload",
      "Run export"
    ]);
    expect(result.current.workflowTaskPanel?.targets.map((target) => target.label)).toEqual(["board", "audit"]);
    expect(result.current.workflowTaskPanel?.profiles.map((profile) => profile.readinessLabel)).toEqual([
      "Dictionary only",
      "Loader only"
    ]);
    expect(result.current.selectedProfile?.title).toBe("Excel");
    expect(result.current.rows.find((row) => row.id === "excel")?.isSelected).toBe(true);
    expect(result.current.workflowTaskPanel?.selectedSummary).toBe(
      "Excel is selected for report-pack approval using Xlsx output to Excel."
    );
    expect(result.current.workflowTaskPanel?.backendLinks.map((link) => link.href)).toEqual([
      "/api/fund-structure/report-packs",
      "/api/export/preview?profile=excel",
      "/api/export/analysis"
    ]);
    expect(result.current.workflowTaskPanel?.backendLinks.map((link) => link.interactionLabel)).toEqual([
      "Open",
      "Open",
      "Reference"
    ]);
    expect(result.current.workflowTaskPanel?.backendLinks.find((link) => link.id === "export-run")).toMatchObject({
      isBrowserNavigable: false,
      ariaLabel: "Reference-only POST /api/export/analysis for Excel export analysis"
    });
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
      statusLabel: "Targets missing",
      hasTargets: false,
      targetsEmptyText: "No report-pack targets loaded. Configure governed targets before approving this packet.",
      targetsEmptyAriaLabel: "No report-pack approval targets",
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
      interactionLabel: "Open",
      ariaLabel: "GET /api/export/preview?profile=csv for CSV export preview"
    });
    expect(result.current.workflowTaskPanel?.actions[0]).toMatchObject({
      id: "preview",
      href: "/api/export/preview?profile=csv",
      ariaLabel: "Preview CSV export payload"
    });
  });

  it("derives report queue summary chips in the view model", () => {
    const { result } = renderHook(() => useReportingScreenViewModel(reporting));

    expect(result.current.recommendedCountLabel).toBe("1");
    expect(result.current.packTargetCountLabel).toBe("2");
    expect(result.current.workbenchChips).toEqual([
      { label: "Profiles", value: "2 profiles" },
      { label: "Pack targets", value: "2" },
      { label: "Recommended", value: "1" },
      { label: "Export route", value: "/api/export/analysis" }
    ]);
    expect(result.current.queueChips).toEqual([
      { label: "Visible", value: "2 of 2" },
      { label: "Recommended", value: "1" },
      { label: "Targets", value: "2" },
      { label: "List", value: "Export profiles" }
    ]);
    expect(result.current.packTargetChips).toEqual([
      { label: "Visible", value: "2" },
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
      label: "Preview payload",
      href: "/api/export/preview?profile=excel",
      ariaLabel: "Preview Excel export payload",
      describedById: "reporting-action-excel-preview-status",
      statusText: "Opens the current export payload preview in a new browser tab.",
      isDisabled: false,
      variant: "outline",
      method: "GET",
      profileId: "excel"
    });
    expect(result.current.selectedProfile?.actions[1]).toMatchObject({
      id: "run",
      label: "Run export",
      variant: "default"
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
      isRunning: false
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
        describedById: "reporting-action-excel-run-status",
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
    const runExport = (profileId: string) => {
      calls.push(profileId);
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
    expect(result.current.packTargetChips.find((chip) => chip.label === "Visible")?.value).toBe("0");
    expect(result.current.loadingState).toEqual({
      role: "status",
      ariaBusy: true,
      ariaLive: "polite",
      titleId: "reporting-loading-title",
      detailId: "reporting-loading-detail",
      title: "Loading Reporting",
      detail: "Waiting for governed report-pack and export evidence.",
      badgeLabel: "Loading",
      routeLabel: "Reporting"
    });
  });

  it("derives route-aware loading state for report packs", () => {
    const { result } = renderHook(() => useReportingScreenViewModel(null, undefined, "/reporting/report-packs"));

    expect(result.current.loadingState.routeLabel).toBe("Report packs");
  });

  it("count label reflects profile count", () => {
    const { result } = renderHook(() => useReportingScreenViewModel(reporting));
    expect(result.current.countLabel).toBe("2 profiles");
  });
});
