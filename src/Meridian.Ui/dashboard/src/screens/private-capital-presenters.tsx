import { WORKSTATION_ROUTE_CATALOG } from "@/lib/workspace";
import type { CapitalDecimal, PrivateCapitalCall, PrivateCapitalCommitment } from "@/types/private-capital";

export type CapitalMutation = (label: string, action: (signal: AbortSignal) => Promise<unknown>, options?: { refresh?: boolean }) => Promise<boolean>;
/** Preserve exact decimal text; formatting never rounds or calculates a financial result. */
export const capitalAmount = (amount: CapitalDecimal, currency: string) => `${amount} ${currency}`;
export const capitalIsPositive = (amount: CapitalDecimal) => /^\+?0*[1-9]\d*(?:\.\d+)?$|^\+?0*\.\d*[1-9]\d*$/.test(String(amount));
function validCapitalDate(value: string) {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(value)) return false;
  const timestamp = Date.parse(`${value}T00:00:00Z`);
  return Number.isFinite(timestamp) && new Date(timestamp).toISOString().slice(0, 10) === value;
}
/** Evaluate server-observed posted balances at the server's UTC date, without calculating money. */
export function capitalCallIsOverdue(call: PrivateCapitalCall, generatedAtUtc: string) {
  if (!validCapitalDate(call.dueDate) || !/^\d{4}-\d{2}-\d{2}T.*(?:Z|[+-]\d{2}:\d{2})$/.test(generatedAtUtc)
    || !validCapitalDate(generatedAtUtc.slice(0, 10))) return false;
  const generated = Date.parse(generatedAtUtc);
  return Number.isFinite(generated) && new Date(generated).toISOString().slice(0, 10) > call.dueDate
    && call.lines.some(line => capitalIsPositive(line.issuedAmount) && capitalIsPositive(line.outstandingAmount));
}
/** Mirror the server's effective revision selection; an inactive successor never revives old terms. */
export function effectiveCapitalCommitments(commitments: PrivateCapitalCommitment[], noticeDate: string) {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(noticeDate)) return [];
  const selected = new Map<string, PrivateCapitalCommitment>();
  for (const row of commitments) {
    if (row.approvalStatus !== 1 || row.effectiveFrom > noticeDate) continue;
    const current = selected.get(row.commitmentId);
    if (!current || row.effectiveFrom > current.effectiveFrom
      || (row.effectiveFrom === current.effectiveFrom && row.revision > current.revision)) selected.set(row.commitmentId, row);
  }
  return [...selected.values()].filter(row => row.isActive);
}
export function capitalJournalRoute(bookId: string, periodId: string, journalEntryId?: string) {
  return `${WORKSTATION_ROUTE_CATALOG.accountingJournalEntries}?${new URLSearchParams({ ledgerBookId: bookId,
    ...(periodId ? { periodId } : {}), ...(journalEntryId ? { journalEntryId } : {}) })}`;
}
export function CapitalCallLines({ call }: { call: PrivateCapitalCall }) {
  return <div className="overflow-x-auto"><table className="w-full min-w-[850px] text-left text-sm"><caption className="sr-only">Call allocation and posted progress</caption><thead><tr>
    {['Installment / investor', 'Allocation', 'Uncalled before', 'Uncalled after', 'Issued', 'Funded', 'Outstanding'].map(label => <th key={label} scope="col" className="p-2">{label}</th>)}
  </tr></thead><tbody>{call.lines.map(line => <tr key={line.installmentId} className="border-t border-border"><th scope="row" className="p-2 font-medium">{line.installmentId}<span className="block text-xs text-muted-foreground">{line.investorId} · Commitment v{line.commitmentRevision}</span></th>
    {[line.amount, line.uncalledBefore, line.uncalledAfter, line.issuedAmount, line.fundedAmount, line.outstandingAmount].map((value, index) => <td key={index} className="p-2 font-mono">{capitalAmount(value, call.scope.currency)}</td>)}
  </tr>)}</tbody></table></div>;
}
