import { describe, expect, it } from "vitest";
import { buildDataUploadWorkbookReviewState } from "./data-screen.workbook-review";
import type { DataUploadWorkbookPreviewResult } from "@/types/workstation-3";

function buildReadyResult(): DataUploadWorkbookPreviewResult {
  return {
    uploadId: "UP-1",
    fileName: "meridian-onboarding-workbook.xlsx",
    fileSizeBytes: 4096,
    contentType: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
    uploadedBy: "ops-user",
    uploadedAtUtc: "2026-07-15T00:00:00Z",
    retainedPath: "workstation/data-uploads/UP-1/meridian-onboarding-workbook.xlsx",
    sheetCount: 2,
    totalParsedRowCount: 3,
    sheets: [
      {
        sheetName: "Entities",
        templateId: "entity-configuration",
        templateLabel: "Entity configuration",
        dataDomain: "Entity setup",
        parsedRowCount: 2,
        previewRowCount: 2,
        headers: ["entity_id", "entity_name", "entity_type"],
        previewRows: [
          { entity_id: "ENT-1", entity_name: "Northwind Income Fund LP", entity_type: "Fund" },
          { entity_id: "ENT-2", entity_name: "Northwind Sleeve", entity_type: "Sleeve" }
        ],
        issues: [],
        status: "ReadyForReview"
      },
      {
        sheetName: "Securities",
        templateId: "asset-information",
        templateLabel: "Asset information",
        dataDomain: "Security Master",
        parsedRowCount: 1,
        previewRowCount: 1,
        headers: ["asset_id", "symbol", "asset_name"],
        previewRows: [{ asset_id: "AST-1", symbol: "AAPL", asset_name: "Apple Inc" }],
        issues: [],
        status: "ReadyForReview"
      }
    ],
    crossSheetIssues: [],
    status: "ReadyForReview",
    nextAction: "Review each sheet's rows, then route the retained workbook into validation and reconciliation."
  };
}

describe("buildDataUploadWorkbookReviewState", () => {
  it("returns an empty, commit-disabled state when there is no result", () => {
    const state = buildDataUploadWorkbookReviewState(null);

    expect(state.hasResult).toBe(false);
    expect(state.busy).toBe(false);
    expect(state.errorSummary).toBeNull();
    expect(state.sheetTabs).toHaveLength(0);
    expect(state.commitDisabled).toBe(true);
    expect(state.commitDisabledReason).toContain("Upload a workbook");
  });

  it("reflects a busy preview without a result", () => {
    const state = buildDataUploadWorkbookReviewState(null, { busy: true, fileName: "onboarding.xlsx" });

    expect(state.busy).toBe(true);
    expect(state.statusLabel).toBe("Previewing");
    expect(state.summary).toContain("onboarding.xlsx");
    expect(state.commitDisabled).toBe(true);
  });

  it("blocks commit with a data-row prompt when the workbook has no rows", () => {
    const result = buildReadyResult();
    result.status = "NeedsSchemaRepair";
    result.totalParsedRowCount = 0;
    result.sheets = result.sheets.map((sheet) => ({
      ...sheet,
      parsedRowCount: 0,
      previewRowCount: 0,
      previewRows: [],
      status: "Empty"
    }));

    const state = buildDataUploadWorkbookReviewState(result);

    expect(state.commitDisabled).toBe(true);
    expect(state.commitDisabledReason).toContain("data row");
  });

  it("surfaces a preview error when the request failed", () => {
    const state = buildDataUploadWorkbookReviewState(null, {
      errorSummary: "Workbook preview accepts .xlsx files."
    });

    expect(state.hasResult).toBe(false);
    expect(state.statusTone).toBe("danger");
    expect(state.statusLabel).toBe("Preview failed");
    expect(state.errorSummary).toBe("Workbook preview accepts .xlsx files.");
    expect(state.commitDisabled).toBe(true);
  });

  it("enables commit and maps sheet tabs when every sheet is ready", () => {
    const state = buildDataUploadWorkbookReviewState(buildReadyResult());

    expect(state.hasResult).toBe(true);
    expect(state.statusTone).toBe("success");
    expect(state.commitDisabled).toBe(false);
    expect(state.commitDisabledReason).toBeNull();
    expect(state.errorCount).toBe(0);
    expect(state.sheetTabs.map((tab) => tab.sheetName)).toEqual(["Entities", "Securities"]);

    const entities = state.sheetTabs[0];
    expect(entities.statusTone).toBe("success");
    expect(entities.statusLabel).toBe("Ready for review");
    expect(entities.previewRows[0].values.map((cell) => cell.value)).toEqual([
      "ENT-1",
      "Northwind Income Fund LP",
      "Fund"
    ]);
  });

  it("blocks commit and surfaces cell-addressed issues when a sheet needs repair", () => {
    const result = buildReadyResult();
    result.status = "NeedsSchemaRepair";
    result.sheets[0] = {
      ...result.sheets[0],
      status: "NeedsRepair",
      issues: [
        {
          severity: "Error",
          field: "entity_name",
          message: "Required value 'entity_name' is missing.",
          rowNumber: 3,
          sheetName: "Entities",
          cellReference: "Entities!B3"
        }
      ]
    };
    result.crossSheetIssues = [
      {
        severity: "Error",
        field: "parent_entity_id",
        message: "parent_entity_id 'ENT-404' does not resolve to an entity_id in the 'Entities' sheet.",
        rowNumber: 4,
        sheetName: "Entities",
        cellReference: "Entities!D4"
      }
    ];

    const state = buildDataUploadWorkbookReviewState(result);

    expect(state.statusTone).toBe("danger");
    expect(state.commitDisabled).toBe(true);
    expect(state.errorCount).toBe(2);
    expect(state.commitDisabledReason).toContain("2 blocking issues");
    expect(state.sheetTabs[0].issues[0].location).toBe("Entities!B3");
    expect(state.crossSheetIssues[0].location).toBe("Entities!D4");
    expect(state.crossSheetIssues[0].tone).toBe("danger");
  });
});
