import type { AccountNode } from "@/components/accounting/AccountTree";
import type { AccountingTrialBalanceRowViewModel } from "@/screens/accounting-screen.view-model";

/**
 * AccountTree's select and expand key, which its contract requires to be unique per node.
 *
 * The account id is not unique: the ledger returns one row per account per dimension set, so the
 * same GL account in two funds arrives as two rows sharing it. Keying on it meant the tree showed
 * two identically-coded leaves, selecting either highlighted the first, and the reverse lookup on
 * select resolved both to the first fund's row. `rowId` is the row's own identity. What an
 * operator reads as the account code is rendered separately — see trialBalanceAccountTreeLabel.
 */
export function trialBalanceAccountTreeCode(row: AccountingTrialBalanceRowViewModel): string {
  return row.rowId;
}

/**
 * The mono tag beside a leaf's name: the real GL account id, qualified by the row's dimension
 * scope when it has one, so two rows for the same account can be told apart on sight.
 */
export function trialBalanceAccountTreeLabel(row: AccountingTrialBalanceRowViewModel): string {
  const accountId = row.financialAccountId?.trim();
  if (!accountId) {
    return "";
  }

  const scope = row.dimensionLabel?.trim();
  return scope && scope !== "No dimensions" ? `${accountId} · ${scope}` : accountId;
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
      codeLabel: trialBalanceAccountTreeLabel(row),
      name: row.accountLabel,
      balance: row.balance,
      type: row.accountTypeLabel
    }))
  }));
}
