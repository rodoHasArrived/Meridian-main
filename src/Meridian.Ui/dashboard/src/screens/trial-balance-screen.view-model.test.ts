import { describe, expect, it } from "vitest";
import { buildTrialBalanceAccountTreeNodes } from "@/screens/trial-balance-screen.view-model";
import type { AccountingTrialBalanceRowViewModel } from "@/screens/accounting-screen.view-model";

function buildRow(overrides: Partial<AccountingTrialBalanceRowViewModel>): AccountingTrialBalanceRowViewModel {
  return {
    accountName: "Cash",
    accountType: "Asset",
    symbol: null,
    financialAccountId: "acct-cash",
    balance: 0,
    entryCount: 1,
    security: null,
    rowId: "row-cash",
    accountLabel: "Cash",
    accountTypeLabel: "Asset",
    basisLabel: "Primary basis",
    basisTone: "default",
    policyLabel: "policy/v1",
    dimensionLabel: "No dimensions",
    dimensionDetailLabel: "No dimensions",
    balanceLabel: "$0.00",
    balanceTone: "default",
    entryCountLabel: "1",
    ariaLabel: "Cash Asset",
    selectAriaLabel: "Inspect trial-balance account Cash for Asset",
    detailPanelId: "trial-balance-account-detail",
    isExpanded: false,
    ...overrides
  };
}

describe("buildTrialBalanceAccountTreeNodes", () => {
  it("returns an empty tree for no rows", () => {
    expect(buildTrialBalanceAccountTreeNodes([])).toEqual([]);
  });

  it("groups rows into parent nodes by account type with roll-up balances", () => {
    const rows = [
      buildRow({ rowId: "row-cash", financialAccountId: "acct-cash", accountLabel: "Cash", accountTypeLabel: "Asset", balance: 500 }),
      buildRow({ rowId: "row-receivable", financialAccountId: "acct-receivable", accountLabel: "Receivable", accountTypeLabel: "Asset", balance: 200 }),
      buildRow({ rowId: "row-payable", financialAccountId: "acct-payable", accountLabel: "Payable", accountTypeLabel: "Liability", balance: -100 })
    ];

    const nodes = buildTrialBalanceAccountTreeNodes(rows);

    expect(nodes).toHaveLength(2);
    const assetGroup = nodes.find((node) => node.code === "Asset");
    expect(assetGroup?.children).toHaveLength(2);
    expect(assetGroup?.children?.map((child) => child.code)).toEqual(["acct-cash", "acct-receivable"]);
    expect(assetGroup?.children?.map((child) => child.balance)).toEqual([500, 200]);
    expect(assetGroup?.balance).toBeUndefined();

    const liabilityGroup = nodes.find((node) => node.code === "Liability");
    expect(liabilityGroup?.children).toHaveLength(1);
    expect(liabilityGroup?.children?.[0]).toMatchObject({ code: "acct-payable", name: "Payable", balance: -100 });
  });

  it("keys leaf nodes by the real GL account id when one is assigned", () => {
    const rows = [
      buildRow({ rowId: "row-a", financialAccountId: "acct-a", accountLabel: "Cash", accountTypeLabel: "Asset", balance: 10 }),
      buildRow({ rowId: "row-b", financialAccountId: "acct-b", accountLabel: "Cash", accountTypeLabel: "Asset", balance: 20 })
    ];

    const nodes = buildTrialBalanceAccountTreeNodes(rows);

    expect(nodes[0].children?.map((child) => child.code)).toEqual(["acct-a", "acct-b"]);
  });

  it("falls back to the unique rowId when no GL account id is assigned", () => {
    const rows = [
      buildRow({ rowId: "row-a", financialAccountId: null, accountLabel: "Cash", accountTypeLabel: "Asset", balance: 10 }),
      buildRow({ rowId: "row-b", financialAccountId: null, accountLabel: "Cash", accountTypeLabel: "Asset", balance: 20 })
    ];

    const nodes = buildTrialBalanceAccountTreeNodes(rows);

    expect(nodes[0].children?.map((child) => child.code)).toEqual(["row-a", "row-b"]);
  });
});
