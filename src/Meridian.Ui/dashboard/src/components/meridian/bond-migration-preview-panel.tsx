import { type FormEvent, useEffect, useId, useRef, useState } from "react";
import { DenseDataTable, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { FormGrid, FormRow } from "@/components/ui/form";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import { StatusBanner } from "@/components/ui/status-banner";
import { TextArea } from "@/components/ui/text-area";
import { describeApiError, isApiError } from "@/lib/api-errors";
import { previewBondMigration, stageBondMigration } from "@/lib/api/bond-migration.api";
import type { SecurityMasterEditResult } from "@/lib/api/security-master-workbench.api";
import type { BondMigrationPreview, BondMigrationPreviewRequest } from "@/types/bond-migration.types";

type CashDifference = BondMigrationPreview["cashDifferences"][number];
const differenceColumns: DenseDataTableColumn<CashDifference>[] = [
  { id: "date", label: "Payment date", render: (row) => row.paymentDate },
  { id: "old-principal", label: "Prior principal", align: "right", render: (row) => String(row.previousPrincipal) },
  { id: "new-principal", label: "Candidate principal", align: "right", render: (row) => String(row.candidatePrincipal) },
  { id: "old-interest", label: "Prior interest", align: "right", render: (row) => String(row.previousInterest) },
  { id: "new-interest", label: "Candidate interest", align: "right", render: (row) => String(row.candidateInterest) }
];

export function BondMigrationPreviewPanel({ securityId, expectedVersion, preview = previewBondMigration, stage = stageBondMigration }: {
  securityId: string;
  expectedVersion: number | null;
  preview?: typeof previewBondMigration;
  stage?: typeof stageBondMigration;
}) {
  const id = useId();
  const [asOf, setAsOf] = useState(() => new Date().toISOString().slice(0, 10));
  const [unit, setUnit] = useState<"" | "Percent" | "Fraction">("");
  const [basis, setBasis] = useState<"Level" | "DayCount">("DayCount");
  const [convention, setConvention] = useState("Unadjusted");
  const [calendars, setCalendars] = useState("");
  const [adjustAccrual, setAdjustAccrual] = useState(false);
  const [endOfMonth, setEndOfMonth] = useState(false);
  const [pending, setPending] = useState(false);
  const [result, setResult] = useState<BondMigrationPreview | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [justification, setJustification] = useState("");
  const [staging, setStaging] = useState(false);
  const [staged, setStaged] = useState<SecurityMasterEditResult | null>(null);
  const [refreshRequired, setRefreshRequired] = useState(false);
  const controller = useRef<AbortController | null>(null);
  useEffect(() => {
    setResult(null); setError(null); setPending(false); setJustification(""); setStaging(false); setStaged(null); setRefreshRequired(false);
    return () => controller.current?.abort();
  }, [securityId, expectedVersion]);

  function edit(change: () => void) {
    controller.current?.abort(); setPending(false); setResult(null); setError(null); setJustification(""); setRefreshRequired(false); change();
  }

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (!unit || !asOf || expectedVersion == null) { setError("Choose the retained coupon unit and load the current terms version before previewing."); return; }
    const calendarIds = calendars.split(",").map((name) => name.trim()).filter(Boolean);
    if (convention !== "Unadjusted" && !calendarIds.length) { setError("Name the retained payment calendar for the selected business-day adjustment."); return; }
    controller.current?.abort();
    const requestController = new AbortController(); controller.current = requestController;
    setPending(true); setResult(null); setError(null); setJustification(""); setRefreshRequired(false);
    const request: BondMigrationPreviewRequest = {
      securityId, expectedVersion,
      context: { asOf: `${asOf}T00:00:00Z`, settlementDate: asOf, securityVersion: expectedVersion },
      scheduleConventions: { couponRateUnit: unit, couponCashBasis: basis, accrualAdjusted: adjustAccrual, businessDayConvention: convention, endOfMonthRule: endOfMonth, ...(calendarIds.length ? { paymentCalendars: calendarIds } : {}) }
    };
    try {
      const response = await preview(request, { signal: requestController.signal, allowDevelopmentFallback: false });
      if (requestController.signal.aborted) return;
      if (response.request.securityId.toLowerCase() !== securityId.toLowerCase() || response.request.expectedVersion !== expectedVersion) throw new Error("The preview does not match the selected retained terms version. Reload the security.");
      setResult(response);
    } catch (failure) {
      if (!requestController.signal.aborted) setError(describeApiError(failure, "Migration preview could not be prepared.").summary);
    } finally { if (!requestController.signal.aborted) setPending(false); }
  }

  async function stageReviewedPreview(event: FormEvent) {
    event.preventDefault();
    if (!result?.canStage || !justification.trim() || staging || staged || refreshRequired) return;
    const requestController = new AbortController(); controller.current = requestController;
    setStaging(true); setError(null);
    try {
      const response = await stage({ request: result.request, previewSha256: result.previewSha256, securityContentSha256: result.securityContentSha256, justification: justification.trim() }, { signal: requestController.signal, allowDevelopmentFallback: false });
      if (requestController.signal.aborted) return;
      if (response.securityId.toLowerCase() !== securityId.toLowerCase()) throw new Error("The draft response does not match this security. Inspect retained revision history before retrying.");
      setStaged(response);
    } catch (failure) {
      if (!requestController.signal.aborted) {
        const uncertain = !isApiError(failure) || failure.status >= 500;
        const stale = isApiError(failure) && failure.status === 409;
        setRefreshRequired(uncertain || stale);
        setError(stale ? "Terms or preview inputs changed. Reload the selected security and prepare a fresh preview before staging." : uncertain ? "The draft outcome is uncertain. Inspect retained revision history and reload the security before trying again." : describeApiError(failure, "The governed draft could not be created.").summary);
      }
    } finally { if (!requestController.signal.aborted) setStaging(false); }
  }

  return <details className="rounded-md border border-border p-3">
    <summary className="cursor-pointer text-sm font-semibold">Review bond convention migration</summary>
    <div className="mt-3 flex flex-col gap-3">
      <p className="text-xs text-muted-foreground">Compare legacy and candidate payments before proposing a governed terms change. This preview does not change terms, posted journals, or published reports. Terms version: {expectedVersion ?? "Unavailable"}.</p>
      <form onSubmit={submit} aria-label="Bond migration preview inputs"><fieldset disabled={pending || staging || staged != null || expectedVersion == null} className="flex min-w-0 flex-col gap-3"><legend className="sr-only">Explicit candidate conventions</legend><FormGrid columns={3}>
        <FormRow label="Preview date (UTC)" labelFor={`${id}-date`}><Input id={`${id}-date`} type="date" required value={asOf} onChange={(e) => edit(() => setAsOf(e.target.value))} /></FormRow>
        <FormRow label="Retained coupon unit" labelFor={`${id}-unit`}><Select id={`${id}-unit`} required value={unit} onChange={(e) => edit(() => setUnit(e.target.value as typeof unit))}><option value="">Choose from source evidence</option><option value="Percent">Percent (5 means 5%)</option><option value="Fraction">Fraction (0.05 means 5%)</option></Select></FormRow>
        <FormRow label="Coupon cash basis" labelFor={`${id}-basis`}><Select id={`${id}-basis`} value={basis} onChange={(e) => edit(() => setBasis(e.target.value as typeof basis))}><option value="DayCount">Day-count accrual</option><option value="Level">Level regular coupon</option></Select></FormRow>
        <FormRow label="Business-day adjustment" labelFor={`${id}-adjustment`}><Select id={`${id}-adjustment`} value={convention} onChange={(e) => edit(() => setConvention(e.target.value))}><option value="Unadjusted">Unadjusted</option><option value="Following">Following</option><option value="ModifiedFollowing">Modified following</option><option value="Preceding">Preceding</option><option value="ModifiedPreceding">Modified preceding</option></Select></FormRow>
        <FormRow label="Retained payment calendar IDs" labelFor={`${id}-calendars`} hint="Comma-separated IDs; calendar availability is checked by the server."><Input id={`${id}-calendars`} value={calendars} onChange={(e) => edit(() => setCalendars(e.target.value))} /></FormRow>
        <FormRow><div className="flex flex-col gap-2"><Checkbox label="Adjust accrual dates" checked={adjustAccrual} onChange={(e) => edit(() => setAdjustAccrual(e.target.checked))} /><Checkbox label="Keep end-of-month anchor" checked={endOfMonth} onChange={(e) => edit(() => setEndOfMonth(e.target.checked))} /></div></FormRow>
      </FormGrid><Button type="submit" variant="outline" className="w-fit">{pending ? "Preparing preview…" : "Preview convention migration"}</Button></fieldset></form>
      {expectedVersion == null ? <p role="status" className="text-xs text-warning">Load the selected security's retained version to enable migration preview.</p> : null}
      {error ? <StatusBanner tone="danger" role="alert" title="Preview unavailable" detail={error} /> : null}
      {result ? <><StatusBanner tone={result.canStage ? "info" : "warning"} title={result.canStage ? "Preview ready for governed review" : "Migration has unresolved gaps"} detail={result.canStage ? "Review the changed payments and fingerprint before creating a terms revision through the existing Security Master workflow." : result.gaps.join(" ")} /><DenseDataTable columns={differenceColumns} rows={result.cashDifferences} getRowId={(row) => row.paymentDate} ariaLabel="Bond migration payment comparison" caption="Prior and candidate economic payments, without changing ledger facts" emptyText="No payment differences were returned." /><details><summary className="cursor-pointer text-xs font-semibold">Reviewed input fingerprints</summary><p className="mt-2 break-all font-mono text-xs">Security SHA-256: {result.securityContentSha256}</p><p className="mt-2 break-all font-mono text-xs">Preview SHA-256: {result.previewSha256}</p></details>
        {result.canStage && !staged ? <form onSubmit={stageReviewedPreview} aria-label="Stage reviewed bond migration"><fieldset disabled={staging || refreshRequired} className="flex min-w-0 flex-col gap-3"><FormRow label="Review justification" labelFor={`${id}-justification`} hint="Explain the source evidence and review of the payment differences. This creates a draft only."><TextArea id={`${id}-justification`} required maxLength={2000} value={justification} onChange={(e) => setJustification(e.target.value)} /></FormRow><Button type="submit" className="w-fit" disabled={!justification.trim()}>{staging ? "Creating governed draft…" : "Stage governed draft"}</Button></fieldset></form> : null}
      </> : null}
      {staged ? <StatusBanner role="status" tone="success" title="Governed revision created" detail={`Revision ${staged.revisionId} · state ${staged.state} · terms version ${staged.newVersion}. No approval or publication was requested. Continue through independent review in the Security Master workflow and reload this security before preparing another migration.`} /> : null}
    </div>
  </details>;
}
