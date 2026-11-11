import type { DenseDataTableSortState } from "@/components/meridian/ui-kit-primitives";
import type { PortfolioBrokeragePositionRow } from "@/screens/portfolio-screen.view-model";

const numericColumns = new Set(["quantity", "average", "mark", "market-value", "unrealized-pnl"]);

/** Native monetary values are ranked within currency groups, never as converted amounts. */
export function sortBrokeragePositions(
  rows: readonly PortfolioBrokeragePositionRow[],
  sort: DenseDataTableSortState | null
): PortfolioBrokeragePositionRow[] {
  if (!sort) return [...rows];
  const direction = sort.direction === "asc" ? 1 : -1;
  return rows.map((row, index) => ({ row, index })).sort((a, b) => {
    let compared = 0;
    if (numericColumns.has(sort.columnId)) {
      if (sort.columnId !== "quantity") {
        // Keep known currencies in stable alphabetical groups in either direction.
        if (!a.row.currency || !b.row.currency) {
          if (a.row.currency !== b.row.currency) return a.row.currency ? -1 : 1;
          return a.index - b.index; // unknown denomination cannot establish a monetary ranking
        }
        compared = a.row.currency.localeCompare(b.row.currency, "en");
        if (compared) return compared;
      }
      const left = a.row.numericValues[sort.columnId];
      const right = b.row.numericValues[sort.columnId];
      const leftMissing = left == null || !Number.isFinite(left);
      const rightMissing = right == null || !Number.isFinite(right);
      if (leftMissing !== rightMissing) return leftMissing ? 1 : -1;
      if (!leftMissing && !rightMissing) compared = left - right;
    } else if (sort.columnId === "symbol" || sort.columnId === "currency") {
      compared = (a.row[sort.columnId] ?? "").localeCompare(b.row[sort.columnId] ?? "", "en", { sensitivity: "base" });
    }
    return compared * direction || a.index - b.index;
  }).map(({ row }) => row);
}
