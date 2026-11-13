import { TableWorkbench } from "@/components/meridian/table-workbench";
import { formatNumber, formatPercent, formatRatioAsPercent } from "@/lib/format";
import { useCallback, useEffect, useMemo, useState } from "react";
import { Link } from "react-router-dom";
import { DatabaseZap, HardDrive, RefreshCcw, ShieldCheck } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { StatusBanner } from "@/components/ui/status-banner";
import { ArchiveMaintenanceOperations } from "@/screens/data-operations-maintenance";
import { DataQualityWatch } from "@/screens/data-quality-watch";
import {
  applyIngestionOperationAction,
  executeStorageMaintenance,
  getIngestionOperations,
  getStorageAssurance,
  previewStorageMaintenance
} from "@/lib/api/data-operations-assurance.api";
import type {
  IngestionOperationRow,
  IngestionOperationsSnapshot,
  StorageAssuranceSnapshot,
  StorageMaintenanceAction,
  StorageMaintenancePreview,
  StorageMaintenanceResult
} from "@/types/data-operations-assurance";
import { cn } from "@/lib/utils";
import { usesTrackedCaps } from "@/design-system/label-case";
import { formatUtcTimestamp } from "@/lib/format";
import { Select } from "@/components/ui/select";

export function IngestionOperationsWorkstream() {
  const [snapshot, setSnapshot] = useState<IngestionOperationsSnapshot | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const [rationale, setRationale] = useState("Operator-reviewed ingestion recovery");
  const [stateFilter, setStateFilter] = useState("All");

  const refresh = useCallback(async () => {
    setError(null);
    try {
      setSnapshot(await getIngestionOperations());
    } catch (reason) {
      setError(errorMessage(reason));
    }
  }, []);

  useEffect(() => { void refresh(); }, [refresh]);

  const jobs = useMemo(() => snapshot?.jobs.filter((job) => stateFilter === "All" || job.state === stateFilter) ?? [], [snapshot, stateFilter]);

  async function apply(job: IngestionOperationRow, action: string) {
    const operationKey = `${job.jobId}:${action}`;
    setBusy(operationKey);
    setError(null);
    try {
      await applyIngestionOperationAction(job.jobId, action, {
        idempotencyKey: crypto.randomUUID(),
        rationale
      });
      await refresh();
    } catch (reason) {
      setError(errorMessage(reason));
    } finally {
      setBusy(null);
    }
  }

  return (
    <section aria-labelledby="ingestion-operations-title" className="workspace-region space-y-3">
      <CardHeader>
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div>
            <CardTitle id="ingestion-operations-title">Ingestion Operations Center</CardTitle>
            <CardDescription>Operate resumable imports and backfills from checkpoint through retained evidence.</CardDescription>
          </div>
          <Button size="sm" variant="outline" onClick={() => void refresh()}><RefreshCcw className="mr-2 h-4 w-4" />Refresh</Button>
        </div>
      </CardHeader>
      <CardContent className="space-y-4">
        {error ? <StatusBanner tone="danger" title="Ingestion operations unavailable" detail={error} /> : null}
        <div className="grid gap-2 sm:grid-cols-4">
          <Metric label="Running" value={snapshot?.summary.running ?? 0} />
          <Metric label="Failed" value={snapshot?.summary.failed ?? 0} tone="danger" />
          <Metric label="Paused" value={snapshot?.summary.paused ?? 0} />
          <Metric label="Resumable" value={snapshot?.summary.resumable ?? 0} tone="success" />
        </div>
        <div className="grid gap-3 md:grid-cols-[12rem_minmax(18rem,1fr)]">
          <label className="space-y-1 text-xs font-medium text-muted-foreground">State
            <Select className="min-h-9 w-full text-sm" value={stateFilter} onChange={(event) => setStateFilter(event.target.value)}>
              {["All", "Queued", "Running", "Paused", "Failed", "Completed", "Cancelled"].map((state) => <option key={state}>{state}</option>)}
            </Select>
          </label>
          <label className="space-y-1 text-xs font-medium text-muted-foreground">Action rationale
            <Input value={rationale} onChange={(event) => setRationale(event.target.value)} aria-label="Ingestion action rationale" />
          </label>
        </div>
        <div className="overflow-x-auto rounded-[2px] border border-border">
          <TableWorkbench workbenchId="data-ingestion-operations" sourceKey={snapshot?.generatedAt ?? "loading"}
            tableClassName="w-full min-w-[58rem] text-left text-sm" maxVisibleRows={null}
            sourceDescription="Authorized ingestion-operation snapshot; active State filter is applied before search and export."
            completePopulation={Boolean(snapshot) && !error} rows={jobs} getRowId={(job) => job.jobId}
            getSearchText={(job) => `${job.jobId} ${job.provider} ${job.workloadType} ${job.symbols.join(" ")} ${job.state}`}
            filterState={{ state: stateFilter }} onRestoreFilters={(filters) => setStateFilter(filters.state ?? "All")}
            ariaLabel="Ingestion jobs" emptyText={!snapshot ? "Loading ingestion operations…" : "No jobs match this filter."}
            columns={[
              { id: "job", label: "Job", render: (job) => <><strong className="font-mono">{job.jobId}</strong><span className="block text-xs">{job.provider} · {job.workloadType} · {job.symbols.join(", ")}</span></>, exportValue: (job) => job.jobId },
              { id: "state", label: "State", render: (job) => <><Badge variant={stateVariant(job.state)}>{job.state}</Badge>{job.errorMessage ? <p className="text-xs text-danger">{job.errorMessage}</p> : null}</>, exportValue: (job) => job.state },
              { id: "progress", label: "Progress", render: (job) => formatPercent(job.progressPercent, { minimumFractionDigits: 1, maximumFractionDigits: 1 }), exportValue: (job) => job.progressPercent },
              { id: "checkpoint", label: "Checkpoint", render: (job) => job.isResumable ? "Durable" : "None", exportValue: (job) => job.isResumable },
              { id: "retry", label: "Retry", render: (job) => `${job.attemptCount}/${job.maxRetries}`, exportValue: (job) => job.attemptCount },
              { id: "evidence", label: "Evidence", render: (job) => job.evidenceRoute ? <Link to={job.evidenceRoute}>Open</Link> : "—", exportValue: (job) => job.evidenceRoute },
              { id: "actions", label: "Actions", render: (job) => <div className="flex flex-wrap gap-1">{job.actions.map((action) => <Button key={action.action} size="sm" variant="outline" disabled={!action.enabled || busy !== null || !rationale.trim()} title={action.disabledReason ?? undefined} onClick={() => void apply(job, action.action)}>{busy === `${job.jobId}:${action.action}` ? "Applying…" : action.label}</Button>)}</div> }
            ]} />
        </div>
      </CardContent>
    </section>
  );
}

export function StorageAssuranceWorkstream() {
  const [snapshot, setSnapshot] = useState<StorageAssuranceSnapshot | null>(null);
  const [preview, setPreview] = useState<StorageMaintenancePreview | null>(null);
  const [result, setResult] = useState<StorageMaintenanceResult | null>(null);
  const [action, setAction] = useState<StorageMaintenanceAction>("QualityCheck");
  const [targetTier, setTargetTier] = useState("Warm");
  const [rationale, setRationale] = useState("Operator-reviewed data assurance maintenance");
  const [confirmation, setConfirmation] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const refresh = useCallback(async () => {
    setError(null);
    try { setSnapshot(await getStorageAssurance()); } catch (reason) { setError(errorMessage(reason)); }
  }, []);
  useEffect(() => { void refresh(); }, [refresh]);

  async function createPreview() {
    setBusy(true); setError(null); setResult(null);
    try {
      const next = await previewStorageMaintenance({ action, relativePath: action === "QualityCheck" ? "." : null, targetTier: action === "TierMigration" ? targetTier : null });
      setPreview(next); setConfirmation("");
    } catch (reason) { setError(errorMessage(reason)); } finally { setBusy(false); }
  }

  async function executePreview() {
    if (!preview) return;
    setBusy(true); setError(null);
    try {
      const completed = await executeStorageMaintenance({ previewId: preview.previewId, idempotencyKey: crypto.randomUUID(), rationale, confirmationText: confirmation });
      setResult(completed); setPreview(null); setConfirmation(""); await refresh();
    } catch (reason) { setError(errorMessage(reason)); } finally { setBusy(false); }
  }

  return (
    <section aria-labelledby="storage-assurance-title" className="workspace-region min-w-0 space-y-3">
      <CardHeader><div className="flex flex-wrap items-start justify-between gap-3"><div><CardTitle id="storage-assurance-title">Storage &amp; Data Assurance</CardTitle><CardDescription>One posture for storage health, data quality, canonicalization, capacity, and safe maintenance.</CardDescription></div><Button size="sm" variant="outline" onClick={() => void refresh()}><RefreshCcw className="mr-2 h-4 w-4" />Refresh</Button></div></CardHeader>
      <CardContent className="space-y-4">
        {error ? <StatusBanner tone="danger" title="Storage assurance action needs attention" detail={error} /> : null}
        {result ? <StatusBanner tone={result.status === "Completed" ? "success" : "warning"} title={`${result.action} ${result.status}`} detail={`${formatBytes(result.affectedBytes)} affected · evidence ${result.evidenceVaultId ?? "not retained"}`} /> : null}
        <div className="grid gap-2 md:grid-cols-4">
          <Metric label="Storage" value={snapshot?.health.status ?? "Loading"} icon={HardDrive} />
          <Metric label="Quality" value={snapshot ? formatRatioAsPercent(snapshot.quality.averageScore, { minimumFractionDigits: 1, maximumFractionDigits: 1 }) : "—"} icon={ShieldCheck} />
          <Metric label="Canonical match" value={snapshot ? formatPercent(snapshot.canonicalization.matchRatePercent, { minimumFractionDigits: 1, maximumFractionDigits: 1 }) : "—"} icon={DatabaseZap} />
          <Metric label="Capacity used" value={snapshot ? formatPercent(snapshot.capacity.usedPercent, { minimumFractionDigits: 1, maximumFractionDigits: 1 }) : "—"} />
        </div>
        <div className="grid gap-3 xl:grid-cols-[minmax(0,1fr)_22rem]">
          <div className="space-y-3">
            <Card><CardHeader><CardTitle className="text-base">Assurance signals</CardTitle></CardHeader><CardContent className="grid gap-3 md:grid-cols-2"><Signal label="Root" value={snapshot?.health.rootLabel ?? "—"} detail={`${snapshot?.health.fileCount ?? 0} files · ${formatBytes(snapshot?.health.totalBytes ?? 0)}`} /><Signal label="Temporary candidates" value={String(snapshot?.health.temporaryFileCount ?? 0)} detail="Only temporary and partial files are cleanup eligible." /><Signal label="Low-quality files" value={String(snapshot?.quality.lowQualityFileCount ?? 0)} detail={`${snapshot?.quality.filesAnalyzed ?? 0} files analyzed`} /><Signal label="Canonical hard fails" value={String(snapshot?.canonicalization.hardFailTotal ?? 0)} detail={`Version ${snapshot?.canonicalization.version ?? 0}`} /></CardContent></Card>
            {snapshot?.alerts.length ? <Card><CardHeader><CardTitle className="text-base">Quality alerts</CardTitle></CardHeader><CardContent className="space-y-2">{snapshot.alerts.map((alert) => <StatusBanner key={alert.alertId} tone={alert.severity === "Critical" ? "danger" : "warning"} title={alert.subject} detail={alert.message} />)}</CardContent></Card> : null}
          </div>
          <Card><CardHeader><CardTitle className="text-base">Guarded maintenance</CardTitle><CardDescription>Preview first; execute only against the unchanged, unexpired candidate set.</CardDescription></CardHeader><CardContent className="space-y-3">
            <label className="block space-y-1 text-xs font-medium text-muted-foreground">Action<Select className="min-h-9 w-full text-sm" value={action} onChange={(event) => { setAction(event.target.value as StorageMaintenanceAction); setPreview(null); }}><option value="QualityCheck">Quality check</option><option value="Cleanup" disabled={!snapshot?.permissions.canDelete}>Temporary-file cleanup</option><option value="TierMigration" disabled={!snapshot?.permissions.canMigrate}>Copy-only tier migration</option></Select></label>
            {action === "TierMigration" ? <label className="block space-y-1 text-xs font-medium text-muted-foreground">Target tier<Select className="min-h-9 w-full text-sm" value={targetTier} onChange={(event) => setTargetTier(event.target.value)}>{["Hot", "Warm", "Cold", "Archive"].map((tier) => <option key={tier}>{tier}</option>)}</Select></label> : null}
            <Button className="w-full" disabled={busy} onClick={() => void createPreview()}>{busy ? "Working…" : "Preview action"}</Button>
            {preview ? <div className="space-y-2 rounded-[2px] border border-warning/50 bg-warning/5 p-3"><div className="font-semibold">Preview: {preview.action}</div><div className="text-xs text-muted-foreground">{preview.candidates.length} candidates · {formatBytes(preview.affectedBytes)} · expires {formatDate(preview.expiresAt)}</div><label className="block space-y-1 text-xs font-medium">Rationale<Input value={rationale} onChange={(event) => setRationale(event.target.value)} /></label><label className="block space-y-1 text-xs font-medium">Type “{preview.confirmationText}”<Input value={confirmation} onChange={(event) => setConfirmation(event.target.value)} /></label><Button variant="destructive" className="w-full" disabled={busy || !rationale.trim() || confirmation !== preview.confirmationText} onClick={() => void executePreview()}>Execute guarded action</Button></div> : null}
          </CardContent></Card>
        </div>
        <DataQualityWatch />
        <ArchiveMaintenanceOperations />
      </CardContent>
    </section>
  );
}

function Metric({ label, value, tone, icon: Icon }: { label: string; value: string | number; tone?: "danger" | "success"; icon?: typeof HardDrive }) {
  return <div className="rounded-[2px] border border-border bg-secondary/20 p-3"><div className="flex items-center gap-2 text-xs uppercase tracking-wide text-muted-foreground">{Icon ? <Icon className="h-4 w-4" /> : null}{label}</div><div className={tone === "danger" ? "mt-1 text-xl font-semibold text-danger" : tone === "success" ? "mt-1 text-xl font-semibold text-success" : "mt-1 text-xl font-semibold"}>{value}</div></div>;
}

function Signal({ label, value, detail }: { label: string; value: string; detail: string }) {
  return <div className="rounded-[2px] border border-border p-3"><div className={cn("text-xs text-muted-foreground", usesTrackedCaps(label) && "uppercase tracking-wide")}>{label}</div><div className="mt-1 font-mono text-lg font-semibold">{value}</div><div className="mt-1 text-xs text-muted-foreground">{detail}</div></div>;
}

function stateVariant(state: string): "default" | "success" | "warning" | "danger" | "outline" {
  if (state === "Failed") return "danger";
  if (state === "Completed") return "success";
  if (state === "Paused") return "warning";
  if (state === "Running") return "default";
  return "outline";
}

function formatBytes(value: number): string {
  if (value < 1024) return `${value} B`;
  const units = ["KB", "MB", "GB", "TB"];
  let current = value / 1024;
  let index = 0;
  while (current >= 1024 && index < units.length - 1) { current /= 1024; index += 1; }
  return `${formatNumber(current, { minimumFractionDigits: 1, maximumFractionDigits: 1 })} ${units[index]}`;
}

function formatDate(value: string): string { return formatUtcTimestamp(new Date(value)); }
function errorMessage(reason: unknown): string { return reason instanceof Error ? reason.message : "The operation could not be completed."; }
