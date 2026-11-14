import { describe, expect, it } from "vitest";
import { safeTableCsv } from "@/lib/table-workbench";

describe("safeTableCsv", () => {
  it("neutralizes spreadsheet formulas in text without changing signed numeric amounts", () => {
    expect(safeTableCsv(["Name", "Amount"], [["=1+2", -12.5], ["  @SUM(A1)", 0], ['quoted,"value"', 3]]))
      .toBe('Name,Amount\n\'=1+2,-12.5\n\'  @SUM(A1),0\n"quoted,""value""",3');
  });

  it("preserves declared exact decimal strings without giving arbitrary text numeric semantics", () => {
    expect(safeTableCsv(["Exact amount", "Text"], [[{ kind: "decimal", value: "-12345678901234567890.123456789" }, "-123.45"]]))
      .toBe("Exact amount,Text\n-12345678901234567890.123456789,'-123.45");
    expect(() => safeTableCsv(["Amount"], [[{ kind: "decimal", value: "-SUM(A1)" }]])).toThrow("invalid source value");
  });
});
