import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import { DepthQuantityIllustration, illustrateDepth } from "./depth-quantity-illustration";
import type { OrderBookResponse } from "@/types/market-data";

const book: OrderBookResponse = { symbol: "ABC", timestamp: "2026-11-12T12:00:00Z", sequenceNumber: 1, marketState: "Open", isStale: false, venue: "TEST", streamId: "s", midPrice: 10, imbalance: null,
  bids: [{ side: "Bid", level: 1, price: 9, size: 30, marketMaker: null }],
  asks: [{ side: "Ask", level: 2, price: 12, size: 70, marketMaker: null }, { side: "Ask", level: 1, price: 11, size: 60, marketMaker: null }] };

describe("displayed depth illustration", () => {
  it("orders the consumed side, uses only part of the last level and never invents missing depth", () => {
    const result = illustrateDepth(book.asks, 100, "Buy")!;
    expect(result.rows.map((r) => r.included)).toEqual([60, 40]);
    expect(result.uncovered).toBe(0);
    expect(illustrateDepth(book.asks, 140, "Buy")!.uncovered).toBe(10);
    expect(illustrateDepth(book.asks, 60, "Buy")!.rows[1].included).toBe(0);
    expect(illustrateDepth(book.bids, 40, "Sell")!.uncovered).toBe(10);
    expect(illustrateDepth([], 12, "Buy")!.uncovered).toBe(12);
    expect(illustrateDepth(book.asks, 0, "Buy")).toBeNull();
    expect(illustrateDepth([{ ...book.asks[0], size: NaN }], 10, "Buy")).toBeNull();
    const fractional = [0.7, 0.1].map((size, index) => ({ ...book.asks[0], level: index + 1, size }));
    expect(illustrateDepth(fractional, 0.8, "Buy")!.uncovered).toBe(0);
    expect(illustrateDepth(fractional, 0.81, "Buy")!.uncovered).toBe(0.01);
  });
  it("retains a frozen snapshot until capture and separates buy and sell sides", async () => {
    const user = userEvent.setup();
    const { rerender } = render(<DepthQuantityIllustration book={book} />);
    await user.type(screen.getByRole("textbox", { name: /Illustrative quantity/ }), "100");
    expect(screen.getByRole("status")).toHaveTextContent(/100 of 100.*covered/);
    rerender(<DepthQuantityIllustration book={{ ...book, sequenceNumber: 2, asks: [] }} />);
    expect(screen.getByRole("status")).toHaveTextContent(/100 of 100.*covered/);
    await user.click(screen.getByRole("button", { name: "Capture latest depth snapshot" }));
    expect(screen.getByRole("status")).toHaveTextContent("100 beyond displayed levels");
    await user.selectOptions(screen.getByLabelText("Illustrative side"), "Sell");
    expect(screen.getByRole("status")).toHaveTextContent(/30 of 100.*covered/);
    await user.clear(screen.getByRole("textbox", { name: /Illustrative quantity/ }));
    expect(screen.queryByRole("status")).not.toBeInTheDocument();
  });
  it("keeps a positive sub-nanounit quantity and shortfall visibly nonzero", async () => {
    const user = userEvent.setup();
    render(<DepthQuantityIllustration book={{ ...book, asks: [] }} />);
    await user.clear(screen.getByRole("textbox", { name: /Illustrative quantity/ }));
    await user.type(screen.getByRole("textbox", { name: /Illustrative quantity/ }), "0.000000001");
    expect(screen.getByRole("status")).toHaveTextContent(/0 of 1E-9.*covered/);
    expect(screen.getByRole("status")).toHaveTextContent("1E-9 beyond displayed levels");
  });
});
