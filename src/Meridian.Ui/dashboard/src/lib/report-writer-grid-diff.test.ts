import { describe, expect, it } from "vitest";
import { buildReportWriterGridDiff } from "@/lib/report-writer-grid-diff";
import type { ReportWriterGridColumn, ReportWriterGridRender, ReportWriterGridRow } from "@/types";

function column(key: string): ReportWriterGridColumn {
  return { key, label: key, role: "dimension" };
}

function row(rowKey: string, values: Record<string, string>): ReportWriterGridRow {
  return { rowKey, values };
}

function render(
  gridId: string,
  columns: ReportWriterGridColumn[],
  rows: ReportWriterGridRow[]
): ReportWriterGridRender {
  return { gridId, title: gridId, kind: "Pivot", columns, rows, warnings: [] };
}

describe("report-writer grid diff", () => {
  it("classifies added, removed, changed, and unchanged rows with deltas", () => {
    const prior = render("holdings", [column("sector"), column("marketValue")], [
      row("Technology", { sector: "Technology", marketValue: "100" }),
      row("Rates", { sector: "Rates", marketValue: "40" }),
      row("Cash", { sector: "Cash", marketValue: "25" })
    ]);
    const current = render("holdings", [column("sector"), column("marketValue")], [
      row("Technology", { sector: "Technology", marketValue: "150" }),
      row("Rates", { sector: "Rates", marketValue: "40" }),
      row("Credit", { sector: "Credit", marketValue: "60" })
    ]);

    const diff = buildReportWriterGridDiff(prior, current);

    expect(diff.changedRowCount).toBe(1);
    expect(diff.unchangedRowCount).toBe(1);
    expect(diff.addedRowCount).toBe(1);
    expect(diff.removedRowCount).toBe(1);
    expect(diff.warnings).toHaveLength(0);

    const technology = diff.rows.find((item) => item.rowKey === "Technology")!;
    expect(technology.state).toBe("Changed");
    const technologyDelta = technology.cells.find((cell) => cell.column === "marketValue")!;
    expect(technologyDelta.priorValue).toBe("100");
    expect(technologyDelta.currentValue).toBe("150");
    expect(technologyDelta.delta).toBe("50");
    expect(technologyDelta.direction).toBe("Up");

    expect(diff.rows.find((item) => item.rowKey === "Rates")!.state).toBe("Unchanged");
    expect(diff.rows.find((item) => item.rowKey === "Credit")!.state).toBe("Added");

    const cash = diff.rows.find((item) => item.rowKey === "Cash")!;
    expect(cash.state).toBe("Removed");
    expect(cash.cells.find((cell) => cell.column === "marketValue")!.currentValue).toBeNull();
  });

  it("orders current rows before removed rows", () => {
    const prior = render("g", [column("sector"), column("marketValue")], [
      row("Cash", { sector: "Cash", marketValue: "25" }),
      row("Technology", { sector: "Technology", marketValue: "100" })
    ]);
    const current = render("g", [column("sector"), column("marketValue")], [
      row("Technology", { sector: "Technology", marketValue: "100" })
    ]);

    const diff = buildReportWriterGridDiff(prior, current);

    expect(diff.rows.map((item) => item.rowKey)).toEqual(["Technology", "Cash"]);
    expect(diff.rows[diff.rows.length - 1].state).toBe("Removed");
  });

  it("warns when the column sets differ", () => {
    const prior = render("g", [column("sector"), column("marketValue")], [
      row("Technology", { sector: "Technology", marketValue: "100" })
    ]);
    const current = render("g", [column("sector"), column("pnl")], [
      row("Technology", { sector: "Technology", pnl: "10" })
    ]);

    const diff = buildReportWriterGridDiff(prior, current);

    expect(diff.warnings.some((warning) => warning.toLowerCase().includes("different column sets"))).toBe(true);
  });
});
