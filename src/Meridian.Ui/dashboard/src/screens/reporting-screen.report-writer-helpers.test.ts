import { describe, expect, it } from "vitest";
import { buildWriterFilters, isReportWriterFormulaFunction } from "@/screens/reporting-screen.report-writer-helpers";
import type { ReportWriterDraftSettings } from "@/screens/reporting-screen.report-writer";

function settings(overrides: Partial<ReportWriterDraftSettings>): ReportWriterDraftSettings {
  return {
    name: "template",
    displayName: "Template Draft",
    gridKind: "Pivot",
    topN: "10",
    previewDataset: "portfolioPositions",
    accessMode: "CompanyWide",
    principalKind: "Group",
    principalId: "reporting-ops",
    filterField: "",
    filterOperator: "Equals",
    filterValue: "",
    extraFilters: [],
    ...overrides
  };
}

describe("buildWriterFilters", () => {
  it("returns null when no filter row is complete", () => {
    expect(buildWriterFilters(settings({}))).toBeNull();
    expect(buildWriterFilters(settings({ filterField: "sector", filterValue: "  " }))).toBeNull();
  });

  it("keeps the first filter and every complete extra filter in order", () => {
    const filters = buildWriterFilters(settings({
      filterField: "strategy",
      filterValue: "Core",
      extraFilters: [
        { id: "a", field: "marketValue", operator: "GreaterThanOrEqual", value: "100" },
        { id: "b", field: "", operator: "Equals", value: "ignored" },
        { id: "c", field: "sector", operator: "Contains", value: "" },
        { id: "d", field: "counterparty", operator: "IsNotBlank", value: "ignored" }
      ]
    }));

    expect(filters).toEqual([
      { field: "strategy", operator: "Equals", value: "Core", label: "strategy = Core" },
      { field: "marketValue", operator: "GreaterThanOrEqual", value: "100", label: "marketValue >= 100" },
      { field: "counterparty", operator: "IsNotBlank", value: null, label: expect.stringContaining("counterparty") }
    ]);
  });

  it("keeps extra filters when the first row is cleared", () => {
    const filters = buildWriterFilters(settings({
      extraFilters: [{ id: "a", field: "sector", operator: "Equals", value: "Tech" }]
    }));

    expect(filters).toEqual([{ field: "sector", operator: "Equals", value: "Tech", label: "sector = Tech" }]);
  });
});

describe("isReportWriterFormulaFunction", () => {
  it("recognises if alongside the existing engine functions, case-insensitively", () => {
    for (const name of ["if", "IF", "abs", "safeDivide", "basisPoints", "round"]) {
      expect(isReportWriterFormulaFunction(name)).toBe(true);
    }
    expect(isReportWriterFormulaFunction("case")).toBe(false);
  });
});
