import { type FormEvent, useEffect, useId, useMemo, useRef, useState } from "react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import { StatusBanner } from "@/components/ui/status-banner";
import { privateCapitalApi } from "@/lib/api/private-capital-records.api";
import { ApiError } from "@/lib/api-errors";
import { formatTaxDecimal, isTaxDecimalText } from "@/lib/ledger-tax-results-format";
import { buildCapitalRows, capitalBarWidth, capitalMeasures } from "./private-capital-lifecycle.view-model";
import type { PrivateCapitalChoices, PrivateCapitalEvidence, PrivateCapitalPreview, PrivateCapitalRecords,
  PrivateCapitalSnapshotInput } from "@/types/private-capital.types";

const emptyEvidence: PrivateCapitalEvidence = { vaultId: "", documentId: "", contentHash: "" };
const initialRow = (): PrivateCapitalSnapshotInput => ({ commitmentId: "", asOfDate: "", navValuationDate: null,
  commitmentAmount: null, calledAmount: null, unfundedAmount: null, cumulativeDistributions: null,
  navAmount: null, sourceReference: "", supersedesSnapshotId: null });
const textAmount = (value: string | null | undefined) => value == null ? "Unavailable" :
  isTaxDecimalText(value) ? formatTaxDecimal(value) : "Invalid source amount";
const csvHeaders = "commitment_id,as_of_date,nav_valuation_date,commitment_amount,called_amount,unfunded_amount,cumulative_distributions,nav_amount,source_reference,supersedes_snapshot_id";

export interface PrivateCapitalLifecyclePanelProps {
  holderEntityIds?: readonly string[];
  scopeKey?: string;
  asOfDate?: string;
  api?: typeof privateCapitalApi;
  onViewStateChange?: (patch: Record<string, string | null>) => void;
}

export function PrivateCapitalLifecyclePanel({ holderEntityIds, scopeKey = "", asOfDate,
  api = privateCapitalApi, onViewStateChange }: PrivateCapitalLifecyclePanelProps) {
  const id = useId();
  const membership = holderEntityIds ? [...holderEntityIds].sort().join("|") : "*";
  const epoch = useRef(0);
  const [data, setData] = useState<PrivateCapitalRecords | null>(null);
  const [choices, setChoices] = useState<PrivateCapitalChoices>({ entities: [], vehicles: [] });
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [refresh, setRefresh] = useState(0);
  const [group, setGroup] = useState<"vintage" | "strategy">(() =>
    new URLSearchParams(window.location.search).get("pcGroup") === "strategy" ? "strategy" : "vintage");
  const [selected, setSelected] = useState(() => new URLSearchParams(window.location.search).get("pcCommitment") || "");
  const [evidence, setEvidence] = useState<PrivateCapitalEvidence>(emptyEvidence);
  const [holder, setHolder] = useState("");
  const [vehicle, setVehicle] = useState("");
  const [currency, setCurrency] = useState("");
  const [source, setSource] = useState("");
  const [mode, setMode] = useState<"manual" | "csv">("manual");
  const [row, setRow] = useState(initialRow);
  const [csv, setCsv] = useState("");
  const [rationale, setRationale] = useState("");
  const [preview, setPreview] = useState<PrivateCapitalPreview | null>(null);
  const [confirmed, setConfirmed] = useState(false);
  const acceptRequest = useRef<{ batchId: string; fingerprint: string; commandId: string } | null>(null);
  const registerRequest = useRef<Parameters<typeof api.register>[0] | null>(null);
  const [uncertain, setUncertain] = useState<"accept" | "register" | null>(null);

  useEffect(() => {
    const revision = ++epoch.current;
    const controller = new AbortController();
    setData(null); setLoading(true); setError(""); setNotice(""); setBusy(false);
    setChoices({ entities: [], vehicles: [] }); setPreview(null); setConfirmed(false);
    setEvidence(emptyEvidence); setRow(initialRow()); setHolder(""); setVehicle("");
    setCurrency(""); setSource(""); setCsv(""); setRationale(""); setUncertain(null);
    acceptRequest.current = null; registerRequest.current = null;
    void api.records({ signal: controller.signal }).then(async records => {
      if (epoch.current !== revision || controller.signal.aborted) return;
      setData(records); setLoading(false);
      if (records.canManage) {
        const available = await api.choices({ signal: controller.signal });
        if (epoch.current === revision && !controller.signal.aborted) setChoices(available);
      }
    }).catch(reason => {
      if (epoch.current !== revision || controller.signal.aborted) return;
      setError(reason instanceof Error ? reason.message : "Private-capital records could not be loaded."); setLoading(false);
    });
    return () => { controller.abort(); epoch.current = revision + 1; };
  }, [api, scopeKey, membership, refresh]);

  useEffect(() => {
    const restore = () => {
      const params = new URLSearchParams(window.location.search);
      setGroup(params.get("pcGroup") === "strategy" ? "strategy" : "vintage");
      setSelected(params.get("pcCommitment") || "");
    };
    window.addEventListener("popstate", restore);
    return () => window.removeEventListener("popstate", restore);
  }, []);

  const setView = (key: string, value: string) => {
    if (onViewStateChange) { onViewStateChange({ [key]: value || null }); return; }
    const url = new URL(window.location.href);
    if (value) url.searchParams.set(key, value); else url.searchParams.delete(key);
    window.history.replaceState(window.history.state, "", `${url.pathname}${url.search}${url.hash}`);
  };
  const allowed = useMemo(() => membership === "*" ? null : new Set(membership.split("|")), [membership]);
  const commitments = useMemo(() => (data?.commitments ?? []).filter(item => !allowed || allowed.has(item.holderEntityId)), [data, allowed]);
  const rows = useMemo(() => buildCapitalRows(commitments, data?.snapshots ?? [], group, asOfDate), [commitments, data, group, asOfDate]);
  const chosen = rows.find(item => item.commitment.commitmentId === selected);
  const history = (data?.snapshots ?? []).filter(item => item.commitmentId === selected)
    .sort((a, b) => b.recordedAt.localeCompare(a.recordedAt));
  const writableHolders = choices.entities.filter(item => !allowed || allowed.has(item.id));
  const writable = data?.canManage === true && writableHolders.length > 0;
  const locked = busy || uncertain !== null;
  const invalidate = () => { setPreview(null); setConfirmed(false); acceptRequest.current = null; setNotice(""); };
  const fail = (reason: unknown, operation?: "accept" | "register") => {
    setError(reason instanceof Error ? reason.message : "The operation could not be completed.");
    // A later rejection cannot disprove a prior dispatch whose response was lost.
    if (operation && (uncertain !== null || !(reason instanceof ApiError && reason.status >= 400 && reason.status < 500))) setUncertain(operation);
    else if (operation === "accept") { acceptRequest.current = null; setPreview(null); setUncertain(null); }
    else if (operation === "register") { registerRequest.current = null; setUncertain(null); }
  };
  const register = async (event?: FormEvent) => {
    event?.preventDefault();
    if (!writable || busy) return;
    const request = registerRequest.current ?? { commandId: crypto.randomUUID(), holderEntityId: holder,
      vehicleSecurityId: vehicle, currency, sourceReference: source, evidence: { ...evidence } };
    registerRequest.current = request;
    const revision = epoch.current; setBusy(true); setError("");
    try {
      const created = await api.register(request);
      if (epoch.current !== revision) return;
      setData(current => current ? { ...current, commitments: [...current.commitments.filter(item => item.commitmentId !== created.commitmentId), created] } : current);
      setSelected(created.commitmentId); setView("pcCommitment", created.commitmentId);
      setRow(current => ({ ...current, commitmentId: created.commitmentId }));
      setNotice("Commitment registered. Add its source-reported observation below.");
      registerRequest.current = null; setUncertain(null);
    } catch (reason) { if (epoch.current === revision) fail(reason, "register"); }
    finally { if (epoch.current === revision) setBusy(false); }
  };
  const prepare = async (event: FormEvent) => {
    event.preventDefault(); if (!writable || locked) return;
    const revision = epoch.current; setBusy(true); setError(""); invalidate();
    try {
      const result = await api.preview({ rows: mode === "manual" ? [row] : null, csv: mode === "csv" ? csv : null, evidence, rationale });
      if (epoch.current === revision) setPreview(result);
    } catch (reason) { if (epoch.current === revision) fail(reason); }
    finally { if (epoch.current === revision) setBusy(false); }
  };
  const accept = async () => {
    if (!preview || !writable || busy || !confirmed || uncertain === "register") return;
    const request = acceptRequest.current ?? { batchId: preview.batchId, fingerprint: preview.fingerprint, commandId: crypto.randomUUID() };
    acceptRequest.current = request;
    const revision = epoch.current; setBusy(true); setError("");
    try {
      const receipt = await api.accept(request);
      if (epoch.current !== revision) return;
      setNotice(`Accepted ${receipt.snapshotIds.length} observations. Receipt ${receipt.commandId}.`);
      setUncertain(null); acceptRequest.current = null; setPreview(null); setConfirmed(false);
      try {
        const records = await api.records();
        if (epoch.current === revision) setData(records);
      } catch {
        if (epoch.current === revision) setError("Observations were accepted, but the refreshed read failed. Refresh the records to inspect the retained receipt.");
      }
    } catch (reason) { if (epoch.current === revision) fail(reason, "accept"); }
    finally { if (epoch.current === revision) setBusy(false); }
  };

  return <Card>
    <CardHeader><CardTitle>Private-capital lifecycle</CardTitle>
      <CardDescription>Source-reported commitment, capital activity, and remaining value. Currency groups use separate scales; these observations are excluded from consolidated totals until reconciled.</CardDescription>
    </CardHeader>
    <CardContent className="space-y-4">
      <div className="flex flex-wrap items-end gap-3">
        <label className="text-sm">Group vehicles by<Select value={group} onChange={event => {
          const value = event.target.value === "strategy" ? "strategy" : "vintage"; setGroup(value); setView("pcGroup", value);
        }}><option value="vintage">Vintage</option><option value="strategy">Strategy</option></Select></label>
        <Button variant="outline" size="sm" disabled={loading || locked} onClick={() => setRefresh(value => value + 1)}>Refresh private-capital records</Button>
        {data && <span className="text-xs text-muted-foreground">Read generated {data.generatedAt}</span>}
      </div>
      {loading && <p role="status">Loading retained private-capital records…</p>}
      {error && <StatusBanner role="alert" tone="danger" title={error} />}
      {notice && <p role="status" className="text-sm">{notice}</p>}
      {uncertain && <StatusBanner tone="warning" title="The write outcome is uncertain" detail="Retry the same retained request before starting another write. Inspect the register and source history after a reload." />}
      {!loading && data && rows.length === 0 && <p className="text-sm">No retained commitment register entries for the selected entities. Assignment references do not supply financial values.</p>}
      {[...new Set(rows.map(item => item.commitment.currency))].map(denomination => {
        const currencyRows = rows.filter(item => item.commitment.currency === denomination);
        const values = currencyRows.flatMap(item => capitalMeasures.map(([key]) => item.snapshot?.[key]));
        return <div key={denomination} className="overflow-x-auto">
          <table className="w-full text-sm" aria-label={`Private-capital lifecycle ${denomination}`}>
            <caption className="pb-2 text-left text-xs text-muted-foreground">{denomination} · source amounts, common magnitude scale within this currency. Called and unfunded are independent observations.</caption>
            <thead><tr className="border-b text-left"><th className="p-2">Vehicle / group</th>{capitalMeasures.map(([key, label]) => <th className="p-2 text-right" key={key}>{label}</th>)}</tr></thead>
            <tbody>{currencyRows.map(item => <tr key={item.commitment.commitmentId} className="border-b" aria-selected={selected === item.commitment.commitmentId}>
              <th scope="row" className="p-2 text-left font-normal"><button type="button" className="text-left underline underline-offset-4 focus-visible:outline-auto" onClick={() => { setSelected(item.commitment.commitmentId); setView("pcCommitment", item.commitment.commitmentId); }}>
                {item.commitment.vehicleName}</button><span className="block text-xs text-muted-foreground">{item.group} · {item.snapshot?.asOfDate ?? (item.conflicting ? "Conflicting source observations" : "Financial values unavailable")}</span></th>
              {capitalMeasures.map(([key]) => {
                const value = item.snapshot?.[key]; const width = capitalBarWidth(value, values) / 2;
                return <td key={key} className="min-w-28 p-2 text-right font-mono"><span>{textAmount(value)}</span>
                  {value != null && <svg viewBox="0 0 100 8" className="mt-1 h-2 w-full" aria-hidden="true"><line x1="50" x2="50" y1="0" y2="8" stroke="var(--chart-axis)" /><rect x={Number(value) < 0 ? 50 - width : 50} y="1" width={width} height="6" fill={Number(value) < 0 ? "var(--semantic-warning, #9a5d1a)" : "var(--chart-equity, #3a7a56)"} /></svg>}
                </td>;
              })}
            </tr>)}</tbody>
          </table>
        </div>;
      })}
      {selected && !chosen && !loading && <p role="status">The selected commitment is unavailable in this scope.</p>}
      {chosen && <section aria-label="Selected commitment evidence" className="space-y-2 border-t pt-3 text-sm">
        <h3 className="font-semibold">{chosen.commitment.vehicleName} — retained evidence</h3>
        <p>Commitment {chosen.commitment.commitmentId} · holder {chosen.commitment.holderEntityId} · currency {chosen.commitment.currency}</p>
        <p>Source {chosen.snapshot?.sourceReference ?? chosen.commitment.sourceReference} · financial as-of {chosen.snapshot?.asOfDate ?? "Unavailable"} · NAV valuation {chosen.snapshot?.navValuationDate ?? "Unavailable"}</p>
        <details><summary className="cursor-pointer">Observation history ({history.length})</summary>{history.map(item => <div key={item.snapshotId} className="my-2 border-l-2 pl-3">
          <p>{item.snapshotId} · as-of {item.asOfDate} · recorded {item.recordedAt} by {item.recordedBy}</p>
          <p>{capitalMeasures.map(([key, label]) => `${label}: ${textAmount(item[key])}`).join(" · ")}</p>
          <p>Evidence {item.evidence.documentId} · SHA-256 {item.evidence.contentHash}</p>
          {item.batchId && <a className="underline" href={`/api/private-capital/imports/${encodeURIComponent(item.batchId)}`} target="_blank" rel="noreferrer">Inspect original import and receipt</a>}
          {item.supersedesSnapshotId && <p>Corrects {item.supersedesSnapshotId}</p>}
        </div>)}</details>
      </section>}
      {data && !writable && <p className="text-xs text-muted-foreground">Private-capital record entry requires record-management permission for an entity in the selected scope.</p>}
      {writable && <details className="border-t pt-3"><summary className="cursor-pointer text-sm font-semibold">Register or import source observations</summary>
        <fieldset disabled={locked} className="mt-4 grid gap-3 sm:grid-cols-3"><legend className="text-sm font-semibold">Retained source evidence</legend>
          {(["vaultId", "documentId", "contentHash"] as const).map(key => <label key={key} className="text-sm">{key === "vaultId" ? "Evidence vault ID" : key === "documentId" ? "Evidence document ID" : "Evidence SHA-256"}
            <Input value={evidence[key]} onChange={event => { setEvidence(current => ({ ...current, [key]: event.target.value })); invalidate(); }} /></label>)}
        </fieldset>
        <p className="my-2 text-xs text-muted-foreground">Use an accessible retained Evidence Vault document; the server verifies its content and scope.</p>
        <form aria-label="Register private-capital commitment" onSubmit={event => void register(event)} className="space-y-3 border-t py-3">
          <fieldset disabled={locked} className="grid gap-3 sm:grid-cols-3"><legend className="text-sm font-semibold">Register a stable commitment identity</legend>
            <label className="text-sm">Holder entity<Select required value={holder} onChange={event => setHolder(event.target.value)}><option value="">Choose entity</option>{writableHolders.map(item => <option key={item.id} value={item.id}>{item.label}</option>)}</Select></label>
            <label className="text-sm">Private-fund vehicle<Select required value={vehicle} onChange={event => setVehicle(event.target.value)}><option value="">Choose vehicle</option>{choices.vehicles.map(item => <option key={item.id} value={item.id}>{item.label}</option>)}</Select></label>
            <label className="text-sm">Commitment currency<Input required value={currency} onChange={event => setCurrency(event.target.value)} /></label>
            <label className="text-sm sm:col-span-3">Register source reference<Input required value={source} onChange={event => setSource(event.target.value)} /></label>
          </fieldset>
          <Button type="submit" disabled={locked || !evidence.documentId || !evidence.contentHash || !evidence.vaultId}>Register commitment</Button>
          {uncertain === "register" && <Button type="button" disabled={busy} onClick={() => void register()}>Retry same registration</Button>}
        </form>
        <form aria-label="Preview private-capital observations" onSubmit={event => void prepare(event)} className="space-y-3 border-t pt-3">
          <fieldset disabled={locked} className="space-y-3"><legend className="text-sm font-semibold">Source-reported financial observations</legend>
            <label className="block text-sm">Entry method<Select value={mode} onChange={event => { setMode(event.target.value === "csv" ? "csv" : "manual"); invalidate(); }}><option value="manual">Manual row</option><option value="csv">Canonical CSV</option></Select></label>
            {mode === "manual" ? <div key="manual" className="grid gap-3 sm:grid-cols-3">
              <label className="text-sm">Registered commitment<Select required value={row.commitmentId} onChange={event => { setRow(current => ({ ...current, commitmentId: event.target.value })); invalidate(); }}><option value="">Choose commitment</option>{commitments.filter(item => writableHolders.some(holder => holder.id === item.holderEntityId)).map(item => <option key={item.commitmentId} value={item.commitmentId}>{item.vehicleName} · {item.commitmentId}</option>)}</Select></label>
              <label className="text-sm">Financial as-of date<Input type="date" required value={row.asOfDate} onChange={event => { setRow(current => ({ ...current, asOfDate: event.target.value })); invalidate(); }} /></label>
              <label className="text-sm">NAV valuation date<Input type="date" value={row.navValuationDate ?? ""} onChange={event => { setRow(current => ({ ...current, navValuationDate: event.target.value || null })); invalidate(); }} /></label>
              {capitalMeasures.map(([key, label]) => <label key={key} className="text-sm">{label}<Input inputMode="decimal" value={row[key] ?? ""} onChange={event => { setRow(current => ({ ...current, [key]: event.target.value || null })); invalidate(); }} /></label>)}
              <label className="text-sm">Observation source reference<Input required value={row.sourceReference} onChange={event => { setRow(current => ({ ...current, sourceReference: event.target.value })); invalidate(); }} /></label>
              <label className="text-sm sm:col-span-3">Snapshot being corrected (optional)<Input value={row.supersedesSnapshotId ?? ""} onChange={event => { setRow(current => ({ ...current, supersedesSnapshotId: event.target.value || null })); invalidate(); }} /></label>
            </div> : <div key="csv" className="space-y-2">
              <p className="break-all font-mono text-xs">{csvHeaders}</p>
              <label className="block text-sm">CSV file<Input type="file" accept=".csv,text/csv" onChange={event => {
                const file = event.target.files?.[0]; const revision = epoch.current; invalidate();
                if (file) void file.text().then(text => { if (epoch.current === revision) setCsv(text); }).catch(reason => { if (epoch.current === revision) fail(reason); });
              }} /></label>
              <label className="block text-sm">CSV content<textarea className="min-h-28 w-full border bg-background p-2 font-mono text-xs" value={csv} onChange={event => { setCsv(event.target.value); invalidate(); }} /></label>
            </div>}
            <label className="block text-sm">Review or correction rationale<Input required value={rationale} onChange={event => { setRationale(event.target.value); invalidate(); }} /></label>
          </fieldset>
          <p className="text-xs text-muted-foreground">Blank amounts remain unavailable. Distributions and NAV stay separate; no roll-forward equation is inferred.</p>
          <Button type="submit" disabled={locked || !evidence.documentId || !evidence.contentHash || !evidence.vaultId}>Preview source observations</Button>
        </form>
        {preview && <section aria-label="Private-capital import preview" className="mt-4 space-y-3 border-t pt-3">
          <h3 className="text-sm font-semibold">Review {preview.rows.length} source observations</h3>
          <div className="overflow-x-auto"><table className="w-full text-xs" aria-label="Reviewed private-capital observations"><thead><tr><th>Commitment / as-of</th>{capitalMeasures.map(([key, label]) => <th key={key}>{label}</th>)}</tr></thead>
            <tbody>{preview.rows.map((item, index) => <tr key={`${item.commitmentId}-${index}`}><th className="p-2 text-left font-normal">{item.commitmentId} · {item.asOfDate}{item.supersedesSnapshotId ? ` · corrects ${item.supersedesSnapshotId}` : ""}</th>{capitalMeasures.map(([key]) => <td className="p-2 text-right font-mono" key={key}>{textAmount(item[key])}</td>)}</tr>)}</tbody></table></div>
          <p className="break-all text-xs">Source document {preview.evidence.documentId} · fingerprint {preview.fingerprint}</p>
          <label htmlFor={`${id}-confirm`} className="flex gap-2 text-sm"><input id={`${id}-confirm`} type="checkbox" checked={confirmed} disabled={locked} onChange={event => setConfirmed(event.target.checked)} />I reviewed these source amounts and correction references.</label>
          <Button disabled={busy || !confirmed || uncertain === "register"} onClick={() => void accept()}>{uncertain === "accept" ? "Retry same acceptance" : "Accept reviewed observations"}</Button>
        </section>}
      </details>}
    </CardContent>
  </Card>;
}
