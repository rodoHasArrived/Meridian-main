import { describe, expect, it } from "vitest";
import { buildLotsTrackerViewModel, type SecurityLot } from "./security-details-tracker.view-model";

const lots: SecurityLot[] = [
  {
    lotId: "lot-aapl-1",
    tradeDate: "2026-04-01",
    quantity: 100,
    price: 184.25,
    fees: 1.25,
    note: "Opening sleeve"
  },
  {
    lotId: "lot-aapl-2",
    tradeDate: "2026-04-15",
    quantity: 50,
    price: 188.5,
    fees: 0,
    note: ""
  }
];

function buildVm(overrides: Partial<Parameters<typeof buildLotsTrackerViewModel>[0]> = {}) {
  return buildLotsTrackerViewModel({
    securityId: "AAPL",
    currency: "USD",
    lots,
    marketPriceOverride: 192,
    draft: {
      tradeDate: "2026-05-12",
      quantity: "25",
      price: "190.25",
      fees: "1.50",
      note: "Add-on"
    },
    selectedLotId: "lot-aapl-2",
    ...overrides
  });
}

describe("buildLotsTrackerViewModel", () => {
  it("projects lots into selectable rows, totals, and selected detail state", () => {
    const vm = buildVm();

    expect(vm.addCommand.disabled).toBe(false);
    expect(vm.metrics.map((metric) => [metric.id, metric.value])).toEqual([
      ["quantity", "150"],
      ["average-cost", "185.68 USD"],
      ["total-cost", "27,851.25 USD"],
      ["unrealised-pnl", "948.75 USD"]
    ]);
    expect(vm.rows[1]).toMatchObject({
      lotId: "lot-aapl-2",
      selected: true,
      expanded: true,
      detailPanelId: "security-lots-detail-aapl",
      noteLabel: "-"
    });
    expect(vm.selectedDetail).toMatchObject({
      panelId: "security-lots-detail-aapl",
      title: "AAPL · 2026-04-15",
      statusLabel: "Long",
      statusBadgeVariant: "success"
    });
    expect(vm.selectedDetail?.fields.map((field) => field.label)).toEqual([
      "Lot ID",
      "Quantity",
      "Price",
      "Fees",
      "Cost basis",
      "Trade date"
    ]);
  });

  it("keeps add-lot validation and disabled copy in the view model", () => {
    const vm = buildVm({
      draft: {
        tradeDate: "2026-05-12",
        quantity: "0",
        price: "190.25",
        fees: "",
        note: ""
      }
    });

    expect(vm.addCommand).toEqual({
      label: "Add lot",
      ariaLabel: "Add lot unavailable: Quantity must be a non-zero number.",
      disabled: true,
      disabledReason: "Quantity must be a non-zero number."
    });
  });

  it("falls back to the first row and exposes an accessible empty state", () => {
    const resolved = buildVm({ selectedLotId: "missing" });
    expect(resolved.selectedLotId).toBe("lot-aapl-1");
    expect(resolved.rows[0]?.expanded).toBe(true);

    const empty = buildVm({ lots: [], selectedLotId: null, marketPriceOverride: null });
    expect(empty.rows).toEqual([]);
    expect(empty.selectedDetail).toBeNull();
    expect(empty.emptyText).toBe("No lots recorded yet. Add a lot above to start tracking cost basis.");
  });
});
