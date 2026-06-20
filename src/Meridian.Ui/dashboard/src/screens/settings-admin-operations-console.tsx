import { Archive, CheckCircle2, Database, HardDrive, PackageCheck, Play, RefreshCcw, ShieldCheck, Trash2, Wrench } from "lucide-react";
import { useCallback, useEffect, useMemo, useState, type ReactNode } from "react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { StatusBanner } from "@/components/ui/status-banner";
import {
  applyAdminRetention,
  createDataPackage,
  disableMaintenanceSchedule,
  enableMaintenanceSchedule,
  executeAdminCleanup,
  getAdminCleanupPreview,
  getAdminErrorCodes,
  getAdminMaintenanceHistory,
  getAdminMaintenanceSchedule,
  getAdminQuickCheck,
  getAdminRetention,
  getAdminShowConfig,
  getAdminStoragePermissions,
  getAdminStorageTiers,
  getAdminStorageUsage,
  getDataPackageContents,
  getMaintenanceSchedules,
  listDataPackages,
  migrateAdminStorage,
  runAdminMaintenance,
  runAdminSelftest,
  runMaintenanceSchedule,
  validateDataPackage
} from "@/lib/api";
import { describeApiError } from "@/lib/api-errors";
import { cn } from "@/lib/utils";
import type {
  AdminCleanupPreviewResponse,
  AdminErrorCodesResponse,
  AdminMaintenanceHistoryResponse,
  AdminMaintenanceScheduleResponse,
  AdminQuickCheckResponse,
  AdminRetentionResponse,
  AdminSelfTestResponse,
  AdminShowConfigResponse,
  AdminStoragePermissionsResponse,
  AdminStorageTiersResponse,
  AdminStorageUsageResponse,
  ArchiveMaintenanceSchedule,
  DataPackageContentsResponse,
  DataPackageCreateRequest,
  DataPackageListResponse,
  DataPackageResult,
  DataPackageValidationResponse,
  MaintenanceExecution,
  MaintenanceSchedulesResponse
} from "@/types";

interface SettingsAdminOperationsConsoleProps {
  active: boolean;
  operatorLabel?: string | null;
  onRefresh?: () => Promise<void> | void;
}

type CommandKey = "health" | "full" | "retention" | "cleanup" | "selftest" | "migrate" | "schedule" | "toggle" | "create-package" | "validate-package" | "contents-package";
type MessageTone = "default" | "success" | "warning" | "danger";

interface CommandMessage {
  tone: MessageTone;
  title: string;
  detail?: string | null;
}

interface Snapshot {
  loadedAt: string;
  adminSchedule: AdminMaintenanceScheduleResponse | null;
  adminHistory: AdminMaintenanceHistoryResponse | null;
  storageTiers: AdminStorageTiersResponse | null;
  storageUsage: AdminStorageUsageResponse | null;
  retention: AdminRetentionResponse | null;
  cleanupPreview: AdminCleanupPreviewResponse | null;
  permissions: AdminStoragePermissionsResponse | null;
  quickCheck: AdminQuickCheckResponse | null;
  config: AdminShowConfigResponse | null;
  errorCodes: AdminErrorCodesResponse | null;
  maintenanceSchedules: MaintenanceSchedulesResponse | null;
  packages: DataPackageListResponse | null;
  failures: string[];
}

interface PackageFormState {
  name: string;
  outputDirectory: string;
  symbols: string;
  eventTypes: string;
  startDate: string;
  endDate: string;
  packagePath: string;
  includeQualityReport: boolean;
  includeDataDictionary: boolean;
  verifyChecksums: boolean;
}

const emptyPackageForm: PackageFormState = {
  name: "browser-operations-package",
  outputDirectory: "",
  symbols: "SPY, QQQ",
  eventTypes: "MarketData, Trading",
  startDate: "",
  endDate: "",
  packagePath: "",
  includeQualityReport: true,
  includeDataDictionary: true,
  verifyChecksums: true
};

const messageToneClass: Record<MessageTone, string> = {
  default: "border-border/70 bg-secondary/25",
  success: "border-success/30 bg-success/10",
  warning: "border-warning/35 bg-warning/10",
  danger: "border-danger/35 bg-danger/10"
};

async function settle<T>(label: string, request: Promise<T>) {
  try {
    return { label, value: await request as unknown, error: null as string | null };
  } catch (error) {
    return { label, value: null, error: `${label}: ${describeApiError(error)}` };
  }
}

function pick<T>(results: Array<{ label: string; value: unknown }>, label: string): T | null {
  return (results.find((result) => result.label === label)?.value as T | null) ?? null;
}

function count(value: number | null | undefined): string {
  return typeof value === "number" && Number.isFinite(value) ? value.toLocaleString() : "--";
}

function bytes(value: number | null | undefined): string {
  if (typeof value !== "number" || !Number.isFinite(value)) {
    return "--";
  }
  const units = ["B", "KB", "MB", "GB", "TB"];
  let scaled = value;
  let index = 0;
  while (scaled >= 1024 && index < units.length - 1) {
    scaled /= 1024;
    index += 1;
  }
  return `${scaled.toLocaleString(undefined, { maximumFractionDigits: scaled >= 10 ? 0 : 1 })} ${units[index]}`;
}

function dateTime(value: string | null | undefined): string {
  if (!value) {
    return "--";
  }
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? value : parsed.toLocaleString(undefined, { month: "short", day: "numeric", hour: "numeric", minute: "2-digit" });
}

function csv(value: string): string[] {
  return value.split(",").map((part) => part.trim()).filter(Boolean);
}

function scheduleId(schedule: ArchiveMaintenanceSchedule): string {
  return schedule.scheduleId || schedule.name;
}

function latestRun(executions: MaintenanceExecution[]): MaintenanceExecution | null {
  return executions.slice().sort((left, right) => Date.parse(right.startedAt || "") - Date.parse(left.startedAt || ""))[0] ?? null;
}

function runDetail(execution: MaintenanceExecution): string {
  return [
    execution.executionId ? `Run ${execution.executionId}` : null,
    execution.status ? `Status ${execution.status}` : null,
    typeof execution.filesProcessed === "number" ? `${execution.filesProcessed.toLocaleString()} files` : null,
    typeof execution.issuesFound === "number" ? `${execution.issuesFound.toLocaleString()} issues` : null
  ].filter(Boolean).join("; ");
}

function packageRequest(form: PackageFormState): DataPackageCreateRequest {
  return {
    name: form.name.trim() || null,
    outputDirectory: form.outputDirectory.trim() || null,
    symbols: csv(form.symbols),
    eventTypes: csv(form.eventTypes),
    startDate: form.startDate || null,
    endDate: form.endDate || null,
    format: "zip",
    compressionLevel: "balanced",
    includeQualityReport: form.includeQualityReport,
    includeDataDictionary: form.includeDataDictionary,
    includeLoaderScripts: true,
    verifyChecksums: form.verifyChecksums,
    tags: ["browser-workstation", "admin-operations"]
  };
}
export function SettingsAdminOperationsConsole({ active, operatorLabel, onRefresh }: SettingsAdminOperationsConsoleProps) {
  const [snapshot, setSnapshot] = useState<Snapshot | null>(null);
  const [loading, setLoading] = useState(false);
  const [loaded, setLoaded] = useState(false);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [busyCommand, setBusyCommand] = useState<CommandKey | null>(null);
  const [busyScheduleId, setBusyScheduleId] = useState<string | null>(null);
  const [message, setMessage] = useState<CommandMessage | null>(null);
  const [retentionConfirmed, setRetentionConfirmed] = useState(false);
  const [cleanupConfirmed, setCleanupConfirmed] = useState(false);
  const [targetTier, setTargetTier] = useState("Archive");
  const [packageForm, setPackageForm] = useState<PackageFormState>(emptyPackageForm);
  const [packageResult, setPackageResult] = useState<DataPackageResult | DataPackageValidationResponse | DataPackageContentsResponse | null>(null);

  const loadSnapshot = useCallback(async () => {
    setLoading(true);
    setLoadError(null);
    const results = await Promise.all([
      settle("Admin maintenance schedule", getAdminMaintenanceSchedule()),
      settle("Admin maintenance history", getAdminMaintenanceHistory(10)),
      settle("Storage tiers", getAdminStorageTiers()),
      settle("Storage usage", getAdminStorageUsage()),
      settle("Retention policy", getAdminRetention()),
      settle("Cleanup preview", getAdminCleanupPreview()),
      settle("Storage permissions", getAdminStoragePermissions()),
      settle("Admin quick check", getAdminQuickCheck()),
      settle("Admin config", getAdminShowConfig()),
      settle("Admin error codes", getAdminErrorCodes()),
      settle("Maintenance schedules", getMaintenanceSchedules()),
      settle("Data packages", listDataPackages())
    ]);
    const failures = results.map((result) => result.error).filter((error): error is string => Boolean(error));
    setSnapshot({
      loadedAt: new Date().toISOString(),
      adminSchedule: pick<AdminMaintenanceScheduleResponse>(results, "Admin maintenance schedule"),
      adminHistory: pick<AdminMaintenanceHistoryResponse>(results, "Admin maintenance history"),
      storageTiers: pick<AdminStorageTiersResponse>(results, "Storage tiers"),
      storageUsage: pick<AdminStorageUsageResponse>(results, "Storage usage"),
      retention: pick<AdminRetentionResponse>(results, "Retention policy"),
      cleanupPreview: pick<AdminCleanupPreviewResponse>(results, "Cleanup preview"),
      permissions: pick<AdminStoragePermissionsResponse>(results, "Storage permissions"),
      quickCheck: pick<AdminQuickCheckResponse>(results, "Admin quick check"),
      config: pick<AdminShowConfigResponse>(results, "Admin config"),
      errorCodes: pick<AdminErrorCodesResponse>(results, "Admin error codes"),
      maintenanceSchedules: pick<MaintenanceSchedulesResponse>(results, "Maintenance schedules"),
      packages: pick<DataPackageListResponse>(results, "Data packages"),
      failures
    });
    setLoaded(true);
    setLoadError(failures.length ? `${failures.length} admin endpoint${failures.length === 1 ? "" : "s"} failed.` : null);
    setLoading(false);
  }, []);

  useEffect(() => {
    if (active && !loaded && !loading) {
      void loadSnapshot();
    }
  }, [active, loaded, loading, loadSnapshot]);

  const schedules = useMemo(() => {
    const byId = new Map<string, ArchiveMaintenanceSchedule>();
    for (const schedule of [...(snapshot?.adminSchedule?.schedules ?? []), ...(snapshot?.maintenanceSchedules?.schedules ?? [])]) {
      byId.set(scheduleId(schedule), schedule);
    }
    return [...byId.values()];
  }, [snapshot]);

  async function runCommand<T>(key: CommandKey, action: () => Promise<T>, toMessage: (result: T) => CommandMessage, schedule?: string) {
    setBusyCommand(key);
    setBusyScheduleId(schedule ?? null);
    setMessage(null);
    try {
      const result = await action();
      setMessage(toMessage(result));
      await loadSnapshot();
      await onRefresh?.();
    } catch (error) {
      setMessage({ tone: "danger", title: "Command failed", detail: describeApiError(error) });
    } finally {
      setBusyCommand(null);
      setBusyScheduleId(null);
    }
  }

  const latest = latestRun(snapshot?.adminHistory?.executions ?? []);
  const enabledSchedules = schedules.filter((schedule) => schedule.enabled).length;
  const packages = snapshot?.packages?.packages ?? [];
  const tierRows = Object.entries(snapshot?.storageTiers?.tiers ?? {});
  const permissions = snapshot?.permissions;
  const permissionLabel = permissions ? permissions.readable && permissions.writable ? "Writable" : permissions.readable ? "Read only" : "Blocked" : "Unknown";
  const disableOther = (key: CommandKey) => busyCommand !== null && busyCommand !== key;

  return (
    <Card id="admin-operations-console" className="panel-surface scroll-mt-6 border border-border/70">
      <CardHeader>
        <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
          <div>
            <div className="eyebrow-label">Admin maintenance</div>
            <CardTitle className="mt-2 flex items-center gap-2 text-base"><Wrench className="h-4 w-4 text-primary" />Admin operations console</CardTitle>
            <CardDescription>Maintenance, archive storage, retention, diagnostics, schedules, and package handoff for {operatorLabel ?? "the workstation operator"}.</CardDescription>
          </div>
          <div className="flex flex-wrap items-center gap-2">
            <Badge variant={loaded ? snapshot?.failures.length ? "warning" : "success" : "secondary"}>{loading ? "Checking" : loaded ? snapshot?.failures.length ? "Degraded" : "Loaded" : "Not loaded"}</Badge>
            <Button type="button" variant="outline" size="sm" busy={loading} busyLabel="Refreshing admin operations" onClick={() => void loadSnapshot()}><RefreshCcw className="h-4 w-4" />Refresh</Button>
          </div>
        </div>
      </CardHeader>
      <CardContent className="grid gap-5">
        {!active && !loaded ? <StatusBanner tone="info" title="Admin operations snapshot is idle" detail="Use the Admin Ops task tab or Refresh to load live maintenance, storage, retention, diagnostics, schedule, and package evidence." /> : null}
        {loadError ? <StatusBanner tone="warning" title={loadError} detail={snapshot?.failures.slice(0, 3).join(" | ")} /> : null}
        {message ? <div className={cn("rounded-md border px-3 py-2 text-sm", messageToneClass[message.tone])}><div className="font-semibold text-foreground">{message.title}</div>{message.detail ? <div className="mt-1 font-mono text-xs leading-5 text-muted-foreground">{message.detail}</div> : null}</div> : null}

        <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
          <MetricTile icon={<Wrench className="h-4 w-4" />} label="Schedules" value={`${enabledSchedules}/${schedules.length}`} detail="enabled maintenance jobs" />
          <MetricTile icon={<HardDrive className="h-4 w-4" />} label="Archive storage" value={bytes(snapshot?.storageUsage?.totalBytes)} detail={`${count(snapshot?.storageUsage?.fileCount)} files`} />
          <MetricTile icon={<ShieldCheck className="h-4 w-4" />} label="Permissions" value={permissionLabel} detail={snapshot?.permissions?.rootPath ?? snapshot?.storageUsage?.rootPath ?? "storage root pending"} />
          <MetricTile icon={<PackageCheck className="h-4 w-4" />} label="Packages" value={count(packages.length)} detail={`error codes ${count(snapshot?.errorCodes?.errorCodes.length)}`} />
        </div>

        <div className="grid gap-4 xl:grid-cols-[minmax(0,1.15fr)_minmax(0,0.85fr)]">
          <Panel icon={<Play className="h-4 w-4" />} title="Maintenance run control" detail="Manual run commands share the admin maintenance execution endpoint.">
            <div className="grid gap-3 md:grid-cols-3">
              <EvidenceLine label="Latest run" value={latest?.status ?? "No run loaded"} detail={latest ? `${latest.taskType} at ${dateTime(latest.startedAt)}` : "history endpoint pending"} />
              <EvidenceLine label="Files processed" value={count(latest?.filesProcessed)} detail={latest?.executionId ?? "--"} />
              <EvidenceLine label="Issues" value={count(latest?.issuesFound)} detail={typeof latest?.issuesResolved === "number" ? `${latest.issuesResolved.toLocaleString()} resolved` : "--"} />
            </div>
            <div className="mt-4 flex flex-wrap gap-2">
              <Button type="button" variant="outline" size="sm" busy={busyCommand === "health"} disabled={disableOther("health")} onClick={() => void runCommand("health", () => runAdminMaintenance({ taskType: "HealthCheck" }), (run) => ({ tone: "success", title: "Health check started", detail: runDetail(run) }))}><Play className="h-4 w-4" />Health check</Button>
              <Button type="button" variant="outline" size="sm" busy={busyCommand === "full"} disabled={disableOther("full")} onClick={() => void runCommand("full", () => runAdminMaintenance({ taskType: "FullMaintenance" }), (run) => ({ tone: "success", title: "Full maintenance started", detail: runDetail(run) }))}><Wrench className="h-4 w-4" />Full maintenance</Button>
            </div>
          </Panel>

          <Panel icon={<Database className="h-4 w-4" />} title="Storage tiering" detail="Tier usage, root permissions, and migration planning.">
            <div className="grid gap-2">
              {tierRows.length ? tierRows.map(([tier, info]) => <div key={tier} className="grid grid-cols-[minmax(0,1fr)_auto] gap-3 rounded-md border border-border/60 bg-secondary/20 px-3 py-2 text-sm"><div><div className="font-semibold text-foreground">{tier}</div><div className="text-xs text-muted-foreground">{count(info.fileCount)} files</div></div><div className="font-mono text-xs text-muted-foreground">{bytes(info.totalBytes)}</div></div>) : <EmptyState label="No tier statistics loaded" />}
            </div>
            <div className="mt-4 grid gap-2 sm:grid-cols-[minmax(0,1fr)_auto]">
              <label className="grid gap-1 text-xs font-medium text-muted-foreground">Target tier<Input value={targetTier} onChange={(event) => setTargetTier(event.target.value)} placeholder="Archive" /></label>
              <Button type="button" variant="outline" size="sm" className="self-end" busy={busyCommand === "migrate"} disabled={disableOther("migrate") || !targetTier.trim()} onClick={() => void runCommand("migrate", () => migrateAdminStorage(targetTier.trim()), (result) => ({ tone: "success", title: "Storage migration plan requested", detail: `Target ${result.targetTier}` }))}><Archive className="h-4 w-4" />Plan migration</Button>
            </div>
          </Panel>
        </div>