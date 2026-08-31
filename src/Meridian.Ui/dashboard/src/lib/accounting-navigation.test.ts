import { describe, expect, it } from "vitest";
import {
  ACCOUNTING_NAVIGATION_GROUPS,
  ACCOUNTING_NAVIGATION_ITEMS,
  appendAccountingNavigationContextToRoute,
  isAccountingNavigationItemActive
} from "@/lib/accounting-navigation";

describe("Accounting navigation", () => {
  it("owns one unique destination list grouped by the accounting lifecycle", () => {
    expect(ACCOUNTING_NAVIGATION_GROUPS.map((group) => group.label)).toEqual([
      "Close",
      "Records",
      "Reconciliation",
      "Review",
      "Administration"
    ]);
    expect(ACCOUNTING_NAVIGATION_GROUPS.map((group) => (
      [group.label, group.items.map((item) => item.label)]
    ))).toEqual([
      ["Close", ["Today", "Operations continuity", "Close calendar"]],
      ["Records", ["Ledger explorer", "Adjustments", "Capital accounts", "Capital calls", "Security Master"]],
      ["Reconciliation", ["Import statement", "Casework", "External GL"]],
      ["Review", ["Exceptions", "Approvals"]],
      ["Administration", ["Entity setup", "Configure"]]
    ]);

    const routes = ACCOUNTING_NAVIGATION_ITEMS.map((item) => item.route);
    expect(routes).toHaveLength(15);
    expect(new Set(routes).size).toBe(routes.length);
    expect(routes).not.toContain("/accounting/reporting");
    expect(routes).not.toContain("/reporting/evidence");
  });

  it("maps detail deep links to one owning destination", () => {
    const activeLabels = (pathname: string) => ACCOUNTING_NAVIGATION_ITEMS
      .filter((item) => isAccountingNavigationItemActive(pathname, item))
      .map((item) => item.label);

    expect(activeLabels("/accounting/accounts/detail")).toEqual(["Ledger explorer"]);
    expect(activeLabels("/accounting/journal-entries/detail")).toEqual(["Adjustments"]);
    expect(activeLabels("/accounting/reconciliation/match")).toEqual(["Casework"]);
    expect(activeLabels("/accounting/reconciliation/external-gl")).toEqual(["External GL"]);
    expect(activeLabels("/accounting/approvals/inbox")).toEqual(["Approvals"]);
    expect(activeLabels("/accounting/security-master/detail")).toEqual(["Security Master"]);
  });

  it("preserves shared accounting context without leaking route-local selection", () => {
    const route = appendAccountingNavigationContextToRoute(
      "/accounting/ledger?fundAccountId=account-alpha&runId=run-42",
      "?fundProfileId=fund-alpha&ledgerBookId=book-alpha&periodId=2026-05&workflowStatus=Blocked&approvalId=approval-1&tab=reference"
    );

    expect(route).toBe(
      "/accounting/ledger?fundAccountId=account-alpha&runId=run-42&fundProfileId=fund-alpha&ledgerBookId=book-alpha&periodId=2026-05&workflowStatus=Blocked"
    );
    expect(route).not.toContain("approvalId");
    expect(route).not.toContain("tab=");
  });
});
