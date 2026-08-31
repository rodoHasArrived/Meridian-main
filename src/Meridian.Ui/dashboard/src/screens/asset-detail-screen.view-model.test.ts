import { describe, expect, it } from "vitest";
import { buildAssetDetailOverviewViewState, selectExactSecuritySearchResult } from "@/screens/asset-detail-screen.view-model";
import type { CorporateAction, SecurityIdentityDrillIn, SecurityMasterEntry } from "@/types";

const entry: SecurityMasterEntry = {
  securityId: "sec-aapl",
  displayName: "Apple Inc.",
  status: "Active",
  classification: {
    assetClass: "Equity",
    subType: "Common Stock",
    primaryIdentifierKind: "Ticker",
    primaryIdentifierValue: "AAPL"
  },
  economicDefinition: {
    currency: "USD",
    version: 3,
    effectiveFrom: "2020-01-01",
    effectiveTo: null,
    subType: null,
    assetFamily: null,
    issuerType: null
  }
};

const identity: SecurityIdentityDrillIn = {
  securityId: "sec-aapl",
  displayName: "Apple Inc.",
  assetClass: "Equity",
  status: "Active",
  version: 3,
  effectiveFrom: "2020-01-01",
  effectiveTo: null,
  identifiers: [],
  aliases: []
};

const corporateActions: CorporateAction[] = [
  {
    corpActId: "ca-1",
    securityId: "sec-aapl",
    eventType: "Dividend",
    exDate: "2026-06-01",
    payDate: "2026-06-15",
    dividendPerShare: 0.25,
    currency: "USD",
    splitRatio: null,
    newSecurityId: null,
    distributionRatio: null,
    acquirerSecurityId: null,
    exchangeRatio: null,
    subscriptionPricePerShare: null,
    rightsPerShare: null,
    recordDate: null,
    lifecycleState: "Announced",
    supersedesCorpActId: null,
    redemptionPricePercentOfPar: null,
    payload: null,
    payloadSchemaVersion: 1
  }
];

describe("buildAssetDetailOverviewViewState", () => {
  it("derives display fields, status tone, and symbol from the security entry", () => {
    const view = buildAssetDetailOverviewViewState({ entry, identity, corporateActions: [] });

    expect(view.displayName).toBe("Apple Inc.");
    expect(view.symbol).toBe("AAPL");
    expect(view.statusLabel).toBe("Active");
    expect(view.statusTone).toBe("success");
    expect(view.fields).toEqual(
      expect.arrayContaining([
        { label: "Asset class", value: "Equity" },
        { label: "Currency", value: "USD" }
      ])
    );
    expect(view.technicalFields).toEqual([
      { label: "Security ID", value: "sec-aapl" },
      { label: "Version", value: "3" }
    ]);
    expect(view.hasCorporateActions).toBe(false);
  });

  it("falls back to the securityId when no primary identifier is set", () => {
    const view = buildAssetDetailOverviewViewState({
      entry: { ...entry, classification: { ...entry.classification, primaryIdentifierValue: null } },
      identity,
      corporateActions: []
    });

    expect(view.symbol).toBe("sec-aapl");
  });

  it("maps inactive and pending statuses to danger and warning tones", () => {
    expect(buildAssetDetailOverviewViewState({ entry: { ...entry, status: "Inactive" }, identity, corporateActions: [] }).statusTone).toBe("danger");
    expect(buildAssetDetailOverviewViewState({ entry: { ...entry, status: "Pending" }, identity, corporateActions: [] }).statusTone).toBe("warning");
  });

  it("summarizes corporate actions with dividend and pay-date detail", () => {
    const view = buildAssetDetailOverviewViewState({ entry, identity, corporateActions });

    expect(view.hasCorporateActions).toBe(true);
    expect(view.corporateActions).toEqual([
      {
        corpActId: "ca-1",
        label: "Dividend - 2026-06-01",
        detail: "0.25 USD per share, Pay date 2026-06-15"
      }
    ]);
  });

  it("shows Unknown for the version field when identity is unavailable", () => {
    const view = buildAssetDetailOverviewViewState({ entry, identity: null, corporateActions: [] });

    expect(view.technicalFields).toEqual(expect.arrayContaining([{ label: "Version", value: "Unknown" }]));
  });
});

describe("selectExactSecuritySearchResult", () => {
  it("returns the unique exact identifier match case-insensitively", () => {
    expect(selectExactSecuritySearchResult("aapl", [entry])).toBe("sec-aapl");
    expect(selectExactSecuritySearchResult("SEC-AAPL", [entry])).toBe("sec-aapl");
  });

  it("does not auto-select broad or ambiguous results", () => {
    expect(selectExactSecuritySearchResult("apple", [entry])).toBeNull();
    expect(selectExactSecuritySearchResult("AAPL", [entry, { ...entry, securityId: "sec-aapl-duplicate" }])).toBeNull();
  });
});
