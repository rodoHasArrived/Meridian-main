/** Screen-local table controls. Monetary strings remain presentation values, never sort keys. */
export interface PortfolioPositionsTableState {
  search: string;
  side: string | null;
  readiness: "all" | "current" | "review-required";
  sort: {
    columnId: "symbol" | "side" | "mark-readiness";
    direction: "asc" | "desc";
  } | null;
}

export const PORTFOLIO_POSITION_SEARCH_MAX_LENGTH = 120;
export const DEFAULT_PORTFOLIO_POSITIONS_TABLE_STATE: PortfolioPositionsTableState = {
  search: "",
  side: null,
  readiness: "all",
  sort: null
};

interface PortfolioPositionTableRow {
  id: string;
  symbol: string;
  side: string;
  markFreshness: { reviewRequired: boolean };
}

/** Decode optional fields independently so old or partially malformed views remain usable. */
export function normalizePortfolioPositionsTableState(input: unknown): PortfolioPositionsTableState {
  const source = isRecord(input) ? input : {};
  const sort = isRecord(source.sort) ? source.sort : {};
  const validColumn = sort.columnId === "symbol" || sort.columnId === "side" || sort.columnId === "mark-readiness";
  const validDirection = sort.direction === "asc" || sort.direction === "desc";
  return {
    search: typeof source.search === "string" ? source.search.slice(0, PORTFOLIO_POSITION_SEARCH_MAX_LENGTH) : "",
    side: typeof source.side === "string" && source.side.trim().length > 0 && source.side.length <= 120 ? source.side : null,
    readiness: source.readiness === "current" || source.readiness === "review-required" ? source.readiness : "all",
    sort: validColumn && validDirection ? {
      columnId: sort.columnId as NonNullable<PortfolioPositionsTableState["sort"]>["columnId"],
      direction: sort.direction as "asc" | "desc"
    } : null
  };
}

export function selectPortfolioPositionRows<T extends PortfolioPositionTableRow>(rows: readonly T[], state: PortfolioPositionsTableState): T[] {
  const query = state.search.trim().toLocaleLowerCase("en");
  const matches = rows.map((row, sourceIndex) => ({ row, sourceIndex })).filter(({ row }) =>
    (!query || row.symbol.toLocaleLowerCase("en").includes(query)) &&
    (state.side === null || row.side === state.side) &&
    (state.readiness === "all" || (state.readiness === "review-required") === row.markFreshness.reviewRequired)
  );
  const sort = state.sort;
  if (sort) {
    const direction = sort.direction === "asc" ? 1 : -1;
    matches.sort((left, right) => {
      const comparison = sort.columnId === "mark-readiness"
        ? Number(left.row.markFreshness.reviewRequired) - Number(right.row.markFreshness.reviewRequired)
        : left.row[sort.columnId].localeCompare(right.row[sort.columnId], "en", { sensitivity: "base" });
      return comparison * direction || left.sourceIndex - right.sourceIndex;
    });
  }
  return matches.map(({ row }) => row);
}

/** Options describe all loaded rows, not only the currently filtered result set. */
export function getPositionSideOptions(rows: readonly Pick<PortfolioPositionTableRow, "side">[]): string[] {
  return [...new Set(rows.map((row) => row.side))].sort((a, b) => a.localeCompare(b, "en"));
}

export function hasPortfolioPositionControls(state: PortfolioPositionsTableState): boolean {
  return state.search !== "" || state.side !== null || state.readiness !== "all" || state.sort !== null;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === "object" && !Array.isArray(value);
}
