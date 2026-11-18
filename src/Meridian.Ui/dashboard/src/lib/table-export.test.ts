import { describe, expect, it } from "vitest";
import { buildShownExport, shownExportCsvRows, shownExportXlsxSheets, type ShownExportColumn } from "./table-export";

interface Row { account: string; debit: number; exact: string }
const columns: ShownExportColumn<Row>[] = [
  { id: "account", label: "Account", exportValue: (row) => row.account },
  { id: "debit", label: "Debit", exportValue: (row) => row.debit, exportDisplay: (row) => `$${row.debit.toLocaleString("en-US", { minimumFractionDigits: 2 })}`, exportNumberFormat: "#,##0.00;(#,##0.00)" },
  { id: "exact", label: "Exact", exportValue: (row) => row.exact, exportType: "decimal" },
  { id: "action", label: "Action" }
];
const rows: Row[] = [{ account: "Cash", debit: 120500, exact: "1.25" }, { account: "Fees", debit: -30, exact: "12345678901234567.89" }];

describe("buildShownExport", () => {
  it("keeps the given column order, drops columns with nothing to export, and uses the shown text", () => {
    const model = buildShownExport(columns, rows);
    expect(model.headers).toEqual(["Account", "Debit", "Exact"]);
    expect(shownExportCsvRows(model)).toEqual([["Cash", "$120,500.00", "1.25"], ["Fees", "$-30.00", "12345678901234567.89"]]);
  });

  it("keeps numbers numeric, but leaves a decimal too long for a double as text", () => {
    const model = buildShownExport(columns, rows);
    expect(model.rows[0]![1]).toEqual({ text: "$120,500.00", number: 120500, format: "#,##0.00;(#,##0.00)" });
    expect(model.rows[0]![2]).toEqual({ text: "1.25", number: 1.25, format: "#,##0.00" });
    expect(model.rows[1]![2]).toMatchObject({ number: null, format: null });
  });

  it("adds the totals row last, labelled in its first cell", () => {
    const model = buildShownExport(columns, rows, { cells: { debit: { value: 120470, display: "$120,470.00" } }, label: "Ledger totals" });
    expect(shownExportCsvRows(model).at(-1)).toEqual(["Ledger totals", "$120,470.00", ""]);
    expect(model.footer![1]).toMatchObject({ number: 120470 });
    expect(buildShownExport(columns, rows, { cells: {}, label: "Totals" }).footer).toBeNull();
  });
});

describe("shownExportXlsxSheets", () => {
  it("writes a bold header, typed cells, a bold totals row and an About sheet", () => {
    const model = buildShownExport(columns, rows, { cells: { debit: 120470 }, label: "Totals" });
    const [table, about] = shownExportXlsxSheets(model, { Source: "Posted journal", "Exported at UTC": "2026-11-17T00:00:00Z" }, "Ledger Explorer results");
    expect(table!.sheet).toBe("Ledger Explorer results");
    expect(table!.stickyRowsCount).toBe(1);
    expect(table!.data[0]![0]).toEqual({ value: "Account", type: String, fontWeight: "bold" });
    expect(table!.data[1]![1]).toEqual({ value: 120500, type: Number, format: "#,##0.00;(#,##0.00)" });
    expect(table!.data[2]![2]).toEqual({ value: "12345678901234567.89", type: String });
    expect(table!.data.at(-1)![1]).toMatchObject({ value: 120470, type: Number, fontWeight: "bold" });
    expect(about!.sheet).toBe("About");
    expect(about!.data).toEqual([
      [{ value: "Source", type: String, fontWeight: "bold" }, { value: "Posted journal", type: String }],
      [{ value: "Exported at UTC", type: String, fontWeight: "bold" }, { value: "2026-11-17T00:00:00Z", type: String }]
    ]);
  });

  it("limits the sheet name to the spreadsheet maximum of 31 characters", () => {
    const [table] = shownExportXlsxSheets(buildShownExport(columns, rows), {}, "A very long table name that is over the limit");
    expect(table!.sheet).toHaveLength(31);
  });
});
