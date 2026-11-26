// @vitest-environment node
import { describe, expect, it } from "vitest";
import { buildHoldingsModel, totalExposureAmount } from "@/lib/holdings-model";
import type { PortfolioWorkspaceResponse, TradingPosition } from "@/types";

const position = (overrides: Partial<TradingPosition> = {}): TradingPosition => ({
  symbol: "AAPL", side: "Long", quantity: "38", averagePrice: "142.20", markPrice: "188.40",
  dayPnl: "+$190", unrealizedPnl: "+$1,756", exposure: "$7,159", ...overrides
});
const portfolio = (positions: TradingPosition[]) => ({ positions }) as PortfolioWorkspaceResponse;
const seeded = [
  position(),
  position({ symbol: "VTI", quantity: "120", averagePrice: "205.00", markPrice: "254.50", unrealizedPnl: "+$5,940", exposure: "$30,540" })
];

describe("shared holdings model", () => {
  it("gives the rows and the headline one total", () => {
    const model = buildHoldingsModel(portfolio(seeded));
    expect(model.totalExposure.value).toBe("$37,699");
    expect(totalExposureAmount(model.totalExposure)).toBe("37699");
    const rowSum = model.rows.reduce((sum, row) => sum + Number(row.exposureAmount), 0);
    expect(rowSum).toBe(37699);
    expect(model.rows.map((row) => row.weight.display)).toEqual(["19.0%", "81.0%"]);
    expect(model.rows[0].weight.reason).toBe("Share of total exposure ($37,699).");
  });

  it("shows the last recorded value with a stale marker when no current price exists", () => {
    const [undated, dated, current] = buildHoldingsModel(portfolio([
      seeded[0],
      position({ markFreshness: { symbol: "AAPL", valuationDate: "2026-11-15", observedOn: "2026-05-07", ageDays: 192, policyVersion: "v1", status: "Stale" } }),
      position({ markFreshness: { symbol: "AAPL", valuationDate: "2026-11-15", observedOn: "2026-11-14", ageDays: 1, policyVersion: "v1", status: "Current" } })
    ])).rows;
    expect(undated.marketValue).toMatchObject({ display: "$7,159.20", stale: true, staleLabel: "Stale · no price date" });
    expect(dated.marketValue).toMatchObject({ display: "$7,159.20", stale: true, staleLabel: "Stale · priced 2026-05-07" });
    expect(dated.marketValue.reason).toContain("quantity 38 × recorded price 188.40");
    expect(current.marketValue).toMatchObject({ display: "$7,159.20", stale: false, staleLabel: null });
  });

  it("keeps a supplied market value and weight, and signs a short holding's recorded value", () => {
    const [supplied, short] = buildHoldingsModel(portfolio([
      position({ marketValue: "7,160.00", currency: "USD", weight: "19.1%", weightBasis: "Net asset value" }),
      position({ side: "Short", exposure: "-$7,159" })
    ])).rows;
    expect(supplied.marketValue).toMatchObject({ display: "7,160.00", stale: false });
    expect(supplied.weight).toMatchObject({ display: "19.1%", reason: "Net asset value" });
    expect(short.marketValue.display).toBe("-$7,159.20");
    expect(short.exposureAmount).toBe("7159");
  });

  it("uses a dash with a reason only where a value truly does not exist", () => {
    const model = buildHoldingsModel(portfolio([
      position({ exposure: "7159", markPrice: "n/a" }),
      position({ exposure: "€40" })
    ]));
    const [first, second] = model.rows;
    expect(first.currency).toMatchObject({ display: "—", reason: "The source did not supply a currency for this holding." });
    expect(first.marketValue.display).toBe("—");
    expect(first.marketValue.reason).toBe("No market value or recorded price was supplied for this holding.");
    expect(second.currencyCode).toBe("EUR");
    expect(second.weight.display).toBe("—");
    expect(second.weight.reason).toContain("total exposure");
    expect(model.totalExposure.status).toBe("unavailable");
  });
});
