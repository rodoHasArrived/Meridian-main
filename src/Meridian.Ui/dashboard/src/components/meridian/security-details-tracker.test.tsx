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

  it("renders add-lot draft fields with helper and validation semantics from the view model", () => {
    render(<LotsTrackerPanel securityId="AAPL" currency="USD" />);

    const quantity = screen.getByLabelText("Quantity");
    const price = screen.getByLabelText("Price");
    const note = screen.getByLabelText("Note");

    expect(screen.getByRole("group", { name: "Add purchase lot for AAPL" })).toBeInTheDocument();
    expect(quantity).toHaveAttribute(
      "aria-describedby",
      "security-lots-aapl-draft-quantity-help security-lots-aapl-draft-quantity-error"
    );
    expect(quantity).toHaveAttribute("aria-errormessage", "security-lots-aapl-draft-quantity-error");
    expect(screen.getByText("Use positive quantity for long lots and negative quantity for short lots.")).toBeInTheDocument();
    expect(screen.getByText("Quantity is required.")).toBeInTheDocument();
    expect(price).toHaveAttribute(
      "aria-describedby",
      "security-lots-aapl-draft-price-help security-lots-aapl-draft-price-error"
    );
    expect(screen.getByText("Price is required.")).toBeInTheDocument();
    expect(note).toHaveAttribute("aria-describedby", "security-lots-aapl-draft-note-help");
    expect(screen.getByText("Add lot unavailable: Quantity is required.")).toHaveAttribute(
      "id",
      "security-lots-aapl-draft-status"
    );
    expect(screen.getByRole("button", { name: "Add lot unavailable: Quantity is required." })).toHaveAttribute(
      "aria-describedby",
      "security-lots-aapl-draft-status"
    );
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
