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
  });

  it("shows no profile selected state initially", () => {
    const { result } = renderHook(() => useReportingScreenViewModel(reporting));
    expect(result.current.selectedProfile).toBeNull();
    expect(result.current.statusTitle).toBe("No profile selected");
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
      expect(result.current.selectedProfile?.actions[1]).toMatchObject({
        label: "Running export…",
        isDisabled: true,
        isRunning: true,
        disabledReason: "Excel export is already running."
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
        timestamp: "2026-05-01T00:00:00Z"
      });
    });

    await waitFor(() => {
      expect(result.current.runningProfileId).toBeNull();
      expect(result.current.exportStatus).toMatchObject({
        text: "Excel export completed — 1 file generated.",
        tone: "success",
        ariaLabel: "Reporting export status"
      });
    });
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
      tone: "danger"
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
  });

  it("count label reflects profile count", () => {
    const { result } = renderHook(() => useReportingScreenViewModel(reporting));
    expect(result.current.countLabel).toBe("2 profiles");
  });
});
