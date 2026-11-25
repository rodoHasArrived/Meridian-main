import { describe, expect, it } from "vitest";
import { extractReportWriterFormulaFields } from "@/screens/reporting-screen.report-writer-helpers";

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

});
