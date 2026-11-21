import { useEffect, useId, useRef, useState } from "react";
import { Button } from "@/components/ui/button";
import { FormGrid, FormRow } from "@/components/ui/form";
import { Input } from "@/components/ui/input";
import { StatusBanner } from "@/components/ui/status-banner";
import { getPricingHierarchy, savePricingHierarchy, type PricingHierarchyWrite, type PricingSourceHierarchy } from "@/lib/api/pricing-hierarchy.api";
import { describeApiError, isApiError } from "@/lib/api-errors";

interface Props {
  accountId: string;
  canRead: boolean;
  canModify: boolean;
  initialSecurityId?: string;
  onDirtyChange?: (dirty: boolean) => void;
}
interface SourceDraft { key: number; sourceId: string; name: string; days: string }
const securityPattern = /^(?!00000000-0000-0000-0000-000000000000$)[\da-f]{8}-[\da-f]{4}-[\da-f]{4}-[\da-f]{4}-[\da-f]{12}$/i;

/** Changes selection policy prospectively; retained price sets remain bound to their own hierarchy. */
export function PricingSourceHierarchyPanel(props: Props) {
  return <ScopedPricingHierarchy key={`${props.accountId}:${props.canRead}:${props.canModify}`} {...props} />;
}

function ScopedPricingHierarchy({ accountId, canRead, canModify, initialSecurityId = "", onDirtyChange }: Props) {
  const id = useId();
  const [securityId, setSecurityId] = useState(initialSecurityId);
  const [loadedId, setLoadedId] = useState("");
  const [retained, setRetained] = useState<PricingSourceHierarchy | null>(null);
  const [rows, setRows] = useState<SourceDraft[]>([]);
  const nextKey = useRef(1);
  const [effectiveAt, setEffectiveAt] = useState("");
  const [busy, setBusy] = useState<"load" | "save" | null>(null);
  const [dirty, setDirty] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [uncertain, setUncertain] = useState(false);
  const pending = useRef<PricingHierarchyWrite | null>(null);
  const controller = useRef<AbortController | null>(null);
  const validSecurity = securityPattern.test(securityId.trim());
  const ready = loadedId === securityId.trim().toLowerCase();
  const locked = Boolean(busy) || uncertain;
  useEffect(() => { onDirtyChange?.(dirty || uncertain); }, [dirty, uncertain, onDirtyChange]);
  useEffect(() => () => { controller.current?.abort(); }, []);

  function edit() { setDirty(true); setNotice(""); setError(""); }
  function update(key: number, field: "sourceId" | "name" | "days", value: string) {
    setRows((current) => current.map((row) => row.key === key ? { ...row, [field]: value } : row)); edit();
  }
  function move(index: number, offset: number) {
    setRows((current) => {
      const next = [...current];
      [next[index], next[index + offset]] = [next[index + offset]!, next[index]!];
      return next;
    }); edit();
  }
  async function load() {
    if (!canRead || !validSecurity || busy || uncertain) return;
    if (dirty && !window.confirm("Discard the unsaved source hierarchy and reload current policy?")) return;
    const active = new AbortController(); controller.current = active;
    setBusy("load"); setError(""); setNotice(""); setRetained(null); setLoadedId(""); setRows([]); setEffectiveAt("");
    const requested = securityId.trim().toLowerCase();
    try {
      const result = await getPricingHierarchy(requested, accountId, { signal: active.signal });
      if (active.signal.aborted) return;
      setRetained(result);
      setRows([...result.entries].sort((a, b) => a.priority - b.priority).map((entry) => ({ key: nextKey.current++, sourceId: entry.sourceId, name: entry.sourceDisplayName, days: String(entry.maxDaysStale) })));
      setLoadedId(requested); setDirty(false);
    } catch (failure) {
      if (active.signal.aborted) return;
      if (isApiError(failure) && failure.status === 404) {
        setLoadedId(requested); setDirty(false);
        setNotice("No current source hierarchy was returned for this security and account. Define its first version below; saving requires the pricing service to be available.");
      } else {
        const details = describeApiError(failure, "Source hierarchy is unavailable. Reload before editing.");
        setError([details.summary, ...details.details].join(" "));
      }
    } finally { if (!active.signal.aborted) { setBusy(null); controller.current = null; } }
  }

  const timestamp = effectiveAt ? new Date(`${effectiveAt}Z`) : null;
  const effective = timestamp && !Number.isNaN(timestamp.valueOf()) ? timestamp.toISOString() : null;
  const validation = !canModify ? "Read only · source maintenance requires Modify Security Master permission."
    : !ready ? "Load the selected security's current account hierarchy first."
      : rows.length === 0 ? "Add at least one source."
        : rows.some((row) => !row.sourceId.trim() || !row.name.trim()) ? "Every source needs an ID and display name."
          : new Set(rows.map((row) => row.sourceId.trim().toLowerCase())).size !== rows.length ? "Source IDs must be distinct, ignoring case."
            : rows.some((row) => !/^\d+$/.test(row.days) || !Number.isSafeInteger(Number(row.days)) || Number(row.days) > 2147483647) ? "Maximum staleness must be a nonnegative whole number of days."
              : !effective ? "Enter an explicit effective timestamp in UTC for the new version."
                : retained && Date.parse(effective) <= Date.parse(retained.asOf) ? "Use an effective timestamp later than the loaded version; retained versions cannot be replaced."
                  : !dirty ? "Edit the hierarchy to define a new version." : null;

  async function save() {
    if (!canRead || !canModify || busy || (!uncertain && validation)) return;
    const request = uncertain ? pending.current : {
      securityId: securityId.trim().toLowerCase(), accountId,
      asOf: effective!, entries: rows.map((row, index) => ({ priority: index + 1, sourceId: row.sourceId.trim(), sourceDisplayName: row.name.trim(), maxDaysStale: Number(row.days) }))
    };
    if (!request) return;
    pending.current = request;
    const active = new AbortController(); controller.current = active;
    setBusy("save"); setError(""); setNotice("");
    try {
      await savePricingHierarchy(request, { signal: active.signal });
      if (active.signal.aborted) return;
      setUncertain(false); pending.current = null; setDirty(false); setLoadedId("");
      setNotice(`Source hierarchy version saved for account ${accountId}, effective ${request.asOf}. Reload to inspect the current effective version. Existing retained price sets keep their original evidence; prepare a new set or linked correction to use changed policy.`);
    } catch (failure) {
      if (active.signal.aborted) return;
      const details = describeApiError(failure, "Source hierarchy could not be saved.");
      setError([details.summary, ...details.details].join(" "));
      // Same immutable timestamp and exact content make retry safe. Never invent another version
      // while the first write's outcome is unknown.
      if (!isApiError(failure) || failure.status === 0 || failure.status >= 500) setUncertain(true);
    } finally { if (!active.signal.aborted) { setBusy(null); controller.current = null; } }
  }

  return <section aria-label="Pricing source hierarchy maintenance" className="flex min-w-0 flex-col gap-4">
    <p className="text-sm">Maintain the ordered source chain for this account. Lower priority numbers are checked first; each source has its own staleness limit. Saving creates an effective-dated policy version.</p>
    <p className="break-all text-xs">Account scope: {accountId}</p>
    {!canRead ? <StatusBanner tone="warning" title="Source hierarchy access unavailable" detail="Pricing read permission and a verified account scope are required." /> : <>
      <FormGrid columns={2}>
        <FormRow label="Hierarchy security ID" labelFor={`${id}-security`}><Input id={`${id}-security`} value={securityId} disabled={locked} onChange={(event) => {
          if (dirty && !window.confirm("Discard unsaved source hierarchy changes and change security?")) return;
          setSecurityId(event.target.value); setLoadedId(""); setRetained(null); setRows([]); setDirty(false); setError(""); setNotice("");
        }} /></FormRow>
      </FormGrid>
      <div><Button variant="outline" disabled={!validSecurity || locked} onClick={() => void load()}>{busy === "load" ? "Loading source hierarchy…" : "Load source hierarchy"}</Button></div>
      {!validSecurity ? <p className="text-xs text-muted-foreground">Enter a complete Security Master UUID to load its account source hierarchy.</p> : null}
      {error ? <StatusBanner role="alert" tone="danger" title="Source hierarchy unavailable" detail={error} /> : null}
      {notice ? <p role="status" className="text-sm">{notice}</p> : null}
      {retained ? <p className="text-xs">Loaded version effective {retained.asOf} · Maintained by {retained.updatedBy}</p> : null}
      {ready ? <form aria-label="Edit pricing source hierarchy" onSubmit={(event) => { event.preventDefault(); void save(); }} className="flex min-w-0 flex-col gap-3">
        <fieldset disabled={!canModify || locked} className="flex min-w-0 flex-col gap-3">
          <legend className="mb-2 text-sm font-semibold">Ordered pricing sources</legend>
          {rows.map((row, index) => <fieldset key={row.key} className="flex min-w-0 flex-col gap-2 rounded border border-border p-3">
            <legend className="px-1 text-xs">Priority {index + 1}</legend>
            <FormGrid columns={3}>
              <FormRow label={`Source ID ${index + 1}`} labelFor={`${id}-${row.key}-source`}><Input id={`${id}-${row.key}-source`} value={row.sourceId} onChange={(event) => update(row.key, "sourceId", event.target.value)} /></FormRow>
              <FormRow label={`Source name ${index + 1}`} labelFor={`${id}-${row.key}-name`}><Input id={`${id}-${row.key}-name`} value={row.name} onChange={(event) => update(row.key, "name", event.target.value)} /></FormRow>
              <FormRow label={`Maximum days stale ${index + 1}`} labelFor={`${id}-${row.key}-days`}><Input id={`${id}-${row.key}-days`} inputMode="numeric" value={row.days} onChange={(event) => update(row.key, "days", event.target.value)} /></FormRow>
            </FormGrid>
            <div className="flex flex-wrap gap-2">
              <Button type="button" size="sm" variant="outline" disabled={index === 0} onClick={() => move(index, -1)}>Move source {index + 1} up</Button>
              <Button type="button" size="sm" variant="outline" disabled={index === rows.length - 1} onClick={() => move(index, 1)}>Move source {index + 1} down</Button>
              <Button type="button" size="sm" variant="outline" onClick={() => { setRows((current) => current.filter((item) => item.key !== row.key)); edit(); }}>Remove source {index + 1}</Button>
            </div>
          </fieldset>)}
          {rows.length === 0 ? <p className="text-xs">No sources configured.</p> : null}
          <div><Button type="button" variant="outline" onClick={() => { setRows((current) => [...current, { key: nextKey.current++, sourceId: "", name: "", days: "0" }]); edit(); }}>Add pricing source</Button></div>
          <FormGrid columns={2}><FormRow label="New hierarchy effective at (UTC)" labelFor={`${id}-effective`}><Input id={`${id}-effective`} type="datetime-local" step="1" value={effectiveAt} onChange={(event) => { setEffectiveAt(event.target.value); edit(); }} /></FormRow></FormGrid>
        </fieldset>
        <p id={`${id}-reason`} className="text-xs text-muted-foreground">{uncertain ? "The write outcome is unknown. Retry only the unchanged version with its original effective timestamp; other edits are locked." : validation || "Review source order, staleness and effective time before saving."}</p>
        <div><Button type="submit" disabled={!canModify || Boolean(busy) || (!uncertain && Boolean(validation))} aria-describedby={`${id}-reason`}>{busy === "save" ? "Saving source hierarchy…" : uncertain ? "Retry unchanged hierarchy version" : "Save source hierarchy version"}</Button></div>
      </form> : null}
      {!canModify && !ready ? <p className="text-xs">Read only · source maintenance requires Modify Security Master permission.</p> : null}
    </>}
  </section>;
}
