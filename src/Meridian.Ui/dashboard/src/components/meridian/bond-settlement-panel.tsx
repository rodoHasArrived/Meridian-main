import { type FormEvent, useEffect, useId, useRef, useState } from "react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { FormGrid, FormRow } from "@/components/ui/form";
import { Input } from "@/components/ui/input";
import { StatusBanner } from "@/components/ui/status-banner";
import { prepareBondSettlement } from "@/lib/api/bond-settlement.api";
import { describeApiError } from "@/lib/api-errors";
import { bondDecimalText, DECIMAL_LITERAL, utcInstantFromLocalInput } from "@/lib/bond-decimal-format";
import type { BondSettlementResult } from "@/types/bond-settlement.types";

const initialInputs = () => ({
  asOf: new Date().toISOString().slice(0, 10), settlementDate: "", knownAt: "", securityVersion: "",
  cleanPrice: "", originalFace: "", bookedFactor: "1", fees: "0", observedDirtyCash: "",
  sourceEvidenceId: "", cashAccountId: "", accruedInterestAccountId: "", feesAccountId: ""
});
type Inputs = ReturnType<typeof initialInputs>;
const fields: Array<{ key: keyof Inputs; label: string; type?: string }> = [
  { key: "asOf", label: "Settlement calculation date (UTC)", type: "date" },
  { key: "settlementDate", label: "Trade settlement date", type: "date" },
  { key: "knownAt", label: "Settlement knowledge cutoff (UTC, optional)", type: "datetime-local" },
  { key: "securityVersion", label: "Settlement terms version" },
  { key: "cleanPrice", label: "Settlement clean price per 100 face" },
  { key: "originalFace", label: "Original face amount" },
  { key: "bookedFactor", label: "Booked factor (0 to 1)" },
  { key: "fees", label: "Separately expensed fees" },
  { key: "observedDirtyCash", label: "Observed dirty cash (including fees)" },
  { key: "sourceEvidenceId", label: "Retained cash evidence ID" },
  { key: "cashAccountId", label: "Cash ledger account ID" },
  { key: "accruedInterestAccountId", label: "Purchased accrued-interest account ID" },
  { key: "feesAccountId", label: "Fee expense account ID (required for fees)" }
];

/** Preparation only: calculations and source reconciliation never grant posting authority. */
export function BondSettlementPanel({ securityId, termsVersion, prepare = prepareBondSettlement }: {
  securityId: string; termsVersion: number | null; prepare?: typeof prepareBondSettlement;
}) {
  const id = useId();
  const [inputs, setInputs] = useState<Inputs>(() => ({ ...initialInputs(), securityVersion: termsVersion ? String(termsVersion) : "" }));
  const [result, setResult] = useState<BondSettlementResult | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [pending, setPending] = useState(false);
  const inFlight = useRef<AbortController | null>(null);
  useEffect(() => {
    inFlight.current?.abort();
    setInputs({ ...initialInputs(), securityVersion: termsVersion ? String(termsVersion) : "" });
    setResult(null); setError(null); setPending(false);
    return () => inFlight.current?.abort();
  }, [securityId, termsVersion]);

  function edit(change: () => void) {
    inFlight.current?.abort(); setResult(null); setError(null); setPending(false); change();
  }

  async function submit(event: FormEvent) {
    event.preventDefault();
    inFlight.current?.abort();
    setResult(null); setError(null);
    const decimalKeys = ["cleanPrice", "originalFace", "bookedFactor", "fees", "observedDirtyCash"] as const;
    if (!inputs.asOf || !inputs.settlementDate || !/^[1-9]\d*$/.test(inputs.securityVersion)
      || !Number.isSafeInteger(Number(inputs.securityVersion))
      || decimalKeys.some((key) => !DECIMAL_LITERAL.test(inputs[key].trim()))
      || !inputs.sourceEvidenceId.trim() || !inputs.cashAccountId.trim() || !inputs.accruedInterestAccountId.trim()) {
      setError("Supply dates, a positive exact terms version, decimal economics, retained cash evidence and ledger accounts."); return;
    }
    if (!/^0*(?:\.0*)?$/.test(inputs.fees.trim()) && !inputs.feesAccountId.trim()) {
      setError("Supply a fee expense account when fees are nonzero."); return;
    }
    const accounts = [inputs.cashAccountId.trim(), inputs.accruedInterestAccountId.trim(), inputs.feesAccountId.trim()].filter(Boolean);
    if (new Set(accounts).size !== accounts.length) { setError("Cash, purchased-interest and fee accounts must be distinct."); return; }
    const controller = new AbortController(); inFlight.current = controller; setPending(true);
    try {
      const response = await prepare({
        analytics: {
          securityId, context: {
            asOf: `${inputs.asOf}T00:00:00Z`, settlementDate: inputs.settlementDate,
            securityVersion: Number(inputs.securityVersion),
            ...(inputs.knownAt ? { knownAt: utcInstantFromLocalInput(inputs.knownAt) } : {})
          }, cleanPrice: inputs.cleanPrice.trim(), yieldConvention: "NominalPeriodic"
        },
        originalFace: inputs.originalFace.trim(), bookedFactor: inputs.bookedFactor.trim(),
        fees: inputs.fees.trim(), observedDirtyCash: inputs.observedDirtyCash.trim(),
        sourceEvidenceId: inputs.sourceEvidenceId.trim(), cashAccountId: inputs.cashAccountId.trim(),
        accruedInterestAccountId: inputs.accruedInterestAccountId.trim(), feesAccountId: inputs.feesAccountId.trim() || null
      }, { signal: controller.signal, allowDevelopmentFallback: false });
      if (controller.signal.aborted) return;
      if (response.settlement && response.settlement.securityId.toLowerCase() !== securityId.toLowerCase())
        throw new Error("Settlement returned a different security. Refresh the holding.");
      setResult(response);
    } catch (failure) {
      if (!controller.signal.aborted) setError(describeApiError(failure, "Settlement preparation unavailable. Check Security Master view permission and service configuration.").summary);
    } finally { if (!controller.signal.aborted) setPending(false); }
  }

  const settlement = result && !result.isBlocked && result.blockers.length === 0 ? result.settlement : null;
  return <Card className="panel-surface">
    <CardHeader><CardTitle>Bond settlement preparation</CardTitle><CardDescription>
      Reconcile acquisition clean cost, purchased interest, fees and observed cash for this holding.
      Requires View Security Master or Modify Security Master permission. Preparation does not book a trade or post a journal.
    </CardDescription></CardHeader>
    <CardContent className="space-y-4">
      <form onSubmit={submit} aria-label="Bond settlement inputs" className="space-y-4">
        <FormGrid columns={3}>{fields.map(({ key, label, type }) => <FormRow key={key} label={label} labelFor={`${id}-${key}`}>
          <Input id={`${id}-${key}`} type={type ?? "text"} value={inputs[key]} onChange={(event) => edit(() => setInputs((current) => ({ ...current, [key]: event.target.value })))} />
        </FormRow>)}</FormGrid>
        <Button type="submit" disabled={pending}>{pending ? "Preparing settlement…" : "Prepare settlement"}</Button>
      </form>
      {error ? <StatusBanner role="alert" tone="danger" title="Settlement preparation unavailable" detail={error} /> : null}
      {result?.isBlocked || (result && result.blockers.length > 0) ? <StatusBanner role="alert" tone="warning" title="Settlement is blocked" detail={<ul>{result.blockers.map((blocker, index) => <li key={index}>{blocker.code}: {blocker.reason}</li>)}</ul>} /> : null}
      {settlement ? <section aria-label="Prepared settlement review" className="space-y-3">
        <StatusBanner role="status" tone="success" title={<span className="text-foreground">Settlement reconciled for review</span>} detail="Clean cost and purchased interest remain separate. Fees are separately expensed. No journal or lot was created." />
        <dl className="grid gap-3 md:grid-cols-3">{([
          ["Currency", settlement.currency], ["Clean consideration", bondDecimalText(settlement.cleanConsideration)],
          ["Purchased accrued interest", bondDecimalText(settlement.purchasedAccruedInterest)], ["Expensed fees", bondDecimalText(settlement.fees)],
          ["Total dirty consideration", bondDecimalText(settlement.dirtyConsideration)], ["Observed cash", bondDecimalText(settlement.observedDirtyCash)],
          ["Settlement date", settlement.context.settlementDate ?? "Unavailable"], ["Model", settlement.modelVersion],
          ["Cash evidence", settlement.sourceEvidenceId], ["Cash account", settlement.cashAccountId],
          ["Accrued-interest account", settlement.accruedInterestAccountId], ["Fee account", settlement.feesAccountId ?? "Not applicable"]
        ] as const).map(([label, value]) => <div key={label}><dt className="text-xs text-muted-foreground">{label}</dt><dd className="break-all font-mono text-sm">{value}</dd></div>)}</dl>
        <details><summary className="cursor-pointer text-sm font-semibold">Exact settlement calculation evidence</summary>
          <ul className="space-y-2 pt-2 text-xs font-mono">{settlement.inputs.map((input, index) => <li key={index} className="break-all">{input.kind}: {input.key} · version {input.version ?? input.versionId ?? "Unavailable"} · SHA-256 {input.contentSha256 ?? "Unavailable"}</li>)}</ul>
        </details>
      </section> : null}
    </CardContent>
  </Card>;
}
