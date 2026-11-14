import { useEffect, useId, useRef, useState } from "react";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { FormRow } from "@/components/ui/form";
import { StatusBanner } from "@/components/ui/status-banner";
import { prepareBondCoupon, previewBondCoupon } from "@/lib/api/bond-workflow.api";
import { describeApiError } from "@/lib/api-errors";
import { bondDecimalText } from "@/lib/bond-decimal-format";
import type { BondCouponRecognitionPreparation, BondCouponRecognitionPreview, BondCouponRecognitionRequest } from "@/types/bond-workflow.types";

export function BondCouponRecognitionPanel({ bookId, accountId, lotId, throughDate, onLockChange, preview = previewBondCoupon, prepare = prepareBondCoupon }: {
  bookId: string; accountId: string; lotId: string; throughDate: string; onLockChange: (locked: boolean) => void;
  preview?: typeof previewBondCoupon; prepare?: typeof prepareBondCoupon;
}) {
  const id = useId(); const [period, setPeriod] = useState(""); const [allocations, setAllocations] = useState("");
  const [reversesReceipt, setReversesReceipt] = useState(""); const [reversalRationale, setReversalRationale] = useState("");
  const [request, setRequest] = useState<BondCouponRecognitionRequest | null>(null);
  const [result, setResult] = useState<BondCouponRecognitionPreview | null>(null);
  const [prepared, setPrepared] = useState<BondCouponRecognitionPreparation | null>(null);
  const [reviewed, setReviewed] = useState(false); const [uncertain, setUncertain] = useState(false);
  const [busy, setBusy] = useState<"preview" | "prepare" | null>(null); const [error, setError] = useState<string | null>(null);
  const active = useRef<AbortController | null>(null);
  const locked = busy === "prepare" || uncertain || prepared !== null;
  useEffect(() => { onLockChange(locked); }, [locked, onLockChange]);
  useEffect(() => () => { active.current?.abort(); onLockChange(false); }, [onLockChange]);
  function edit(change: () => void) { active.current?.abort(); setBusy(null); setRequest(null); setResult(null); setPrepared(null); setReviewed(false); setError(null); change(); }
  function validate(response: BondCouponRecognitionPreview, expected: BondCouponRecognitionRequest) {
    if (response.instruction.scope.bookId.toLowerCase() !== bookId.trim().toLowerCase()
      || response.instruction.scope.accountId.toLowerCase() !== accountId.trim().toLowerCase()
      || response.instruction.request.taxLotRecordId.toLowerCase() !== lotId.trim().toLowerCase()
      || response.instruction.request.periodId.toLowerCase() !== period.trim().toLowerCase()
      || response.instruction.request.runId !== expected.runId
      || (response.instruction.request.reversesReceiptId ?? null) !== (expected.reversesReceiptId ?? null))
      throw new Error("Coupon recognition returned a different account, book, lot, period or run.");
  }
  async function calculate() {
    active.current?.abort(); const controller = new AbortController(); active.current = controller; setError(null); setResult(null); setReviewed(false);
    if (![bookId, accountId, lotId, period, throughDate].every((value) => value.trim())) { setError("Enter account, book, lot, accounting period and through date first."); return; }
    setBusy("preview");
    try {
      const selected = allocations.trim() ? JSON.parse(allocations) as BondCouponRecognitionRequest["cashAllocations"] : [];
      if (!Array.isArray(selected)) throw new Error("Coupon cash allocations must be an array of retained monitoring references.");
      if (Boolean(reversesReceipt.trim()) !== Boolean(reversalRationale.trim())) throw new Error("A coupon reversal requires both its posted receipt ID and correction rationale.");
      const bound = request ?? { runId: crypto.randomUUID(), taxLotRecordId: lotId.trim(), periodId: period.trim(), throughDate, knownAt: new Date().toISOString(), cashAllocations: selected,
        ...(reversesReceipt.trim() ? { reversesReceiptId: reversesReceipt.trim(), correctionRationale: reversalRationale.trim() } : {}) };
      setRequest(bound);
      const response = await preview(bookId.trim(), accountId.trim(), bound, { signal: controller.signal, allowDevelopmentFallback: false });
      if (controller.signal.aborted) return; validate(response, bound); setResult(response);
    } catch (failure) { if (!controller.signal.aborted) setError(describeApiError(failure, "Coupon recognition preview unavailable.").summary); }
    finally { if (!controller.signal.aborted) setBusy(null); }
  }
  async function prepareForReview() {
    if (!request || !result || !reviewed || !result.hasChanges || prepared || busy) return;
    active.current?.abort(); const controller = new AbortController(); active.current = controller; setBusy("prepare"); setError(null);
    try {
      const response = await prepare(bookId.trim(), accountId.trim(), { request, expectedFingerprint: result.fingerprint }, { signal: controller.signal, allowDevelopmentFallback: false });
      if (controller.signal.aborted) return; validate(response.preparation.preview, request);
      if (response.preparation.preview.fingerprint !== result.fingerprint || response.projection.spine.eventId !== result.instruction.eventId)
        throw new Error("Prepared coupon source does not match the reviewed fingerprint or canonical event.");
      setPrepared(response); setUncertain(false);
    } catch (failure) { if (!controller.signal.aborted) { setUncertain(true); setError(describeApiError(failure, "Preparation was not confirmed. Retry the same request before replacing it.").summary); } }
    finally { if (!controller.signal.aborted) setBusy(null); }
  }
  return <details className="rounded border border-border p-3"><summary className="cursor-pointer text-sm font-medium">Coupon accrual and collection for governed review</summary><div className="mt-3 space-y-3">
    <p className="text-xs text-muted-foreground">Uses the account, book, lot and through date above. Empty allocations prepare earned accrual only. Cash clearing requires retained monitoring allocation IDs and payment versions. Preparation creates a canonical Income source for review; approval and posting remain separate. No payment is initiated.</p>
    <fieldset disabled={locked} className="space-y-3"><legend className="sr-only">Coupon recognition inputs</legend>
      <FormRow label="Coupon accounting period ID" labelFor={`${id}-period`}><Input id={`${id}-period`} value={period} onChange={(event) => edit(() => setPeriod(event.target.value))} /></FormRow>
      <FormRow label="Retained coupon cash allocations JSON (optional)" labelFor={`${id}-cash`} hint="Supply selected retained allocations; omit for accrual only. The server verifies amounts, source scope and prior use."><textarea id={`${id}-cash`} value={allocations} onChange={(event) => edit(() => setAllocations(event.target.value))} className="min-h-24 w-full rounded border border-border bg-background p-2 font-mono text-xs" /></FormRow>
      <details><summary className="cursor-pointer text-sm">Reverse a posted coupon receipt</summary><p className="mt-2 text-xs text-muted-foreground">The server verifies the posted target, latest receipt, accounting period and reversal eligibility. The original receipt and journal remain retained.</p><FormRow label="Posted coupon receipt ID to reverse" labelFor={`${id}-reverse`}><Input id={`${id}-reverse`} value={reversesReceipt} onChange={(event) => edit(() => setReversesReceipt(event.target.value))} /></FormRow><FormRow label="Coupon reversal rationale" labelFor={`${id}-reverse-rationale`}><Input id={`${id}-reverse-rationale`} value={reversalRationale} onChange={(event) => edit(() => setReversalRationale(event.target.value))} /></FormRow></details>
      <Button type="button" variant="outline" disabled={busy !== null} onClick={() => void calculate()}>{busy === "preview" ? "Resolving coupon recognition…" : "Preview coupon recognition"}</Button>
    </fieldset>
    {error ? <StatusBanner role="alert" tone="danger" title={uncertain ? "Coupon preparation unconfirmed" : "Coupon preview unavailable"} detail={error} /> : null}
    {result ? <section aria-label="Coupon recognition preview" className="space-y-3"><dl className="grid gap-3 sm:grid-cols-3"><div><dt className="text-xs text-muted-foreground">New earned coupon</dt><dd className="font-mono">{bondDecimalText(result.earnedDelta)}</dd></div><div><dt className="text-xs text-muted-foreground">Retained gross cash settled</dt><dd className="font-mono">{bondDecimalText(result.settledGross)}</dd></div><div><dt className="text-xs text-muted-foreground">Net remaining signed balance</dt><dd className="font-mono">{bondDecimalText(result.remainingSignedBalance)}</dd></div><div><dt className="text-xs text-muted-foreground">Remaining receivable</dt><dd className="font-mono">{bondDecimalText(result.remainingReceivable)}</dd></div><div><dt className="text-xs text-muted-foreground">Remaining payable</dt><dd className="font-mono">{bondDecimalText(result.remainingPayable)}</dd></div></dl>
      {request?.reversesReceiptId ? <StatusBanner tone="info" title="Posted coupon reversal preview" detail={`Receipt ${request.reversesReceiptId} · ${request.correctionRationale}. Preparation still requires governed review.`} /> : null}
      <p className="break-all text-xs text-muted-foreground">{result.instruction.modelVersion} · {result.instruction.scope.baseCurrency} · Run {request?.runId} · Canonical event {result.instruction.eventId}</p>
      <div className="overflow-x-auto"><table className="w-full text-left text-xs"><caption className="mb-2 text-left font-medium">Proposed coupon accounting legs · pending review</caption><thead><tr><th className="p-2">Account</th><th className="p-2">Debit</th><th className="p-2">Credit</th><th className="p-2">Currency</th></tr></thead><tbody>{result.instruction.lines.map((line, index) => <tr className="border-t border-border" key={index}><th className="p-2 font-normal">{line.accountId}</th><td className="p-2">{bondDecimalText(line.debit)}</td><td className="p-2">{bondDecimalText(line.credit)}</td><td className="p-2">{line.currency}</td></tr>)}</tbody></table></div>
      {result.hasChanges ? <><Checkbox checked={reviewed} disabled={locked} onChange={(event) => setReviewed(event.target.checked)} label="I reviewed the coupon accrual, signed balance and retained cash allocations." /><Button type="button" disabled={busy !== null || !reviewed || prepared !== null} onClick={() => void prepareForReview()}>{busy === "prepare" ? "Preparing governed source…" : uncertain ? "Retry same coupon preparation" : "Prepare for review"}</Button></> : <p role="status" className="text-sm">No new earned or collected coupon amounts require preparation.</p>}
      {uncertain ? <p role="status" className="text-sm text-warning">The preparation outcome is uncertain. Retain this run and fingerprint and retry the same reviewed request.</p> : null}
      <details><summary className="cursor-pointer text-xs">Reviewed coupon fingerprint</summary><p className="break-all font-mono text-xs">{result.fingerprint}</p></details>
    </section> : null}
    {prepared ? <StatusBanner role="status" tone="success" title="Coupon Income source prepared for governed review" detail={<>Canonical event {prepared.projection.spine.eventId}. Continue through the existing accounting review workflow for drafting, independent approval and posting. No journal was posted and no payment was created.</>} /> : null}
  </div></details>;
}
