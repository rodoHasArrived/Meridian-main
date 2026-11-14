import { describe, expect, it } from "vitest";
import { buildReportWriterPreviewRows, extractReportWriterFormulaFields } from "@/screens/reporting-screen.report-writer-helpers";

describe("report-writer formula references", () => {
  it("reads a hyphen between known names as subtraction", () => {
    const known = ["pnl", "fees", "net-pnl"];

    expect(extractReportWriterFormulaFields("pnl-fees", known)).toEqual(["pnl", "fees"]);
    expect(extractReportWriterFormulaFields("net-pnl-fees", known)).toEqual(["net-pnl", "fees"]);
    expect(extractReportWriterFormulaFields("{net-pnl} + pnl-1", known)).toEqual(["net-pnl", "pnl"]);
  });

  it("keeps an unknown hyphenated identifier whole", () => {
    expect(extractReportWriterFormulaFields("gross-costs", ["pnl"])).toEqual(["gross-costs"]);
    expect(extractReportWriterFormulaFields("pnl-fees")).toEqual(["pnl-fees"]);
  });

  it("does not invent a hyphenated preview column for a subtraction formula", () => {
    const rows = buildReportWriterPreviewRows(
      {
        gridId: "grid-1",
        title: "Grid",
        kind: "Detail",
        rowFields: ["sector"],
        columnFields: [],
        metrics: [
          { name: "pnl", sourceField: "pnl", function: "Sum", label: "P&L" },
          { name: "fees", sourceField: "fees", function: "Sum", label: "Fees" }
        ],
        formulas: [{ name: "afterFees", expression: "pnl-fees", label: "After fees" }]
      },
      "portfolioPositions"
    );

    expect(Object.keys(rows[0]).sort()).toEqual(["fees", "pnl", "previewDataset", "sector"]);
  });
});
