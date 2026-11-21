import { render, screen, within } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

import { GovernedApprovalsPanel } from "@/components/ui/governed-approvals-panel";
import type { GovernedApprovalsViewModel } from "@/screens/trading-screen.governed-approvals";
import type { RiskEscalation, RiskEscalationOptionContract } from "@/types";

const FUND_ACCOUNT_ID = "11111111-2222-3333-4444-555555555555";

const DETAIL_LABELS = [
  "Routed notional",
  "Stop price",
  "Trail amount",
  "Trail percent",
  "Time in force",
  "Position intent",
  "Option contract",
  "Leg 1",
  "Strategy",
  "Fund account ID"
];

// The shape /api/risk/escalations returns for a plain quantity-sized order: every economic
// detail key is present and null, as the server writes nulls rather than omitting them.
function escalation(overrides: Partial<RiskEscalation> = {}): RiskEscalation {
  return {
    escalationId: "esc-1",
    symbol: "AAPL",
    side: "Buy",
    type: "Limit",
    quantity: 200,
    limitPrice: 100,
    reason: "Order notional is inside the governed-approval band.",
    ruleName: "OrderNotional",
    status: "PendingApproval",
    parkedAt: "2026-07-28T12:00:00Z",
    resolvedBy: null,
    resolutionReason: null,
    resolvedAt: null,
    stopPrice: null,
    trailPrice: null,
    trailPercent: null,
    timeInForce: null,
    routedNotional: null,
    fundAccountId: null,
    strategyId: null,
    positionIntent: null,
    optionContract: null,
    legs: null,
    ...overrides
  };
}

function optionContract(overrides: Partial<RiskEscalationOptionContract> = {}): RiskEscalationOptionContract {
  return {
    underlyingSymbol: "AAPL",
    expirationDate: "2026-12-18",
    strikePrice: 150.5,
    right: "C",
    multiplier: "100",
    contractMonth: null,
    tradingClass: null,
    localSymbol: null,
    ...overrides
  };
}

function model(escalations: RiskEscalation[]): GovernedApprovalsViewModel {
  return {
    escalations,
    forbidden: false,
    reasons: {},
    pendingId: null,
    errorText: null,
    statusText: null,
    setReason: vi.fn(),
    approve: vi.fn().mockResolvedValue(undefined),
    deny: vi.fn().mockResolvedValue(undefined),
    refresh: vi.fn().mockResolvedValue(undefined)
  };
}

function renderEntry(entry: RiskEscalation): HTMLElement {
  render(<GovernedApprovalsPanel model={model([entry])} />);
  return screen.getByTestId("governed-approval-entry");
}

/** The value shown beside a label in the entry's detail list. */
function detailValue(entry: HTMLElement, label: string): string | null {
  const term = within(entry).getByText(label, { selector: "dt" });
  return term.nextElementSibling?.textContent ?? null;
}

describe("GovernedApprovalsPanel economic details", () => {
  it("shows every economic detail the server sends for a pending approval", () => {
    const entry = renderEntry(escalation({
      type: "StopLimit",
      quantity: 1,
      limitPrice: 101.25,
      stopPrice: 99.5,
      trailPrice: 1.25,
      trailPercent: 2.5,
      timeInForce: "GoodTilCancelled",
      routedNotional: 500000,
      fundAccountId: FUND_ACCOUNT_ID,
      strategyId: "momentum-desk",
      positionIntent: "BuyToOpen",
      optionContract: optionContract(),
      legs: [
        // Leg enums arrive as ordinals: side 1 is Sell, position intent 2 is SellToOpen.
        { symbol: "AAPL261218C00150500", side: 1, ratioQuantity: 2, positionIntent: 2, optionContract: optionContract() },
        { symbol: "AAPL261218P00140000", side: 0, ratioQuantity: 1, positionIntent: null, optionContract: null }
      ]
    }));

    // A dollar-sized order is described by its routed dollars, never by the placeholder quantity.
    expect(within(entry).getByText("Buy $500,000.00 notional of AAPL stoplimit @ 101.25")).toBeInTheDocument();
    expect(within(entry).queryByText(/Buy 1 AAPL/)).not.toBeInTheDocument();

    const details = within(entry).getByLabelText("Order details for AAPL escalation");
    expect(details.tagName).toBe("DL");
    expect(detailValue(entry, "Routed notional")).toBe("$500,000.00This order is sized in dollars, not in quantity.");
    expect(detailValue(entry, "Stop price")).toBe("99.5");
    expect(detailValue(entry, "Trail amount")).toBe("1.25");
    expect(detailValue(entry, "Trail percent")).toBe("2.5%");
    expect(detailValue(entry, "Time in force")).toBe("Good til cancelled");
    expect(detailValue(entry, "Position intent")).toBe("Buy to open");
    expect(detailValue(entry, "Option contract")).toBe("AAPL · Expires Dec 18, 2026 · Strike 150.5 · Call · Multiplier 100");
    expect(detailValue(entry, "Leg 1")).toBe(
      "Sell AAPL261218C00150500 · Ratio 2 · Sell to open · AAPL · Expires Dec 18, 2026 · Strike 150.5 · Call · Multiplier 100");
    expect(detailValue(entry, "Leg 2")).toBe("Buy AAPL261218P00140000 · Ratio 1");
    expect(detailValue(entry, "Strategy")).toBe("momentum-desk");

    // The fund account arrives as an opaque identifier, so it sits behind the technical
    // disclosure rather than in the reading path.
    expect(within(details).queryByText(FUND_ACCOUNT_ID)).not.toBeInTheDocument();
    const technical = within(entry).getByText("Technical details").closest("details");
    expect(technical).not.toBeNull();
    expect(within(technical as HTMLElement).getByText("Fund account ID")).toBeInTheDocument();
    expect(within(technical as HTMLElement).getByText(FUND_ACCOUNT_ID)).toBeInTheDocument();
  });

  it("shows only the details the server sent when the optional ones are null", () => {
    // A plain limit order: the server always names a time in force and nulls the rest.
    const entry = renderEntry(escalation({ timeInForce: "Day" }));

    expect(within(entry).getByText("Buy 200 AAPL limit @ 100")).toBeInTheDocument();
    expect(detailValue(entry, "Time in force")).toBe("Day");
    for (const label of DETAIL_LABELS.filter((candidate) => candidate !== "Time in force")) {
      expect(within(entry).queryByText(label)).not.toBeInTheDocument();
    }
    expect(within(entry).queryByText("Technical details")).not.toBeInTheDocument();
    expect(entry.textContent).not.toMatch(/null|undefined|NaN|\$0|—/);
  });

  it("renders no detail list and no placeholder values when every economic detail is null or absent", () => {
    const absent = escalation();
    for (const key of ["stopPrice", "trailPrice", "trailPercent", "timeInForce", "routedNotional",
      "fundAccountId", "strategyId", "positionIntent", "optionContract", "legs"] as const) {
      delete (absent as Partial<RiskEscalation>)[key];
    }

    for (const entryModel of [escalation(), absent]) {
      const { unmount } = render(<GovernedApprovalsPanel model={model([entryModel])} />);
      const entry = screen.getByTestId("governed-approval-entry");

      expect(within(entry).getByText("Buy 200 AAPL limit @ 100")).toBeInTheDocument();
      expect(within(entry).queryByLabelText("Order details for AAPL escalation")).not.toBeInTheDocument();
      for (const label of DETAIL_LABELS) {
        expect(within(entry).queryByText(label)).not.toBeInTheDocument();
      }
      expect(entry.textContent).not.toMatch(/null|undefined|NaN|\$0|—/);
      unmount();
    }
  });

  it("omits the parts of an option contract the server left null and never guesses a leg side", () => {
    const entry = renderEntry(escalation({
      optionContract: optionContract({ expirationDate: null, strikePrice: null, right: "X", multiplier: null, localSymbol: "AAPL  261218C00150500" }),
      legs: [{ symbol: "AAPL261218C00150500", side: 7, ratioQuantity: 1, positionIntent: 9, optionContract: null }]
    }));

    expect(detailValue(entry, "Option contract")).toBe("AAPL · X · Local symbol AAPL  261218C00150500");
    // An ordinal outside the server enum is shown as missing, not mapped to a real side or intent.
    expect(detailValue(entry, "Leg 1")).toBe("— AAPL261218C00150500 · Ratio 1");
  });
});
