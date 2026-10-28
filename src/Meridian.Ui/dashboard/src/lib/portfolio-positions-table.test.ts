import { describe, expect, it } from "vitest";
import {
  DEFAULT_PORTFOLIO_POSITIONS_TABLE_STATE,
  getPositionSideOptions,
  hasPortfolioPositionControls,
  normalizePortfolioPositionsTableState,
  selectPortfolioPositionRows,
  type PortfolioPositionsTableState
} from "@/lib/portfolio-positions-table";

const rows = [
  { id: "z1", symbol: "MSFT", side: "Long", markFreshness: { reviewRequired: true }, exposure: "$100,000" },
  { id: "a1", symbol: "AAPL", side: "Short", markFreshness: { reviewRequired: false }, exposure: "$9" },
  { id: "z2", symbol: "msft", side: "Long", markFreshness: { reviewRequired: false }, exposure: "$20" },
  { id: "a2", symbol: "AAPL", side: "Long", markFreshness: { reviewRequired: true }, exposure: "$5" }
];
const state = (overrides: Partial<PortfolioPositionsTableState> = {}): PortfolioPositionsTableState => ({
  ...DEFAULT_PORTFOLIO_POSITIONS_TABLE_STATE,
  ...overrides
});
const ids = (controls: PortfolioPositionsTableState) => selectPortfolioPositionRows(rows, controls).map((row) => row.id);

describe("portfolio position table controls", () => {
  it("keeps source order, source objects and IDs without mutating input", () => {
    const original = structuredClone(rows);
    const result = selectPortfolioPositionRows(rows, state());
    expect(result).toEqual(rows);
    expect(result).not.toBe(rows);
    expect(result[0]).toBe(rows[0]);
    selectPortfolioPositionRows(rows, state({ sort: { columnId: "symbol", direction: "asc" } }));
    expect(rows).toEqual(original);
  });

  it("searches symbol substrings case insensitively and trims only for matching", () => {
    expect(ids(state({ search: " mSf " }))).toEqual(["z1", "z2"]);
    expect(ids(state({ search: "ap" }))).toEqual(["a1", "a2"]);
    expect(ids(state({ search: "Long" }))).toEqual([]);
  });

  it("combines search, exact side and readiness with AND semantics", () => {
    expect(ids(state({ search: "ms", side: "Long", readiness: "current" }))).toEqual(["z2"]);
    expect(ids(state({ search: "aa", side: "Long", readiness: "review-required" }))).toEqual(["a2"]);
    expect(ids(state({ search: "aa", side: "Short", readiness: "review-required" }))).toEqual([]);
    expect(ids(state({ side: "long" }))).toEqual([]);
  });

  it.each([
    ["symbol", "asc", ["a1", "a2", "z1", "z2"]],
    ["symbol", "desc", ["z1", "z2", "a1", "a2"]],
    ["side", "asc", ["z1", "z2", "a2", "a1"]],
    ["side", "desc", ["a1", "z1", "z2", "a2"]],
    ["mark-readiness", "asc", ["a1", "z2", "z1", "a2"]],
    ["mark-readiness", "desc", ["z1", "a2", "a1", "z2"]]
  ] as const)("sorts %s %s with source-order ties", (columnId, direction, expected) => {
    expect(ids(state({ sort: { columnId, direction } }))).toEqual(expected);
  });

  it("sorts the matching subset without regenerating original row IDs", () => {
    const result = selectPortfolioPositionRows(rows, state({ side: "Long", sort: { columnId: "symbol", direction: "asc" } }));
    expect(result.map((row) => row.id)).toEqual(["a2", "z1", "z2"]);
    expect(result[0]).toBe(rows[3]);
  });

  it("derives stable distinct side options from all loaded rows", () => {
    expect(getPositionSideOptions(rows)).toEqual(["Long", "Short"]);
    expect(getPositionSideOptions([])).toEqual([]);
  });

  it.each([undefined, null, [], "bad", 1, true])("defaults missing or non-object state %j", (input) => {
    expect(normalizePortfolioPositionsTableState(input)).toEqual(DEFAULT_PORTFOLIO_POSITIONS_TABLE_STATE);
  });

  it("normalizes fields independently and ignores unsupported financial sort columns", () => {
    expect(normalizePortfolioPositionsTableState({ search: "ms", side: 9, readiness: "current", sort: { columnId: "exposure", direction: "asc" } }))
      .toEqual(state({ search: "ms", readiness: "current" }));
    expect(normalizePortfolioPositionsTableState({ search: false, side: "Long", readiness: "unknown", sort: { columnId: "symbol", direction: "down" } }))
      .toEqual(state({ side: "Long" }));
    expect(normalizePortfolioPositionsTableState({ search: "MSFT", sort: ["symbol", "asc"] })).toEqual(state({ search: "MSFT" }));
  });

  it("limits search and side lengths while preserving search display whitespace", () => {
    expect(normalizePortfolioPositionsTableState({ search: "x".repeat(200), side: "x".repeat(200) }))
      .toEqual(state({ search: "x".repeat(120) }));
    expect(normalizePortfolioPositionsTableState({ search: " MSFT ", side: " " })).toEqual(state({ search: " MSFT " }));
  });

  it("roundtrips valid controls and recognizes all resettable controls", () => {
    const controls = state({ search: " ms ", side: "Long", readiness: "review-required", sort: { columnId: "mark-readiness", direction: "desc" } });
    expect(normalizePortfolioPositionsTableState(controls)).toEqual(controls);
    expect(hasPortfolioPositionControls(state())).toBe(false);
    expect(hasPortfolioPositionControls(state({ search: " " }))).toBe(true);
    expect(hasPortfolioPositionControls(state({ side: "Long" }))).toBe(true);
    expect(hasPortfolioPositionControls(state({ readiness: "current" }))).toBe(true);
    expect(hasPortfolioPositionControls(state({ sort: { columnId: "side", direction: "asc" } }))).toBe(true);
  });

  it("handles empty data and unmatched filters without altering controls", () => {
    const controls = state({ search: "missing" });
    expect(selectPortfolioPositionRows([], controls)).toEqual([]);
    expect(selectPortfolioPositionRows(rows, controls)).toEqual([]);
    expect(controls.search).toBe("missing");
  });
});
