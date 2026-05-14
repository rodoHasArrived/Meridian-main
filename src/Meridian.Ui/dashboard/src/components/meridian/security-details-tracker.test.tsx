import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it } from "vitest";
import { LotsTrackerPanel } from "@/components/meridian/security-details-tracker";
import type { SecurityLot } from "@/components/meridian/security-details-tracker.view-model";

const lotsKey = "meridian.security.lots.AAPL";

const lots: SecurityLot[] = [
  {
    lotId: "lot-aapl-1",
    tradeDate: "2026-04-01",
    quantity: 100,
    price: 184.25,
    fees: 1.25,
    note: "Opening sleeve"
  }
];

describe("LotsTrackerPanel", () => {
  beforeEach(() => {
    window.localStorage.clear();
    window.localStorage.setItem(lotsKey, JSON.stringify(lots));
  });

  it("requires confirmation before removing a cost-basis lot", async () => {
    const user = userEvent.setup();
    render(<LotsTrackerPanel securityId="AAPL" currency="USD" />);

    await user.click(screen.getByRole("button", { name: "Remove AAPL lot from 2026-04-01" }));

    expect(screen.getByRole("button", {
      name: "Confirm remove AAPL lot from 2026-04-01. This deletes the local cost-basis lot."
    })).toBeInTheDocument();
    expect(screen.getByRole("row", { name: /Remove confirmation pending/i })).toBeInTheDocument();
    expect(JSON.parse(window.localStorage.getItem(lotsKey) ?? "[]")).toHaveLength(1);

    await user.click(screen.getByRole("button", {
      name: "Confirm remove AAPL lot from 2026-04-01. This deletes the local cost-basis lot."
    }));

    await waitFor(() => expect(screen.getByText("No lots recorded yet. Add a lot above to start tracking cost basis.")).toBeInTheDocument());
    expect(JSON.parse(window.localStorage.getItem(lotsKey) ?? "[]")).toEqual([]);
  });
});
