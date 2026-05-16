import { describe, expect, it } from "vitest";
import {
  buildLotsTrackerViewModel,
  buildSecurityDetailsViewModel,
  SECURITY_DETAIL_GROUPS,
  type SecurityLot
} from "./security-details-tracker.view-model";
import type { SecurityIdentityDrillIn, SecurityMasterEntry } from "@/types";

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

function buildSecurityDetailsVm(
  options: {
    assetClass?: string;
    entry?: SecurityMasterEntry | null;
    identity?: SecurityIdentityDrillIn | null;
    overrides?: Record<string, string>;
  } = {}
) {
  const assetClass = options.assetClass ?? "Equity";
  return buildSecurityDetailsViewModel({
    entry: options.entry === undefined
      ? { ...securityEntry, classification: { ...securityEntry.classification, assetClass } }
      : options.entry,
    identity: options.identity === undefined
      ? { ...securityIdentity, assetClass }
      : options.identity,
    tradingParameters: null,
    overrides: options.overrides ?? {}
  });
}

function visibleFieldKeys(vm: ReturnType<typeof buildSecurityDetailsViewModel>): string[] {
  return vm.groups.flatMap((group) => group.fields.map((field) => field.key));
}

function visibleGroupIds(vm: ReturnType<typeof buildSecurityDetailsViewModel>): string[] {
  return vm.groups.map((group) => group.id);
}

describe("buildLotsTrackerViewModel", () => {
  it("projects lots into selectable rows, totals, and selected detail state", () => {
    const vm = buildVm();

    expect(vm.addCommand.disabled).toBe(false);
    expect(vm.formLabel).toBe("Add purchase lot for AAPL");
    expect(vm.draftStatus).toEqual({
      id: "security-lots-aapl-draft-status",
      role: "status",
      tone: "success",
      text: "Lot draft is ready to add."
    });
    expect(vm.draftFields.quantity).toMatchObject({
      id: "security-lots-aapl-draft-quantity",
      label: "Quantity",
      type: "number",
      step: "any",
      placeholder: "100",
      invalid: false,
      errorText: null,
      describedBy: "security-lots-aapl-draft-quantity-help"
    });
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
    expect(vm.draftStatus).toMatchObject({
      role: "status",
      tone: "warning",
      text: "Add lot unavailable: Quantity must be a non-zero number."
    });
    expect(vm.draftFields.quantity).toMatchObject({
      invalid: true,
      errorText: "Quantity must be a non-zero number.",
      errorId: "security-lots-aapl-draft-quantity-error",
      describedBy: "security-lots-aapl-draft-quantity-help security-lots-aapl-draft-quantity-error"
    });
    expect(vm.draftFields.price.invalid).toBe(false);
  });

  it("keeps all add-lot draft field labels, helpers, and validation ids in the view model", () => {
    const vm = buildVm({
      draft: {
        tradeDate: "",
        quantity: "",
        price: "0",
        fees: "abc",
        note: ""
      }
    });

    expect(Object.keys(vm.draftFields)).toEqual(["tradeDate", "quantity", "price", "fees", "note"]);
    expect(vm.draftFields.tradeDate).toMatchObject({
      id: "security-lots-aapl-draft-trade-date",
      label: "Trade date",
      helperText: "Required for lot chronology and selected-lot evidence.",
      errorText: "Trade date is required."
    });
    expect(vm.draftFields.price).toMatchObject({
      errorText: "Price must be greater than zero.",
      describedBy: "security-lots-aapl-draft-price-help security-lots-aapl-draft-price-error"
    });
    expect(vm.draftFields.fees).toMatchObject({
      errorText: "Fees must be a number.",
      describedBy: "security-lots-aapl-draft-fees-help security-lots-aapl-draft-fees-error"
    });
    expect(vm.draftFields.note).toMatchObject({
      invalid: false,
      errorText: null,
      describedBy: "security-lots-aapl-draft-note-help"
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

  it("projects pending lot removal as a row-owned confirmation action", () => {
    const vm = buildVm({ pendingRemoveLotId: "lot-aapl-2" });

    expect(vm.rows[1]).toMatchObject({
      lotId: "lot-aapl-2",
      removeLabel: "Confirm remove",
      removeAriaLabel: "Confirm remove AAPL lot from 2026-04-15. This deletes the local cost-basis lot.",
      removeConfirmationPending: true
    });
    expect(vm.rows[1]?.ariaLabel).toContain("Remove confirmation pending.");
    expect(vm.rows[0]).toMatchObject({
      removeLabel: "Remove",
      removeAriaLabel: "Remove AAPL lot from 2026-04-01",
      removeConfirmationPending: false
    });
  });
});

describe("buildSecurityDetailsViewModel", () => {
  it("shows equity fields while hiding bond-only detail groups", () => {
    const vm = buildSecurityDetailsVm({ assetClass: "Equity" });
    const keys = visibleFieldKeys(vm);

    expect(visibleGroupIds(vm)).toContain("issuer");
    expect(keys).toEqual(expect.arrayContaining(["identifier", "assetClass", "currency", "issuer", "shareClass", "sharesOutstanding", "marketPrice", "fedTax"]));
    expect(keys).not.toEqual(expect.arrayContaining(["couponRate", "finalMaturity", "spRating", "factor"]));
    expect(visibleGroupIds(vm)).not.toEqual(expect.arrayContaining(["ratings", "coupon", "maturity", "conversion"]));
  });

  it("shows fixed-income ratings, coupon, maturity, and duration fields", () => {
    const vm = buildSecurityDetailsVm({ assetClass: "FixedIncome" });
    const keys = visibleFieldKeys(vm);

    expect(visibleGroupIds(vm)).toEqual(expect.arrayContaining(["ratings", "coupon", "maturity", "pricing", "conversion", "regulatory"]));
    expect(keys).toEqual(expect.arrayContaining(["spRating", "couponRate", "finalMaturity", "factor", "yield", "duration", "convexity"]));
    expect(keys).not.toEqual(expect.arrayContaining(["shareClass", "sharesOutstanding", "contractSize"]));
  });

  it("keeps option and future assets to contract and pricing fields", () => {
    for (const assetClass of ["Option", "Future"]) {
      const vm = buildSecurityDetailsVm({ assetClass });
      const keys = visibleFieldKeys(vm);

      expect(keys).toEqual(expect.arrayContaining(["identifier", "assetClass", "currency", "marketPrice", "contractSize"]));
      expect(keys).not.toEqual(expect.arrayContaining(["issuerConcentration", "couponRate", "finalMaturity", "spRating"]));
      expect(visibleGroupIds(vm)).toContain("pricing");
      expect(visibleGroupIds(vm)).not.toEqual(expect.arrayContaining(["issuer", "coupon", "ratings", "maturity"]));
    }
  });

  it("keeps the full field set visible for unknown asset classes", () => {
    const vm = buildSecurityDetailsVm({ assetClass: "StructuredNote" });
    const allGroupIds = SECURITY_DETAIL_GROUPS.map((group) => group.id);
    const allFieldKeys = SECURITY_DETAIL_GROUPS.flatMap((group) => group.fields.map((field) => field.key));

    expect(visibleGroupIds(vm)).toEqual(allGroupIds);
    expect(visibleFieldKeys(vm)).toEqual(allFieldKeys);
  });

  it("preserves hidden overrides in metadata without rendering them as visible fields", () => {
    const vm = buildSecurityDetailsVm({
      assetClass: "Equity",
      overrides: {
        assetClass: "Equity",
        couponRate: "5.25",
        finalMaturity: "2032-06-30",
        issuer: "Apple Inc."
      }
    });
    const keys = visibleFieldKeys(vm);
    const issuer = vm.groups.flatMap((group) => group.fields).find((field) => field.key === "issuer");

    expect(vm.overrideCount).toBe(4);
    expect(vm.hiddenOverrideCount).toBe(2);
    expect(keys).not.toEqual(expect.arrayContaining(["couponRate", "finalMaturity"]));
    expect(issuer).toMatchObject({
      value: "Apple Inc.",
      displayValue: "Apple Inc.",
      isOverridden: true
    });
  });
});
