import { useEffect, useId, useRef, useState, type FormEvent } from "react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { FormGrid, FormRow } from "@/components/ui/form";
import { Input } from "@/components/ui/input";
import { StatusBanner } from "@/components/ui/status-banner";
import { BondCouponRecognitionPanel } from "@/components/meridian/bond-coupon-recognition-panel";
import { previewBondIncome } from "@/lib/api/bond-workflow.api";
import { describeApiError } from "@/lib/api-errors";
import { bondDecimalText } from "@/lib/bond-decimal-format";
import type { BondIncomePreview } from "@/types/bond-workflow.types";

export function BondIncomePanel({ preview = previewBondIncome }: { preview?: typeof previewBondIncome }) {
  const id = useId(); const [book, setBook] = useState(""); const [account, setAccount] = useState(""); const [lot, setLot] = useState("");
  const [date, setDate] = useState(() => new Date().toISOString().slice(0, 10));
  const [result, setResult] = useState<BondIncomePreview | null>(null); const [pending, setPending] = useState(false);
  const [couponLocked, setCouponLocked] = useState(false);
  const [error, setError] = useState<string | null>(null); const active = useRef<AbortController | null>(null);
  useEffect(() => () => active.current?.abort(), []);
  function edit(change: () => void) { active.current?.abort(); setPending(false); setResult(null); setError(null); change(); }
  async function submit(event: FormEvent) {
    event.preventDefault(); active.current?.abort(); const controller = new AbortController(); active.current = controller;
    setError(null); setResult(null);
    if (![book, account, lot, date].every((value) => value.trim())) { setError("Enter account, book, tax lot and through date."); return; }
    setPending(true);
    try {
      const response = await preview(book.trim(), account.trim(), { ledgerBookId: book.trim(), taxLotRecordId: lot.trim(), throughDate: date, knownAt: new Date().toISOString() }, { signal: controller.signal, allowDevelopmentFallback: false });
      if (controller.signal.aborted) return;
      if (response.ledgerBookId.toLowerCase() !== book.trim().toLowerCase() || response.taxLotRecordId.toLowerCase() !== lot.trim().toLowerCase()) throw new Error("The income preview returned a different book or lot.");
      setResult(response);
    } catch (failure) { if (!controller.signal.aborted) setError(describeApiError(failure, "Income preview unavailable.").summary); }
    finally { if (!controller.signal.aborted) setPending(false); }
  }
  const amounts = result ? [["Opening carrying basis", result.income.openingCarryingBasis], ["Closing carrying basis", result.income.closingCarryingBasis], ["Coupon income", result.income.couponIncome], ["Premium / discount income", result.income.premiumDiscountIncome], ["Observed indexation income", result.income.indexationIncome], ["Total accounting income", result.income.totalAccountingIncome], ["Scheduled principal", result.income.scheduledPrincipal], ["Capitalized fees", result.income.capitalizedFees], ["Expensed fees", result.income.expensedFees], ["Withholding tax", result.income.withholdingTax], ["Interest receivable", result.income.interestReceivable], ["Interest payable", result.income.interestPayable]] as const : [];
  return <Card className="panel-surface"><CardHeader><CardTitle>Bond accounting income preview</CardTitle><CardDescription>US GAAP amortized-cost income from the authoritative lot and book policy. Market-return attribution stays separate. This preview creates no journal or payment.</CardDescription></CardHeader><CardContent className="space-y-4">
    <form onSubmit={submit} aria-label="Bond accounting income inputs" className="space-y-3"><fieldset disabled={couponLocked} className="space-y-3"><legend className="sr-only">Income preview scope</legend><FormGrid columns={2}>
      <FormRow label="Account ID" labelFor={`${id}-account`}><Input id={`${id}-account`} value={account} onChange={(e) => edit(() => setAccount(e.target.value))} required /></FormRow>
      <FormRow label="Ledger book ID" labelFor={`${id}-book`}><Input id={`${id}-book`} value={book} onChange={(e) => edit(() => setBook(e.target.value))} required /></FormRow>
      <FormRow label="Tax lot record ID" labelFor={`${id}-lot`}><Input id={`${id}-lot`} value={lot} onChange={(e) => edit(() => setLot(e.target.value))} required /></FormRow>
      <FormRow label="Income through date" labelFor={`${id}-date`}><Input id={`${id}-date`} type="date" value={date} onChange={(e) => edit(() => setDate(e.target.value))} required /></FormRow>
    </FormGrid><Button type="submit" disabled={pending}>{pending ? "Preparing income preview…" : "Preview accounting income"}</Button></fieldset></form>
    {error ? <StatusBanner tone="danger" role="alert" title="Income preview unavailable" detail={error} /> : null}
    {result ? <section aria-label="Bond accounting income result" className="space-y-3"><p className="text-xs text-muted-foreground">{result.income.modelVersion} · {result.instruction?.bondIncome?.currentProjection.currency ?? "Transaction currency unavailable"} · Book election: {result.income.floaterPolicy === "AcquisitionIndex" ? "Acquisition-index amortization" : "Prospective reset recalculation"} · Effective annual yield {bondDecimalText(result.income.effectiveAnnualYield)}</p><dl className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">{amounts.map(([label, amount]) => <div key={label}><dt className="text-xs text-muted-foreground">{label}</dt><dd className="font-mono text-sm">{bondDecimalText(amount)}</dd></div>)}</dl><details><summary className="cursor-pointer text-sm">Retained review fingerprint</summary><p className="break-all font-mono text-xs">{result.fingerprint}</p></details></section> : null}
    <BondCouponRecognitionPanel key={`${book}-${account}-${lot}-${date}`} bookId={book} accountId={account} lotId={lot} throughDate={date} onLockChange={setCouponLocked} />
  </CardContent></Card>;
}
