import { useEffect, useState, type FormEvent, type ReactNode } from "react";
import { useSearchParams } from "react-router-dom";
import { Badge, type BadgeProps } from "@/components/ui/badge";
import { Button, type ButtonProps } from "@/components/ui/button";
import { DensityToggle } from "@/components/ui/density-toggle";
import { FormGrid, FormRow } from "@/components/ui/form";
import { Input } from "@/components/ui/input";
import { PanelSurface } from "@/components/ui/panel-surface";
import { Select } from "@/components/ui/select";
import { SkeletonText } from "@/components/ui/skeleton";
import { StatusBanner } from "@/components/ui/status-banner";
import { ThemeToggle } from "@/components/ui/theme-toggle";
import { DenseDataTable, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { DenseRowDetailPanel } from "@/components/meridian/dense-row-detail-accessibility";
import { WorkspaceWorkbenchShell } from "@/components/meridian/workspace-workbench-shell";
import { DESIGN_SYSTEM_SEVERITIES, StatusBadge, type DesignSystemSeverity } from "@/design-system/status";
import { applyDensity } from "@/lib/density";
import { formatMoney, formatUtcTimestamp } from "@/lib/format";
import { applyAppearance } from "@/lib/theme";
import {
  DEFAULT_WORKBENCH_OPTIONS, readWorkbenchOptions, writeWorkbenchOptions, type WorkbenchOptions
} from "./design-system-workbench-state";
import "./design-system-workbench.css";

interface SampleRecord {
  id: string;
  name: string;
  amount: number;
  severity: DesignSystemSeverity;
  source: string;
  observed: string;
}

const SAMPLE_RECORDS: SampleRecord[] = [
  { id: "sample-positive", name: "Positive sample", amount: 1250, severity: "review", source: "Synthetic statement", observed: "2026-01-15T14:30:00Z" },
  { id: "sample-negative", name: "Negative sample", amount: -500, severity: "blocked", source: "Synthetic ledger", observed: "2026-01-15T14:31:00Z" },
  { id: "sample-zero", name: "Zero sample", amount: 0, severity: "ready", source: "Synthetic comparison", observed: "2026-01-15T14:32:00Z" }
];

const INSPECTOR_ID = "workbench-record-inspector";
const BUTTON_VARIANTS: Array<{ variant: NonNullable<ButtonProps["variant"]>; label: string }> = [
  { variant: "default", label: "Primary preview" },
  { variant: "secondary", label: "Secondary preview" },
  { variant: "outline", label: "Outline preview" },
  { variant: "ghost", label: "Ghost preview" },
  { variant: "destructive", label: "Destructive preview" }
];
const BADGE_VARIANTS: Array<NonNullable<BadgeProps["variant"]>> = [
  "default", "outline", "success", "warning", "danger", "paper", "live", "research"
];
const SAMPLE_COLUMNS: DenseDataTableColumn<SampleRecord>[] = [
  { id: "record", label: "Record", render: (row) => <span className="font-medium">{row.name}</span> },
  { id: "amount", label: "Amount (USD)", numeric: true, render: (row) => <span className="font-mono">{formatSampleAmount(row.amount)}</span> },
  { id: "status", label: "Status", render: (row) => <StatusBadge status={row.severity} /> },
  { id: "observed", label: "Observed", render: (row) => <span className="font-mono text-muted-foreground">{formatUtcTimestamp(new Date(row.observed))}</span> },
  { id: "source", label: "Source", render: (row) => <span className="text-muted-foreground">{row.source}</span> }
];

function formatSampleAmount(amount: number): string {
  return formatMoney(amount, { currency: "USD", signed: true, decimals: 2 });
}

function Case({ id, title, description, children, className = "" }: {
  id: string; title: string; description: string; children: ReactNode; className?: string;
}) {
  return (
    <PanelSurface className={`native-workbench-case ${className}`} role="region" aria-labelledby={id}>
      <h2 id={id}>{title}</h2>
      <p className="native-workbench-description">{description}</p>
      {children}
    </PanelSurface>
  );
}

/** Standalone native-adapter preview. It deliberately mounts no application providers. */
export function DesignSystemWorkbench() {
  const [search, setSearch] = useSearchParams();
  const options = readWorkbenchOptions(search);
  const [selectedId, setSelectedId] = useState(SAMPLE_RECORDS[0].id);
  const [buttonMessage, setButtonMessage] = useState("");
  const [reference, setReference] = useState("Synthetic reference");
  const [referenceError, setReferenceError] = useState(false);
  const [referenceMessage, setReferenceMessage] = useState("");
  const selected = options.data === "populated" ? SAMPLE_RECORDS.find((row) => row.id === selectedId) : null;

  useEffect(() => {
    const previousTheme = document.documentElement.getAttribute("data-theme");
    const previousDensity = document.body.getAttribute("data-theme-density");
    const previousTitle = document.title;
    document.title = "Native component workbench - Meridian";
    return () => {
      if (previousTheme === null) document.documentElement.removeAttribute("data-theme");
      else document.documentElement.setAttribute("data-theme", previousTheme);
      if (previousDensity === null) document.body.removeAttribute("data-theme-density");
      else document.body.setAttribute("data-theme-density", previousDensity);
      document.title = previousTitle;
    };
  }, []);
  useEffect(() => applyAppearance(options.theme), [options.theme]);
  useEffect(() => applyDensity(options.density), [options.density]);

  function updateOption<K extends keyof WorkbenchOptions>(key: K, value: WorkbenchOptions[K]) {
    setSearch(writeWorkbenchOptions({ ...options, [key]: value }), { replace: true });
  }
  function reset() {
    setSearch(writeWorkbenchOptions(DEFAULT_WORKBENCH_OPTIONS), { replace: true });
    setSelectedId(SAMPLE_RECORDS[0].id);
    setButtonMessage("");
    setReference("Synthetic reference");
    setReferenceError(false);
    setReferenceMessage("");
  }
  function saveReference(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const invalid = !reference.trim();
    setReferenceError(invalid);
    setReferenceMessage(invalid ? "" : "Sample reference saved locally. No data was sent.");
  }

  return (
    <main className="native-workbench" data-design-system-workbench="true" aria-labelledby="native-workbench-title">
      <header className="native-workbench-header">
        <div>
          <p className="native-workbench-kicker">Meridian development</p>
          <h1 id="native-workbench-title">Meridian native component workbench</h1>
          <p>Synthetic component fixtures · no backend. These are the same adapters used by the workstation.</p>
        </div>
        <a href="/workstation/">Open workstation</a>
      </header>

      <PanelSurface className="native-workbench-controls" role="region" aria-label="Workbench controls">
        <div className="native-workbench-control">
          <span>Appearance</span>
          <ThemeToggle value={options.theme} onChange={(value) => updateOption("theme", value)} persist={null} apply={false} />
        </div>
        <div className="native-workbench-control">
          <span>Display density</span>
          <DensityToggle value={options.density} onChange={(value) => updateOption("density", value)} persist={null} apply={false} />
        </div>
        <FormRow label="Specimen surface" labelFor="workbench-surface">
          <Select id="workbench-surface" value={options.surface} onChange={(event) => updateOption("surface", event.target.value as WorkbenchOptions["surface"])}>
            <option value="inherited">Inherit workstation</option><option value="light">Explicit light</option>
          </Select>
        </FormRow>
        <FormRow label="Data state" labelFor="workbench-data-state">
          <Select id="workbench-data-state" value={options.data} onChange={(event) => updateOption("data", event.target.value as WorkbenchOptions["data"])}>
            <option value="populated">Populated</option><option value="loading">Initial loading</option><option value="empty">Empty</option><option value="error">Error</option>
          </Select>
        </FormRow>
        <FormRow label="Button state" labelFor="workbench-button-state">
          <Select id="workbench-button-state" value={options.buttons} onChange={(event) => updateOption("buttons", event.target.value as WorkbenchOptions["buttons"])}>
            <option value="normal">Normal</option><option value="disabled">Disabled</option><option value="busy">Busy</option>
          </Select>
        </FormRow>
        <div className="native-workbench-reset"><Button variant="outline" onClick={reset}>Reset workbench</Button></div>
      </PanelSurface>

      <div className="native-workbench-specimens" data-appearance={options.surface === "light" ? "light" : undefined} data-workbench-surface={options.surface}>
        <div className="native-workbench-grid">
          <Case id="workbench-buttons" title="Buttons" description="Run a local preview, or inspect its disabled and busy states.">
            <div className="native-workbench-flow">
              {BUTTON_VARIANTS.map(({ variant, label }) => (
                <Button key={variant} variant={variant} aria-label={label}
                  disabled={options.buttons === "disabled"} busy={options.buttons === "busy"}
                  busyLabel="Running preview" disabledReason={options.buttons === "disabled" ? "This synthetic preview is disabled." : options.buttons === "busy" ? "A synthetic preview is in progress." : null}
                  onClick={() => setButtonMessage(`${label} completed locally. No data was sent.`)}>
                  {label}
                </Button>
              ))}
            </div>
            <p className="native-workbench-note" role="status">{buttonMessage || "Preview actions affect this page only."}</p>
          </Case>

          <Case id="workbench-status" title="Status and badges" description="Canonical severity and environment roles, including sentence-length labels.">
            <div className="native-workbench-flow">{DESIGN_SYSTEM_SEVERITIES.map((severity) => <StatusBadge key={severity} status={severity} />)}</div>
            <div className="native-workbench-flow native-workbench-badges">{BADGE_VARIANTS.map((variant) => <Badge key={variant} variant={variant} dot>{variant}</Badge>)}</div>
            <Badge variant="warning">2 of 3 synthetic records need review</Badge>
          </Case>

          <Case id="workbench-form" title="Form controls" description="Local validation and read-only input; this form never sends a request." className="native-workbench-wide">
            <form noValidate onSubmit={saveReference}>
              <FormGrid>
                <FormRow label="Sample reference" labelFor="workbench-reference" error={referenceError ? <span id="workbench-reference-error">Enter a sample reference.</span> : null}
                  hint={<span id="workbench-reference-hint">A reference is required for this local demonstration.</span>}>
                  <Input id="workbench-reference" required value={reference} error={referenceError}
                    aria-describedby={referenceError ? "workbench-reference-error" : "workbench-reference-hint"}
                    onChange={(event) => { setReference(event.target.value); setReferenceError(false); setReferenceMessage(""); }} />
                </FormRow>
                <FormRow label="Fixture source" labelFor="workbench-fixture-source" hint="Read-only fixture metadata.">
                  <Input id="workbench-fixture-source" readOnly value="Synthetic fixtures" />
                </FormRow>
              </FormGrid>
              <div className="native-workbench-form-actions"><Button type="submit">Save sample reference</Button><p role="status">{referenceMessage}</p></div>
            </form>
          </Case>

          <Case id="workbench-records" title="Table and inspector" description="Fixed synthetic USD records exercise signed values, metadata, selection, and keyboard focus." className="native-workbench-wide">
            <WorkspaceWorkbenchShell label="Synthetic record workspace" contentLabel="Synthetic record list" inspectorLabel="Record detail preview" inspectorRole="region"
              inspector={
                <DenseRowDetailPanel id={INSPECTOR_ID} ariaLabel="Synthetic record inspector" className="native-workbench-inspector">
                  <h3>{selected ? selected.name : "Synthetic record inspector"}</h3>
                  {selected ? (
                    <dl className="native-workbench-facts">
                      <div><dt>Amount (USD)</dt><dd className="font-mono">{formatSampleAmount(selected.amount)}</dd></div>
                      <div><dt>Status</dt><dd><StatusBadge status={selected.severity} /></dd></div>
                      <div><dt>Source</dt><dd className="text-muted-foreground">{selected.source}</dd></div>
                      <div><dt>As of</dt><dd className="font-mono text-muted-foreground">{formatUtcTimestamp(new Date(selected.observed))}</dd></div>
                    </dl>
                  ) : <p>{options.data === "loading" ? "Loading synthetic records." : options.data === "error" ? "Synthetic records are unavailable. Retry the preview." : "Select a synthetic record to inspect it."}</p>}
                </DenseRowDetailPanel>
              }>
              <div data-workbench-data-state={options.data} aria-busy={options.data === "loading"}>
                {options.data === "loading" ? <><p role="status">Loading synthetic records.</p><SkeletonText lines={4} /></> : options.data === "error" ? (
                  <div className="native-workbench-error"><StatusBanner tone="danger" title="Synthetic records unavailable" detail="This is a simulated read failure. No service was contacted." role="alert" />
                    <Button variant="outline" onClick={() => updateOption("data", "populated")}>Retry synthetic records</Button></div>
                ) : (
                  <DenseDataTable columns={SAMPLE_COLUMNS} rows={options.data === "empty" ? [] : SAMPLE_RECORDS} getRowId={(row) => row.id}
                    getRowAriaLabel={(row) => `Select ${row.name}`} getRowAriaControls={() => INSPECTOR_ID}
                    onRowSelect={(row) => setSelectedId(row.id)} selectedRowId={options.data === "populated" ? selectedId : null}
                    density={options.density} emptyText="No synthetic records." ariaLabel="Synthetic records" />
                )}
              </div>
            </WorkspaceWorkbenchShell>
          </Case>
        </div>
      </div>
    </main>
  );
}
