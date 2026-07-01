import type { AccountNode } from "@/components/accounting/AccountTree";
import type { AccountingTrialBalanceRowViewModel } from "@/screens/accounting-screen.view-model";

// AccountTree renders `code` as a visible mono tag next to `name`, so leaves use the real GL
// account id (falling back to the unique rowId only when no account id is assigned) instead of
// the internal composite rowId, which would otherwise show as a confusing account "code".
export function trialBalanceAccountTreeCode(row: AccountingTrialBalanceRowViewModel): string {
  return row.financialAccountId ?? row.rowId;
}

export function buildTrialBalanceAccountTreeNodes(
  rows: AccountingTrialBalanceRowViewModel[]
): AccountNode[] {
  const groups = new Map<string, AccountingTrialBalanceRowViewModel[]>();

  for (const row of rows) {
    const key = row.accountTypeLabel;
    const bucket = groups.get(key);
    if (bucket) {
      bucket.push(row);
    } else {
      groups.set(key, [row]);
    }
  }

  return Array.from(groups.entries()).map(([accountTypeLabel, groupRows]) => ({
    code: accountTypeLabel,
    name: accountTypeLabel,
    type: accountTypeLabel,
    children: groupRows.map((row) => ({
      code: trialBalanceAccountTreeCode(row),
      name: row.accountLabel,
      balance: row.balance,
      type: row.accountTypeLabel
    }))
  }));
}
