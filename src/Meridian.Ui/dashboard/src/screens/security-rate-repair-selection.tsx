import { useId, useState, type FormEvent } from "react";
import { Link } from "react-router-dom";
import { DenseDataTable, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { FormGrid, FormRow } from "@/components/ui/form";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import type { CreateRateRepairBatch, RateRepairAction, RateRepairCandidate } from "@/lib/api/security-rate-repair.api";

export const rateRepairCohortKey = (row: RateRepairCandidate) =>
  JSON.stringify([row.assetClass, row.sourceSystem, row.sourceField, row.sourceSchemaVersion ?? "", row.rateRole]);
const guid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const localFix = (path: string) => path.startsWith("/") && !path.startsWith("//") ? path : null;
const initialForm = { unit: "", fundAccountId: "", ledgerBookId: "", fundProfileId: "", periodId: "",
  sourceSchemaVersion: "", effectiveDate: "", evidenceId: "", justification: "", attested: false };

export function RateRepairSelection({ candidates, action, disabled, initialSearch = "", onCreate }: {
  candidates: RateRepairCandidate[]; action?: RateRepairAction; disabled: boolean; initialSearch?: string;
  onCreate: (input: CreateRateRepairBatch) => void;
}) {
  const id = useId();
  const [search, setSearch] = useState(initialSearch);
  const [vendor, setVendor] = useState("");
  const [assetClass, setAssetClass] = useState("");
  const [issuer, setIssuer] = useState("");
  const [selected, setSelected] = useState<string[]>([]);
  const [form, setForm] = useState(initialForm);
  const [validation, setValidation] = useState("");
  const selectedRows = candidates.filter(row => selected.includes(row.securityId));
  const first = selectedRows[0];
  const units = first?.allowedUnits.filter(unit => selectedRows.every(row => row.allowedUnits.includes(unit))) ?? [];
  const rows = candidates.filter(row => (!vendor || row.sourceSystem === vendor) && (!assetClass || row.assetClass === assetClass)
    && (!issuer || (row.issuer ?? "Not retained") === issuer) && (!search ||
      `${row.displayName} ${row.securityId}`.toLowerCase().includes(search.toLowerCase())));
  const permissionReason = action?.enabled ? null : action?.disabledReason ?? "Current batch-creation permission is unavailable. Refresh the queue.";
  const locked = disabled || Boolean(permissionReason);
  const compatibleVisible = first ? rows.filter(row => row.eligible && rateRepairCohortKey(row) === rateRepairCohortKey(first)) : [];
  const options = (get: (row: RateRepairCandidate) => string) => [...new Set(candidates.map(get))].sort();

  function pick(row: RateRepairCandidate, checked: boolean) {
    if (locked || !row.eligible || (first && rateRepairCohortKey(first) !== rateRepairCohortKey(row))) return;
    setSelected(current => checked ? [...current, row.securityId] : current.filter(value => value !== row.securityId));
    setForm(current => ({ ...current, unit: "", attested: false })); setValidation("");
  }
  function edit<K extends keyof typeof initialForm>(key: K, value: typeof initialForm[K]) {
    setForm(current => ({ ...current, [key]: value, ...(key !== "attested" ? { attested: false } : {}) })); setValidation("");
  }
  function create(event: FormEvent) {
    event.preventDefault();
    if (locked || !first) return;
    if (!guid.test(form.fundAccountId.trim()) || !guid.test(form.ledgerBookId.trim()) || !form.fundProfileId.trim()
      || !form.periodId.trim() || !(first.sourceSchemaVersion ?? form.sourceSchemaVersion.trim()) || !first.rateRole
      || !units.includes(form.unit) || !form.effectiveDate || !form.evidenceId.trim() || !form.justification.trim() || !form.attested
      || selectedRows.some(row => !Number.isSafeInteger(row.version))) {
      setValidation("Complete the accounting scope, source schema, rate role, unit, effective date and evidence, then confirm that the evidence covers every selected record."); return;
    }
    onCreate({ requestId: crypto.randomUUID(), scope: { fundAccountId: form.fundAccountId.trim(), ledgerBookId: form.ledgerBookId.trim(),
      fundProfileId: form.fundProfileId.trim(), periodId: form.periodId.trim() },
      cohort: { sourceSystem: first.sourceSystem, sourceField: first.sourceField,
        sourceSchemaVersion: first.sourceSchemaVersion ?? form.sourceSchemaVersion.trim(), rateRole: first.rateRole },
      items: selectedRows.map(row => ({ securityId: row.securityId, expectedVersion: row.version })), unit: form.unit,
      effectiveFrom: `${form.effectiveDate}T00:00:00Z`, evidenceId: form.evidenceId.trim(), justification: form.justification.trim(),
      evidenceAppliesToAllRecords: true });
  }
  const columns: DenseDataTableColumn<RateRepairCandidate>[] = [
    { id: "select", label: "Select", render: row => <Checkbox aria-label={`Select ${row.displayName}`}
      checked={selected.includes(row.securityId)} disabled={locked || !row.eligible || Boolean(first && rateRepairCohortKey(first) !== rateRepairCohortKey(row))}
      onCheckedChange={checked => pick(row, checked)} /> },
    { id: "security", label: "Security / issuer", rowHeader: true, render: row => <><Link className="font-medium underline" to={`/accounting/security-master?securityId=${encodeURIComponent(row.securityId)}`}>{row.displayName}</Link><span className="block text-xs text-muted-foreground">{row.assetClass} · {row.issuer ?? "Issuer not retained"}</span></> },
    { id: "source", label: "Source / schema", render: row => <>{row.sourceSystem}<span className="block text-xs text-muted-foreground">{row.sourceField} · {row.sourceSchemaVersion ?? "Schema version needs evidence"}</span></> },
    { id: "raw", label: "Original scalar", numeric: true, render: row => <span className="font-mono break-all">{row.rawValue ?? "Unavailable"}</span> },
    { id: "unit", label: "Declared unit", render: row => row.currentUnit ?? "Missing" },
    { id: "state", label: "Repair eligibility", render: row => <>{row.eligible
      ? first && rateRepairCohortKey(first) !== rateRepairCohortKey(row) ? "Different source cohort" : "Eligible for reviewed declaration"
      : <><span>{row.blockerReason ?? "Not eligible for this repair"}</span>{localFix(row.fixPath) ? <Link className="block underline" to={row.fixPath}>Open required repair</Link> : null}</>}</> }
  ];
  return <section aria-labelledby={`${id}-title`} className="space-y-4">
    <h3 id={`${id}-title`} className="font-semibold">Find undeclared rate units</h3>
    <p className="text-sm text-muted-foreground">Vendor, class and issuer narrow the queue. A batch must share an asset class, source field and schema, rate role and evidence scope. Original scalars stay unchanged.</p>
    <FormGrid columns={3}>
      <FormRow label="Vendor" labelFor={`${id}-vendor`}><Select id={`${id}-vendor`} value={vendor} onChange={event => setVendor(event.target.value)}><option value="">All vendors</option>{options(row => row.sourceSystem).map(value => <option key={value}>{value}</option>)}</Select></FormRow>
      <FormRow label="Asset class" labelFor={`${id}-class`}><Select id={`${id}-class`} value={assetClass} onChange={event => setAssetClass(event.target.value)}><option value="">All classes</option>{options(row => row.assetClass).map(value => <option key={value}>{value}</option>)}</Select></FormRow>
      <FormRow label="Issuer" labelFor={`${id}-issuer`}><Select id={`${id}-issuer`} value={issuer} onChange={event => setIssuer(event.target.value)}><option value="">All issuers</option>{options(row => row.issuer ?? "Not retained").map(value => <option key={value}>{value}</option>)}</Select></FormRow>
    </FormGrid>
    <label className="grid gap-1 text-sm">Search security or name<Input value={search} onChange={event => setSearch(event.target.value)} /></label>
    <p className="text-xs text-muted-foreground">{rows.length} visible · {selectedRows.length} selected across all filters. Showing fetched discovery pages.</p>
    <div className="flex flex-wrap items-center gap-3">
      <Button type="button" size="sm" variant="outline" disabled={locked || !first || compatibleVisible.every(row => selected.includes(row.securityId))}
        disabledReason={!first ? "Select one record to establish the exact source cohort first." : null}
        onClick={() => { setSelected(current => [...new Set([...current, ...compatibleVisible.map(row => row.securityId)])]); setForm(current => ({ ...current, unit: "", attested: false })); }}>
        Select all compatible records in this view
      </Button>
      <p className="text-xs text-muted-foreground">Uses the current filters and fetched pages. Load more source records to expand the view.</p>
    </div>
    <DenseDataTable columns={columns} rows={rows} getRowId={row => row.securityId} ariaLabel="Rate-unit repair candidates" emptyText="No matching rate-unit repair candidates in this discovery result." />
    {permissionReason ? <p role="status" className="text-sm text-warning">{permissionReason}</p> : null}
    {first ? <form onSubmit={create} className="space-y-3 border-t border-border pt-4">
      <h4 className="font-semibold">Freeze {selectedRows.length} selected records for review</h4>
      <p className="text-sm">Source: {first.sourceSystem} · {first.sourceField} · {first.sourceSchemaVersion ?? "Attest schema version below"}. Base versions are retained with each record.</p>
      <fieldset disabled={locked} className="space-y-3">
        <FormGrid columns={3}>
          <FormRow label="Rate unit" labelFor={`${id}-unit`}><Select id={`${id}-unit`} value={form.unit} onChange={event => edit("unit", event.target.value)}><option value="">Choose from evidence</option>{units.map(unit => <option key={unit}>{unit}</option>)}</Select></FormRow>
          <FormRow label="Source schema version" labelFor={`${id}-schema`} hint={!first.sourceSchemaVersion ? "Not retained on these records. The evidence attestation must establish which source schema applies to this selected roster." : undefined}><Input id={`${id}-schema`} value={first.sourceSchemaVersion ?? form.sourceSchemaVersion} readOnly={Boolean(first.sourceSchemaVersion)} onChange={event => edit("sourceSchemaVersion", event.target.value)} /></FormRow>
          <FormRow label="Rate role" labelFor={`${id}-role`}><Input id={`${id}-role`} value={first.rateRole} readOnly /></FormRow>
          <FormRow label="Fund account ID" labelFor={`${id}-account`}><Input id={`${id}-account`} value={form.fundAccountId} onChange={event => edit("fundAccountId", event.target.value)} /></FormRow>
          <FormRow label="Ledger book ID" labelFor={`${id}-book`}><Input id={`${id}-book`} value={form.ledgerBookId} onChange={event => edit("ledgerBookId", event.target.value)} /></FormRow>
          <FormRow label="Fund profile" labelFor={`${id}-fund`}><Input id={`${id}-fund`} value={form.fundProfileId} onChange={event => edit("fundProfileId", event.target.value)} /></FormRow>
          <FormRow label="Accounting period" labelFor={`${id}-period`}><Input id={`${id}-period`} value={form.periodId} onChange={event => edit("periodId", event.target.value)} /></FormRow>
          <FormRow label="Effective date (UTC)" labelFor={`${id}-date`}><Input id={`${id}-date`} type="date" value={form.effectiveDate} onChange={event => edit("effectiveDate", event.target.value)} /></FormRow>
          <FormRow label="Retained evidence ID" labelFor={`${id}-evidence`} hint={<Link to="/reporting/evidence" target="_blank" rel="noreferrer" className="underline">Open Evidence Vault in a new tab</Link>}><Input id={`${id}-evidence`} value={form.evidenceId} onChange={event => edit("evidenceId", event.target.value)} /></FormRow>
        </FormGrid>
        <label className="grid gap-1 text-sm">Justification<Input value={form.justification} onChange={event => edit("justification", event.target.value)} /></label>
        <Checkbox label="I reviewed the source field definition or prospectus and this evidence applies to every selected record."
          hint="A value's size does not establish its unit. This freezes a proposal; it does not submit, approve or publish it."
          checked={form.attested} onCheckedChange={checked => edit("attested", checked)} />
      </fieldset>
      {validation ? <p role="alert" className="text-sm text-danger">{validation}</p> : null}
      <Button type="submit" disabled={locked || !form.attested || !units.includes(form.unit)} disabledReason={permissionReason}>Freeze repair batch</Button>
      <Button type="button" variant="ghost" disabled={disabled} onClick={() => { setSelected([]); setValidation(""); }}>Clear selection</Button>
    </form> : null}
  </section>;
}
