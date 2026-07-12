import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import {
  LotsTrackerPanel,
  parseStoredLots,
  parseStoredOverrides,
  SecurityDetailsPanel,
  type OperatorOverridesService
} from "@/components/meridian/security-details-tracker";
import type { SecurityLot } from "@/components/meridian/security-details-tracker.view-model";
import type { OperatorOverridesDto, SecurityIdentityDrillIn, SecurityMasterEntry } from "@/types";

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

const securityEntry: SecurityMasterEntry = {
  securityId: "sec-1",
  displayName: "Apple Inc.",
  status: "Active",
  classification: {
    assetClass: "Equity",
    subType: "CommonStock",
    primaryIdentifierKind: "Ticker",
    primaryIdentifierValue: "AAPL"
  },
  economicDefinition: {
    currency: "USD",
    version: 3,
    effectiveFrom: "2024-01-01T00:00:00Z",
    effectiveTo: null,
    subType: "CommonStock",
    assetFamily: "Equity",
    issuerType: "Corporate"
  }
};

const securityIdentity: SecurityIdentityDrillIn = {
  securityId: "sec-1",
  displayName: "Apple Inc.",
  assetClass: "Equity",
  status: "Active",
  version: 3,
  effectiveFrom: "2024-01-01T00:00:00Z",
  effectiveTo: null,
  identifiers: [
    {
      kind: "Ticker",
      value: "AAPL",
      isPrimary: true,
      validFrom: "2024-01-01T00:00:00Z",
      validTo: null,
      provider: "Nasdaq"
    }
  ],
  aliases: []
};

const operatorOverrides: OperatorOverridesDto = {
  securityId: "sec-1",
  values: {
    issuer: "Apple Inc."
  },
  updatedBy: "ops",
  updatedAt: "2026-05-19T09:00:00Z"
};

describe("LotsTrackerPanel", () => {
  it("validates persisted lots before using them", () => {
    expect(parseStoredLots([
      { lotId: "lot-1", tradeDate: "2026-01-02", quantity: 10, price: 20 },
      { lotId: "bad-fees", tradeDate: "2026-01-02", quantity: 10, price: 20, fees: "wrong" },
      { lotId: "bad-price", tradeDate: "2026-01-02", quantity: 10, price: "20" }
    ])).toEqual([
      { lotId: "lot-1", tradeDate: "2026-01-02", quantity: 10, price: 20, fees: 0, note: "" }
    ]);
  });

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

describe("SecurityDetailsPanel", () => {
  it("validates persisted local overrides before using them", () => {
    expect(parseStoredOverrides({
      marketPrice: "101.25",
      empty: 42,
      note: "operator note"
    })).toEqual({ marketPrice: "101.25", note: "operator note" });

    expect(parseStoredOverrides(["bad"])).toEqual({});
  });

  beforeEach(() => {
    window.localStorage.clear();
  });

  it("renders sync status and disables override commands from the view model while loading", async () => {
    const overridesService: OperatorOverridesService = {
      get: vi.fn<OperatorOverridesService["get"]>(() => new Promise<OperatorOverridesDto>(() => undefined)),
      patch: vi.fn<OperatorOverridesService["patch"]>()
    };

    render(
      <SecurityDetailsPanel
        entry={securityEntry}
        identity={securityIdentity}
        tradingParameters={null}
        overridesService={overridesService}
      />
    );

    expect(await screen.findByLabelText("Security override sync status: Loading from server")).toHaveTextContent("Loading from server");
    const editIdentifier = screen.getByRole("button", { name: "Edit Identifier" });
    expect(editIdentifier).toBeDisabled();
    expect(editIdentifier).toHaveAttribute("title", "Security overrides are still loading from the server.");
  });

  it("renders editor helper semantics from the view model when editing an override", async () => {
    const overridesService: OperatorOverridesService = {
      get: vi.fn<OperatorOverridesService["get"]>().mockResolvedValue(operatorOverrides),
      patch: vi.fn<OperatorOverridesService["patch"]>().mockResolvedValue(operatorOverrides)
    };
    const user = userEvent.setup();

    render(
      <SecurityDetailsPanel
        entry={securityEntry}
        identity={securityIdentity}
        tradingParameters={null}
        overridesService={overridesService}
      />
    );

    await screen.findByText("Synced");
    await user.click(screen.getByRole("button", { name: "Edit Issuer" }));

    const issuerEditor = screen.getByLabelText("Issuer override value");
    expect(issuerEditor).toHaveAttribute("id", "security-detail-sec-1-issuer-editor");
    expect(issuerEditor).toHaveAttribute(
      "aria-describedby",
      "security-detail-sec-1-issuer-editor-help security-detail-sec-1-issuer-editor-disabled-reason"
    );
    expect(screen.getByText("Enter a value to override the server field, or leave it blank to clear the override.")).toHaveAttribute(
      "id",
      "security-detail-sec-1-issuer-editor-help"
    );
    expect(screen.getByRole("button", { name: "Save Issuer" })).toBeEnabled();
  });
});
