import { useEffect, useRef, useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import { StatusBanner } from "@/components/ui/status-banner";
import { acceptOptionAssessment, listOptionAssessments, optionAssessmentSourceUrl, previewOptionAssessment } from "@/lib/api/option-visualization.api";
import { describeApiError, isApiError } from "@/lib/api-errors";
import { comparableBasket, emptyAssessment, OPTION_LEGS_CSV_HEADER } from "@/screens/options-screen.view-model";
import type { BrokerOptionAssessment, OptionAssessmentBasket, OptionAssessmentImportPreview, OptionAssessmentImportRequest, RetainedOptionAssessment } from "@/types/option-visualization.types";

const amountFields = ["initialMarginBefore", "initialMarginChange", "initialMarginAfter", "maintenanceMarginBefore", "maintenanceMarginChange", "maintenanceMarginAfter", "excessLiquidityAfter", "cashImpact"] as const;
const label = (value: string) => value.replace(/([A-Z])/g, " $1").replace(/^./, first => first.toUpperCase());
const areaClass = "min-h-24 w-full rounded-sm border border-border bg-background p-2 font-mono text-xs focus-visible:outline-2 focus-visible:outline-primary";
type AcceptCommand = Parameters<typeof acceptOptionAssessment>[1];

export function OptionAssessmentPanel({ account, basket, basketKey, onUseBasket }: {
  account: string; basket: OptionAssessmentBasket; basketKey: string; onUseBasket: (basket: OptionAssessmentBasket) => void
}) {
  const [form, setForm] = useState(() => emptyAssessment(account, basket));
  const [mode, setMode] = useState<"form" | "json">("form"); const [jsonText, setJsonText] = useState("");
  const [legsCsv, setLegsCsv] = useState(""); const [correction, setCorrection] = useState("");
  const [source, setSource] = useState<{ name: string; base64: string; type: string } | null>(null);
  const [preview, setPreview] = useState<OptionAssessmentImportPreview | null>(null);
  const [previewRequest, setPreviewRequest] = useState<OptionAssessmentImportRequest | null>(null);
  const [command, setCommand] = useState<AcceptCommand | null>(null);
  const [uncertain, setUncertain] = useState(false);
  const [rows, setRows] = useState<RetainedOptionAssessment[]>([]); const [loaded, setLoaded] = useState(false);
  const [error, setError] = useState<string | null>(null); const [notice, setNotice] = useState("");
  const [busy, setBusy] = useState<"history" | "preview" | "accept" | "source" | null>(null);
  const request = useRef<AbortController | null>(null); const revision = useRef(0); const mounted = useRef(true);
  const sourceInput = useRef<HTMLInputElement | null>(null);
  const validAccount = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(account);

  useEffect(() => { mounted.current = true; return () => { mounted.current = false; request.current?.abort(); }; }, []);
  useEffect(() => {
    // Exact pending acceptance is retained for retry; basket edits never alter that command.
    revision.current++; request.current?.abort(); setPreview(null); setPreviewRequest(null);
    if (!command) setBusy(null);
  }, [basketKey, command]);

  function edit(change: () => void) {
    if (command) return;
    revision.current++; request.current?.abort(); setPreview(null); setPreviewRequest(null); setError(null); setNotice(""); setBusy(null); change();
  }
  function update<K extends keyof BrokerOptionAssessment>(key: K, value: BrokerOptionAssessment[K]) {
    edit(() => setForm(previous => ({ ...previous, [key]: value })));
  }
  async function loadHistory() {
    request.current?.abort(); const controller = new AbortController(); request.current = controller;
    const current = ++revision.current; setBusy("history"); setError(null); setLoaded(false); setRows([]);
    try {
      const records = await listOptionAssessments(account, { signal: controller.signal });
      if (!controller.signal.aborted && current === revision.current) { setRows(records); setLoaded(true); }
    } catch (failure) { if (!controller.signal.aborted && current === revision.current) setError(describeApiError(failure, "Broker assessment history unavailable.").summary); }
    finally { if (!controller.signal.aborted && current === revision.current) setBusy(null); }
  }
  async function readSource(file: File | undefined) {
    if (!file) return;
    edit(() => setSource(null));
    const current = revision.current; setBusy("source");
    try {
      if (file.size < 1 || file.size > 8 * 1024 * 1024) throw new Error("Attach a broker source file of 1 byte to 8 MiB.");
      const base64 = await new Promise<string>((resolve, reject) => {
        const reader = new FileReader(); reader.onload = () => resolve(String(reader.result).split(",", 2)[1]);
        reader.onerror = () => reject(new Error("Source file could not be read.")); reader.readAsDataURL(file);
      });
      if (mounted.current && current === revision.current) setSource({ name: file.name, base64, type: file.type || "application/octet-stream" });
    } catch (failure) { if (mounted.current && current === revision.current) setError(String(failure)); }
    finally { if (mounted.current && current === revision.current) setBusy(null); }
  }
  async function makePreview() {
    request.current?.abort(); const controller = new AbortController(); request.current = controller;
    const current = ++revision.current; setBusy("preview"); setError(null); setPreview(null); setPreviewRequest(null);
    try {
      if (!source) throw new Error("Attach the original broker assessment document before previewing.");
      const assessment = mode === "json" ? JSON.parse(jsonText) as BrokerOptionAssessment : { ...form, fundAccountId: account, basket };
      const payload: OptionAssessmentImportRequest = { assessment, sourceFileName: source.name,
        sourceContentBase64: source.base64, sourceContentType: source.type, legsCsv: legsCsv.trim() || null, correctsAssessmentId: correction || null };
      const result = await previewOptionAssessment(account, payload, { signal: controller.signal });
      if (controller.signal.aborted || current !== revision.current) return;
      setPreview(result); setPreviewRequest(payload);
    } catch (failure) { if (!controller.signal.aborted && current === revision.current) setError(describeApiError(failure, "Assessment import preview failed.").summary); }
    finally { if (!controller.signal.aborted && current === revision.current) setBusy(null); }
  }
  async function accept() {
    const exact = command ?? (preview && previewRequest ? { idempotencyKey: crypto.randomUUID(), previewFingerprint: preview.previewFingerprint, import: previewRequest } : null);
    if (!exact) return;
    const hadUncertainOutcome = uncertain;
    // Deliberately do not abort acceptance on basket edits; uncertain outcomes keep this exact request.
    setCommand(exact); setBusy("accept"); setError(null); setUncertain(false);
    try {
      const retained = await acceptOptionAssessment(account, exact);
      if (!mounted.current) return;
      setRows(previous => [retained, ...previous.filter(row => row.assessmentId !== retained.assessmentId)
        .map(row => row.assessmentId === retained.correctsAssessmentId ? { ...row, isSuperseded: true } : row)]);
      setNotice(`Retained broker assessment ${retained.assessmentId}. Its dated claim does not establish current acceptance. Load history to verify the complete retained account history.`);
      setCommand(null); setPreview(null); setPreviewRequest(null); setSource(null); setCorrection("");
      if (sourceInput.current) sourceInput.current.value = "";
    } catch (failure) {
      if (!mounted.current) return;
      // A rejected retry cannot establish whether an earlier dispatch committed.
      const rejected = !hadUncertainOutcome && isApiError(failure) && failure.status >= 400 && failure.status < 500 && failure.status !== 408 && failure.status !== 429;
      setUncertain(!rejected);
      if (rejected) { setCommand(null); setPreview(null); setPreviewRequest(null); }
      setError(describeApiError(failure, rejected ? "The import was rejected. Correct the inputs and preview again." : "Import outcome is uncertain. Retry this same reviewed command or inspect history.").summary);
    } finally { if (mounted.current) setBusy(null); }
  }
  const matches = rows.filter(row => !row.isSuperseded && comparableBasket(row.assessment.basket) === basketKey
    && row.assessment.providerId === form.providerId.trim() && row.assessment.externalAccountId === form.externalAccountId.trim());

  return <section aria-label="Imported option assessments" className="grid gap-4">
    <div className="flex flex-wrap items-center gap-3"><Button variant="outline" disabled={!validAccount || busy !== null} onClick={() => void loadHistory()}>Load assessment history</Button><span className="text-xs">{loaded ? `${rows.length} retained assessment(s).` : "History has not been verified for this account."}</span></div>
    {error ? <StatusBanner role="alert" tone="danger" title={uncertain ? "Import requires recovery" : "Assessment unavailable"} detail={error} /> : null}
    {notice ? <p role="status" className="text-xs">{notice}</p> : null}
    {uncertain ? <div className="grid gap-2"><p className="text-xs">The exact reviewed import and retry key remain in this view. Retrying cannot create a duplicate. Keep this view open until the outcome is resolved.</p><Button className="w-fit" disabled={busy !== null} onClick={() => void accept()}>Retry exact import</Button></div> : null}
    <details open><summary className="cursor-pointer text-sm font-semibold">Import broker assessment</summary>
      <p className="my-2 text-xs text-muted-foreground">Enter a transcription with its source, paste canonical schema-version-1 JSON, or replace the legs with the documented CSV. Source documents are retained as evidence; no PDF interpretation or broker connection is performed.</p>
      <fieldset disabled={command !== null || busy === "accept"} className="grid gap-3"><legend className="sr-only">Broker assessment import inputs</legend>
        <label className="grid gap-1 text-xs">Entry method<Select aria-label="Entry method" value={mode} onChange={event => edit(() => setMode(event.target.value as "form" | "json"))}><option value="form">Evidence-backed form</option><option value="json">Canonical JSON</option></Select></label>
        {mode === "form" ? <>
          <div className="grid gap-3 md:grid-cols-3">
            <label className="grid gap-1 text-xs">Broker provider ID<Input value={form.providerId} onChange={event => update("providerId", event.target.value)} /></label>
            <label className="grid gap-1 text-xs">External broker account<Input value={form.externalAccountId} onChange={event => update("externalAccountId", event.target.value)} /></label>
            <label className="grid gap-1 text-xs">Broker assessed at (UTC)<Input placeholder="2026-11-12T15:30:00Z" value={form.assessedAt} onChange={event => update("assessedAt", event.target.value)} /></label>
            <label className="grid gap-1 text-xs">Broker assessment reference<Input value={form.brokerReference} onChange={event => update("brokerReference", event.target.value)} /></label>
            <label className="grid gap-1 text-xs">Reported outcome<Select aria-label="Reported outcome" value={form.outcome} onChange={event => update("outcome", event.target.value as BrokerOptionAssessment["outcome"])}><option>NotReported</option><option>Approved</option><option>Conditional</option><option>Rejected</option></Select></label>
            <label className="grid gap-1 text-xs">Original broker status text<Input value={form.rawBrokerStatus} onChange={event => update("rawBrokerStatus", event.target.value)} /></label>
            <label className="grid gap-1 text-xs">Reported margin currency<Input value={form.marginCurrency ?? ""} onChange={event => update("marginCurrency", event.target.value || null)} placeholder="Required when margin figures are reported" /></label>
            <label className="grid gap-1 text-xs">Reported cash-impact currency<Input value={form.cashImpactCurrency ?? ""} onChange={event => update("cashImpactCurrency", event.target.value || null)} placeholder="Required when cash impact is reported" /></label>
            {amountFields.map(field => <label key={field} className="grid gap-1 text-xs">{label(field)} ({(field === "cashImpact" ? form.cashImpactCurrency : form.marginCurrency) || "currency unspecified"})<Input inputMode="decimal" placeholder="Not reported" value={form[field] ?? ""} onChange={event => update(field, event.target.value || null)} /></label>)}
          </div>
          <label className="grid gap-1 text-xs">Dated account permission evidence<textarea className={areaClass} value={form.permissionEvidence ?? ""} onChange={event => update("permissionEvidence", event.target.value || null)} placeholder="Copy broker account approval/permission statements; leave blank if not reported" /></label>
          <label className="grid gap-1 text-xs">Dated restrictions and collateral evidence<textarea className={areaClass} value={form.restrictionEvidence ?? ""} onChange={event => update("restrictionEvidence", event.target.value || null)} placeholder="Record broker conditions, restrictions and collateral observations separately" /></label>
          <label className="grid gap-1 text-xs">Broker warnings (one per line)<textarea className={areaClass} value={form.warnings.join("\n")} onChange={event => update("warnings", event.target.value.split("\n").filter(Boolean))} /></label>
        </> : <label className="grid gap-1 text-xs">Canonical broker assessment JSON<textarea className={`${areaClass} min-h-64`} value={jsonText} onChange={event => edit(() => setJsonText(event.target.value))} /><Button type="button" variant="outline" className="w-fit" onClick={() => edit(() => setJsonText(JSON.stringify({ ...form, fundAccountId: account, basket }, null, 2)))}>Fill schema from form and basket</Button></label>}
        <details><summary className="cursor-pointer text-xs">Optional CSV leg replacement</summary><p className="my-2 break-all font-mono text-xs">{OPTION_LEGS_CSV_HEADER}</p><label className="grid gap-1 text-xs">Basket legs CSV<textarea className={areaClass} value={legsCsv} onChange={event => edit(() => setLegsCsv(event.target.value))} placeholder="Header followed by one row per leg; quote fields containing commas" /></label></details>
        <label className="grid gap-1 text-xs">Original broker source document (maximum 8 MiB)<Input ref={sourceInput} type="file" onChange={event => void readSource(event.target.files?.[0])} /><span>{source ? `${source.name} attached` : "No source attached"}</span></label>
        <label className="grid gap-1 text-xs">Corrects retained assessment<Select aria-label="Corrects retained assessment" value={correction} onChange={event => edit(() => setCorrection(event.target.value))}><option value="">New assessment</option>{rows.filter(row => !row.isSuperseded).map(row => <option key={row.assessmentId} value={row.assessmentId}>{row.assessmentId} · {row.assessment.assessedAt}</option>)}</Select></label>
        <Button className="w-fit" disabled={!validAccount || !source || busy !== null} onClick={() => void makePreview()}>{busy === "preview" ? "Validating…" : "Preview evidence import"}</Button>
      </fieldset>
      {preview ? <section className="mt-3 grid gap-2 border border-border p-3" aria-label="Reviewed broker assessment preview"><h4 className="text-sm font-semibold">{preview.assessment.outcome} · broker assessed {preview.assessment.assessedAt}</h4><p className="text-xs">{preview.assessment.basket.legs.length} legs · basket quantity {preview.assessment.basket.quantity} · {preview.assessment.basket.priceEffect} {preview.assessment.basket.limitPrice ?? "market"} {preview.assessment.basket.currency}</p><p className="break-all font-mono text-xs">Source SHA-256 {preview.sourceSha256}</p><details><summary className="cursor-pointer text-xs">Inspect exact normalized assessment and legs</summary><pre className="overflow-auto whitespace-pre-wrap break-all text-xs">{JSON.stringify(preview.assessment, null, 2)}</pre></details>{preview.warnings.map(warning => <p key={warning} className="text-xs">{warning}</p>)}<Button className="w-fit" disabled={busy !== null || command !== null} onClick={() => void accept()}>Accept reviewed evidence import</Button></section> : null}
    </details>
    {loaded ? <section aria-label="Retained broker assessment history" className="grid gap-3"><h3 className="text-sm font-semibold">Retained assessments</h3><p className="text-xs">{matches.length ? `${matches.length} matching imported assessment(s) for the basket and broker/account text above.` : "No matching imported assessment for the exact proposed basket and broker/account text above."} A historical match establishes no present broker acceptance.</p>
      {rows.map(row => <article key={row.assessmentId} className="grid gap-2 border border-border p-3"><h4 className="text-sm font-semibold">{row.assessment.outcome} as of {row.assessment.assessedAt}{row.isSuperseded ? " · Superseded" : ""}</h4><p className="text-xs">{row.assessment.providerId} · {row.assessment.externalAccountId} · {row.assessment.rawBrokerStatus}</p><p className="text-xs">Recorded {row.recordedAt} by {row.recordedBy}. {row.correctsAssessmentId ? `Corrects ${row.correctsAssessmentId}.` : "Original assessment."}</p><dl className="grid gap-2 text-xs md:grid-cols-3">{amountFields.map(field => <div key={field}><dt className="text-muted-foreground">{label(field)}</dt><dd className="font-mono">{row.assessment[field] ?? "Not reported"} {row.assessment[field] !== null ? (field === "cashImpact" ? row.assessment.cashImpactCurrency : row.assessment.marginCurrency) ?? "Currency unavailable" : ""}</dd></div>)}</dl><p className="text-xs">Permission evidence: {row.assessment.permissionEvidence ?? "Not reported"}</p><p className="text-xs">Restrictions / collateral evidence: {row.assessment.restrictionEvidence ?? "Not reported"}</p>{row.assessment.warnings.map(warning => <p key={warning} className="text-xs">{warning}</p>)}<details><summary className="cursor-pointer text-xs">Basket, identities and provenance</summary><pre className="overflow-auto whitespace-pre-wrap break-all text-xs">{JSON.stringify(row, null, 2)}</pre></details><div className="flex flex-wrap gap-3"><Button variant="outline" className="w-fit" disabled={command !== null} onClick={() => { onUseBasket(row.assessment.basket); setForm({ ...row.assessment }); }}>Use exact assessed basket</Button><a className="text-xs underline" href={optionAssessmentSourceUrl(account, row.assessmentId)}>Download retained broker source</a></div></article>)}
      {!rows.length ? <p className="text-xs">No broker assessments are retained for this account.</p> : null}
    </section> : null}
  </section>;
}
