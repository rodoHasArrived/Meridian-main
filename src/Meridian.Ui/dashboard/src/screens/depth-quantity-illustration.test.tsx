import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import { DepthQuantityIllustration } from "@/screens/depth-quantity-illustration";
import { buildDepthQuantityIllustration } from "@/screens/depth-quantity.view-model";
import type { OrderBookResponse } from "@/types/market-data";

const level = (side: string, price: number, size: number, index: number) => ({ side, price, size, level: index, marketMaker: null });
const book: OrderBookResponse = { symbol: "ABC", timestamp: "2026-06-01T12:00:00Z", bids: [level("Bid", 98, 50, 2), level("Bid", 99, 10, 1)], asks: [level("Ask", 102, 40, 2), level("Ask", 101, 20, 1)], midPrice: 100, imbalance: null, marketState: "Open", sequenceNumber: 55, isStale: false, streamId: "test", venue: "TEST" };

describe("displayed depth quantity illustration", () => {
  it("walks asks from best price and distinguishes partial final coverage", () => {
    const vm = buildDepthQuantityIllustration(book, "Buy", "25");
    expect(vm.levels.map((row) => [row.price, row.used])).toEqual([[101, 20], [102, 5]]);
    expect(vm.covered).toBe(25);
    expect(vm.shortfall).toBe(0);
    expect(vm.terminalPrice).toBe(102);
  });
  it("walks bids highest first and retains a displayed-liquidity shortfall", () => {
    const vm = buildDepthQuantityIllustration(book, "Sell", "70");
    expect(vm.levels.map((row) => [row.price, row.used])).toEqual([[99, 10], [98, 50]]);
    expect(vm.shortfall).toBe(10);
  });
  it("handles exact boundary, fractional sizes, and one-sided books", () => {
    expect(buildDepthQuantityIllustration(book, "Buy", "20").terminalPrice).toBe(101);
    expect(buildDepthQuantityIllustration({ ...book, asks: [level("Ask", 101, .5, 1)] }, "Buy", "0.25").covered).toBe(.25);
    expect(buildDepthQuantityIllustration({ ...book, asks: [] }, "Buy", "20").shortfall).toBe(20);
  });
  it.each(["0", "-1", "NaN", "Infinity", "1e3", "9007199254740992"])("rejects unsupported quantity %s", (input) => {
    expect(buildDepthQuantityIllustration(book, "Buy", input).active).toBe(false);
    expect(buildDepthQuantityIllustration(book, "Buy", input).error).toBeTruthy();
  });
  it("refuses malformed depth instead of silently skipping it", () => {
    expect(buildDepthQuantityIllustration({ ...book, asks: [level("Ask", 101, -1, 1)] }, "Buy", "10").error).toMatch(/unsupported values/);
  });
  it("labels the read-only illustration and shows partial and beyond-displayed states", async () => {
    const user = userEvent.setup();
    render(<DepthQuantityIllustration book={{ ...book, isStale: true }} />);
    expect(screen.getByText(/not predicted execution/)).toBeInTheDocument();
    const input = screen.getByLabelText(/Illustrative quantity/);
    await user.type(input, "25");
    expect(screen.getByText("Partial level")).toBeInTheDocument();
    expect(screen.getByRole("status")).toHaveTextContent("25 of 25");
    await user.clear(input);
    await user.type(input, "70");
    expect(screen.getByRole("status")).toHaveTextContent("10 beyond displayed levels");
    expect(screen.getByText(/STALE/)).toBeInTheDocument();
    await user.selectOptions(screen.getByLabelText("Illustrative side"), "Sell");
    expect(screen.getByRole("status")).toHaveTextContent("bid levels");
  });
});
