import { useEffect, useState } from "react";
import { LedgerAmountProofDrawer } from "@/components/meridian/proof-drawer";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { StatusBanner } from "@/components/ui/status-banner";
import { TechnicalDetails } from "@/components/ui/technical-details";
import { formatAmountWithCode, pluralizeCount } from "@/lib/format";
import { getReportAmountBindings } from "@/lib/report-amount-proof-api";
import type { LedgerAmountSelection } from "@/types/ledger-amount-proof";
import type { ReportLedgerAmountBinding } from "@/types/report-amount-proof";
import type { GovernedReportingRun } from "@/types/reporting-governance";

interface AmountState {
  key: string;
  bindings: ReportLedgerAmountBinding[];
  error: string | null;
}

/** One report surface over retained amounts; never reconstructs balances from live journals. */
export function ReportingRetainedAmountsPanel({ run }: { run: Pick<GovernedReportingRun, "runId" | "scope" | "snapshot"> }) {
  const scopeKey = JSON.stringify([run.runId, run.scope.tenantId, run.scope.companyId,
    run.scope.fundId, run.scope.bookId, run.scope.periodId, run.snapshot.sourceCheckpointHash]);
  const [result, setResult] = useState<AmountState | null>(null);
  const [selected, setSelected] = useState<{ key: string; amount: LedgerAmountSelection } | null>(null);
  const [retry, setRetry] = useState(0);

  // Discard the selection on any retained identity change, including a round trip to the old run.
  if (selected && selected.key !== scopeKey) setSelected(null);

  useEffect(() => {
    const controller = new AbortController();
    const [runId, tenantId, companyId, fundId, bookId, periodId, checkpointHash] = JSON.parse(scopeKey) as string[];
    setResult(null);
    setSelected(null);
    void getReportAmountBindings(runId!, { signal: controller.signal }).then((bindings) => {
      if (controller.signal.aborted) return;
      const valid = Array.isArray(bindings) && bindings.every((binding) =>
        binding && typeof binding.amountId === "string" && binding.amountId.length > 0 &&
        binding.subjectId === `report:${runId}:${binding.amountId}` &&
        typeof binding.label === "string" && binding.label.trim().length > 0 &&
        typeof binding.amount === "number" && Number.isFinite(binding.amount) &&
        typeof binding.currency === "string" && /^[A-Z]{3}$/.test(binding.currency) &&
        binding.scope?.tenantId === tenantId && !!tenantId &&
        binding.scope.companyId === companyId && !!companyId &&
        binding.scope.fundProfileId === fundId && !!fundId &&
        binding.scope.ledgerBookId === bookId && !!bookId &&
        binding.scope.periodId === periodId && !!periodId &&
        typeof checkpointHash === "string" && /^[a-fA-F0-9]{64}$/.test(checkpointHash) &&
        binding.sourceSnapshotHash === checkpointHash &&
        hasRetainedEntryIds(binding.journalEntryIds) && hasRetainedEntryIds(binding.ledgerEntryIds));
      const unique = valid && new Set(bindings.map((binding) => binding.amountId)).size === bindings.length;
      setResult({ key: scopeKey, bindings: unique ? bindings : [], error: unique ? null :
        "Blocked: retained amount identities, accounting scope or checkpoint do not match this report run." });
    }).catch(() => {
      if (!controller.signal.aborted) setResult({ key: scopeKey, bindings: [], error:
        "Blocked: retained report amounts are unavailable. Retry the authoritative read." });
    });
    return () => controller.abort();
  }, [scopeKey, retry]);

  const current = result?.key === scopeKey ? result : null;
  const selection = current && !current.error && selected?.key === scopeKey ? selected.amount : null;

  return (
    <Card className="panel-surface">
      <CardHeader>
        <CardTitle>Retained trial-balance amounts</CardTitle>
        <CardDescription>Inspect each account balance against the ledger population retained for this run. Select an amount to verify its supporting evidence.</CardDescription>
      </CardHeader>
      <CardContent className="space-y-3">
        {!current ? <p role="status">Loading retained report amounts…</p> : current.error ? (
          <>
            <StatusBanner role="alert" tone="danger" title="Report amount proof blocked" detail={current.error} />
            <Button type="button" size="sm" variant="outline" onClick={() => setRetry((value) => value + 1)}>Retry retained amounts</Button>
          </>
        ) : current.bindings.length === 0 ? (
          <StatusBanner role="status" tone="warning" title="Report amount proof blocked" detail="No retained amount bindings are available for this report. Generated values cannot acquire evidence from live balances." />
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-sm" aria-label="Retained report account balances">
              <thead className="border-b border-border text-xs text-muted-foreground">
                <tr><th scope="col" className="px-3 py-2 text-left">Account</th><th scope="col" className="px-3 py-2 text-right">Balance</th><th scope="col" className="px-3 py-2 text-left">Retained journal support</th></tr>
              </thead>
              <tbody>
                {current.bindings.map((binding) => (
                  <tr key={binding.amountId} className="border-b border-border/60 align-top">
                    <th scope="row" className="px-3 py-3 text-left font-medium">{binding.label}</th>
                    <td className="px-3 py-3 text-right">
                      <Button type="button" size="sm" variant="ghost" className="whitespace-nowrap font-mono underline underline-offset-4"
                        aria-label={`Inspect ${binding.label} balance ${formatAmountWithCode(binding.amount, binding.currency)}`}
                        onClick={() => setSelected({ key: scopeKey, amount: {
                          subjectId: binding.subjectId, amount: binding.amount, currency: binding.currency,
                          tenantId: binding.scope.tenantId, companyId: binding.scope.companyId,
                          fundProfileId: binding.scope.fundProfileId, ledgerBookId: binding.scope.ledgerBookId,
                          periodId: binding.scope.periodId, label: `${binding.label} report balance`,
                          journalEntryIds: binding.journalEntryIds, ledgerEntryIds: binding.ledgerEntryIds
                        } })}>
                        {formatAmountWithCode(binding.amount, binding.currency)}
                      </Button>
                    </td>
                    <td className="min-w-[16rem] px-3 py-3">
                      <TechnicalDetails label={`${pluralizeCount(binding.journalEntryIds.length, "journal entry", { plural: "journal entries" })} · ${pluralizeCount(binding.ledgerEntryIds.length, "ledger entry", { plural: "ledger entries" })}`}>
                        <dl className="space-y-2 text-xs">
                          <div><dt className="font-semibold">Journal entry IDs</dt><dd className="break-all font-mono">{binding.journalEntryIds.join(" · ")}</dd></div>
                          <div><dt className="font-semibold">Ledger entry IDs</dt><dd className="break-all font-mono">{binding.ledgerEntryIds.join(" · ")}</dd></div>
                          <div><dt className="font-semibold">Retained ledger checkpoint</dt><dd className="break-all font-mono">{binding.sourceSnapshotHash}</dd></div>
                        </dl>
                      </TechnicalDetails>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
        <LedgerAmountProofDrawer selection={selection} onClose={() => setSelected(null)} />
      </CardContent>
    </Card>
  );
}

function hasRetainedEntryIds(value: unknown): value is string[] {
  return Array.isArray(value) && value.length > 0 && value.every((id) => typeof id === "string" && id.length > 0) &&
    new Set(value).size === value.length;
}
