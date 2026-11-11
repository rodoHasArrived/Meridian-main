import { describe, expect, it } from "vitest";
import { sortBrokeragePositions } from "./portfolio-screen.brokerage-table";
import type { PortfolioBrokeragePositionRow } from "./portfolio-screen.view-model";
import { presentMarkFreshness } from "@/lib/mark-freshness";

function row(id: string, currency: string | null, value: number | null): PortfolioBrokeragePositionRow {
  return {
    id, currency, numericValues: { "unrealized-pnl": value, "market-value": value, quantity: value },
    symbol: id, accountLabel: "Account", accountKind: "Brokerage", quantity: "999", averagePrice: "not a sort key",
    markPrice: "not a sort key", marketValue: "$999", unrealizedPnl: "+$999", pnlTone: "default",
    assetClass: "Equity", securityCoverage: "Covered", rowClassName: "", isSelected: false,
    detailPanelId: "detail", expanded: false, selectAriaLabel: id, ariaLabel: id,
    markFreshness: presentMarkFreshness(undefined)
  };
}

describe("Brokerage holdings numeric sorting", () => {
  const rows = [row("usd-loss", "USD", -200), row("eur-gain", "EUR", 500), row("usd-gain", "USD", 9), row("eur-loss", "EUR", -2.5), row("unknown", null, -99999), row("missing", "USD", null)];
  it("ranks raw amounts within stable currency groups without comparing monetary display strings", () => {
    expect(sortBrokeragePositions(rows, { columnId: "unrealized-pnl", direction: "asc" }).map(r => r.id))
      .toEqual(["eur-loss", "eur-gain", "usd-loss", "usd-gain", "missing", "unknown"]);
    expect(sortBrokeragePositions(rows, { columnId: "unrealized-pnl", direction: "desc" }).map(r => r.id))
      .toEqual(["eur-gain", "eur-loss", "usd-gain", "usd-loss", "missing", "unknown"]);
  });
  it("sorts quantity across currency groups and keeps missing observations last", () => {
    expect(sortBrokeragePositions(rows, { columnId: "quantity", direction: "desc" }).map(r => r.id))
      .toEqual(["eur-gain", "usd-gain", "eur-loss", "usd-loss", "unknown", "missing"]);
  });
  it("preserves original records, source-order ties, selection and source array", () => {
    const tied = [row("first", "EUR", 4), row("second", "EUR", 4)];
    tied[1].isSelected = true;
    const sorted = sortBrokeragePositions(tied, { columnId: "market-value", direction: "desc" });
    expect(sorted.map(r => r.id)).toEqual(["first", "second"]);
    expect(sorted[1]).toBe(tied[1]);
    expect(sorted[1].isSelected).toBe(true);
    expect(sortBrokeragePositions(tied, null)).toEqual(tied);
    expect(tied.map(r => r.id)).toEqual(["first", "second"]);
  });
});
