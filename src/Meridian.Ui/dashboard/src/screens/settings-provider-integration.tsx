import { FixedIncomePaymentPreview } from "@/components/data/fixed-income-payment-preview";
import { DetailFieldItem } from "@/components/meridian/detail-pane";
import { BondImportValuePreview } from "./bond-import-value-preview";
import { ProviderIdentityEvidence } from "@/components/data/provider-identity-evidence";
import {
  Activity,
  GitBranch,
  LoaderCircle,
  MonitorCheck,
  RefreshCcw,
  Save,
  ShieldCheck,
  Trash2
} from "lucide-react";
import { formatCount, formatUtcTimestamp } from "@/lib/format";
import { useEffect, useState } from "react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import { TextArea } from "@/components/ui/text-area";
import { SeverityBadge } from "@/components/operations";
import {
  activateProviderIntegration,
  checkProviderIntegrationSchemaDrift,
  getProviderIntegrationReadiness,
  getProviderIntegrationTemplate,
  getProviderIntegrationTemplates,
  runManualCsvProviderIntegrationDryRun,
  runRestProviderIntegrationDryRun,
  saveProviderIntegrationSetup
} from "@/lib/api";
import { describeApiError } from "@/lib/api-errors";
import { formatProviderIntegrationSetupDraftIssues, validateProviderIntegrationSetupDraft } from "@/lib/provider-integration-setup-validation";
import {
  parseProviderIntegrationStringRecord,
  parseProviderIntegrationWorkbenchJson,
  providerIntegrationCredentialReference,
  providerIntegrationFormatJson,
  providerIntegrationNormalizedId,
  providerIntegrationReadinessDetails,
  providerIntegrationSampleCsv,
  providerIntegrationWorkbenchEvidenceId,
  providerIntegrationWorkbenchSyncRunId
} from "@/lib/provider-integration-workbench";
import { cn } from "@/lib/utils";
import { SettingsMutationConfirmDialog } from "@/screens/settings-mutation-confirm-dialog";
import { type SettingsProviderConnectionRow } from "@/screens/settings-screen.view-model";
import type {
  ProviderIntegrationActivationReadiness,
  ProviderIntegrationActivationResult,
  ProviderIntegrationAuthType,
  ProviderIntegrationCapabilityKind,
  ProviderIntegrationConnection,
  ProviderIntegrationConnectionMonitor,
  ProviderIntegrationDryRunResult,
  ProviderIntegrationEndpointDefinition,
  ProviderIntegrationFieldMapping,
  ProviderIntegrationManifest,
  ProviderIntegrationManifestReference,
  ProviderIntegrationOpenApiImportResult,
  ProviderIntegrationProcessingStatus,
  ProviderIntegrationPromotionReadinessPreview,
  ProviderIntegrationQuarantineDecision,
  ProviderIntegrationQuarantinedRecord,
  ProviderIntegrationQuarantineResolutionAction,
  ProviderIntegrationQuarantineReview,
  ProviderIntegrationReconciliationHandoffHistory,
  ProviderIntegrationSchemaDriftCheckResult,
  ProviderIntegrationSetupSaveResult,
  ProviderIntegrationStagingIdentityResolutionPreview,
  ProviderIntegrationStagingReview,
  ProviderIntegrationSyncPlan,
  ProviderIntegrationSyncRunEvidence,
  ProviderIntegrationSyncRunHistory,
  ProviderIntegrationTemplateCatalogEntry
} from "@/types";
import { toSeverity } from "@/design-system/status-styles";

// Domain-specific projections and components shared by the screen composition.


export type ProviderRuntimePhase = "idle" | "loading" | "loaded" | "error";


export interface ProviderRuntimeEvidenceState {
  phase: ProviderRuntimePhase;
  message: string | null;
  details: string[];
  monitor: ProviderIntegrationConnectionMonitor | null;
  syncRuns: ProviderIntegrationSyncRunHistory | null;
  syncPlan: ProviderIntegrationSyncPlan | null;
  staging: ProviderIntegrationStagingReview | null;
  identity: ProviderIntegrationStagingIdentityResolutionPreview | null;
  promotion: ProviderIntegrationPromotionReadinessPreview | null;
  handoff: ProviderIntegrationReconciliationHandoffHistory | null;
  quarantine: ProviderIntegrationQuarantineReview | null;
}


export interface ProviderOpenApiImportState {
  manifestId: string;
  displayName: string;
  environment: string;
  authType: ProviderIntegrationAuthType;
  tokenUrl: string;
  scopes: string;
  capabilities: string;
  openApiDocumentJson: string;
  changeReason: string;
  busy: boolean;
  result: ProviderIntegrationOpenApiImportResult | null;
  message: string | null;
  details: string[];
  tone: "default" | "success" | "danger" | "warning";
}

export type ProviderIntegrationWorkbenchBusyAction =
  | "activate"
  | "csv-dry-run"
  | "drift"
  | "readiness"
  | "rest-dry-run"
  | "save"
  | "template"
  | "templates"
  | null;


export interface ProviderIntegrationWorkbenchState {
  templates: ProviderIntegrationTemplateCatalogEntry[] | null;
  selectedManifestId: string;
  manifest: ProviderIntegrationManifest | null;
  manifestReference: ProviderIntegrationManifestReference | null;
  connection: ProviderIntegrationConnection | null;
  draftManifestJson: string;
  draftConnectionJson: string;
  capability: ProviderIntegrationCapabilityKind;
  endpointKey: string;
  csvFileName: string;
  csvContent: string;
  restPathParametersJson: string;
  restQueryParametersJson: string;
  readiness: ProviderIntegrationActivationReadiness | null;
  setupResult: ProviderIntegrationSetupSaveResult | null;
  dryRunResult: ProviderIntegrationDryRunResult | null;
  driftResult: ProviderIntegrationSchemaDriftCheckResult | null;
  activationResult: ProviderIntegrationActivationResult | null;
  busyAction: ProviderIntegrationWorkbenchBusyAction;
  message: string | null;
  details: string[];
  tone: "default" | "success" | "danger" | "warning";
}


export const itemToneClass = {
  default: "text-foreground",
  success: "text-success",
  warning: "text-warning",
  danger: "text-danger",
  muted: "text-muted-foreground"
} as const;


export const diagnosticToneClass = {
  default: "border-border/70 bg-secondary/30",
  success: "border-success/30 bg-success/10",
  warning: "border-warning/35 bg-warning/10",
  danger: "border-danger/35 bg-danger/10"
} as const;


export function SettingsFieldRow({
  label,
  value,
  tone
}: {
  label: string;
  value: string;
  tone: keyof typeof itemToneClass;
}) {
  return (
    <DetailFieldItem label={label} value={value} tone={tone} />
  );
}


export function FilterSelect({
  label,
  ariaLabel,
  value,
  onChange,
  options,
  disabled = false
}: {
  label: string;
  ariaLabel?: string;
  value: string;
  onChange: (value: string) => void;
  options: Array<{ value: string; label: string }>;
  disabled?: boolean;
}) {
  return (
    <label className="grid gap-1 text-xs font-medium text-muted-foreground">
      {label}
      <Select
        value={value}
        onChange={(event) => onChange(event.target.value)}
        disabled={disabled}
        className="h-9 text-sm"
        aria-label={ariaLabel ?? label}
      >
        {options.map((option) => (
          <option key={option.value} value={option.value}>
            {option.label}
          </option>
        ))}
      </Select>
    </label>
  );
}


export function ProviderIntegrationRuntimePanel({
  row,
  state,
  operatorName,
  openApiImportState,
  onOpenApiImportStateChange,
  onImportOpenApi,
  onLoad,
  onRunDueSync,
  onCreateHandoff,
  onReplayQuarantine,
  onResolveQuarantineRecord
}: {
  row: SettingsProviderConnectionRow;
  state: ProviderRuntimeEvidenceState;
  operatorName: string;
  openApiImportState: ProviderOpenApiImportState;
  onOpenApiImportStateChange: (updater: (state: ProviderOpenApiImportState) => ProviderOpenApiImportState) => void;
  onImportOpenApi: () => void;
  onLoad: () => void;
  onRunDueSync: () => void;
  onCreateHandoff: () => void;
  onReplayQuarantine: () => void;
  onResolveQuarantineRecord: (
    record: ProviderIntegrationQuarantinedRecord,
    action: ProviderIntegrationQuarantineResolutionAction
  ) => void;
}) {
  const runs = providerRuntimeRuns(state);
  const latestRun = runs[0] ?? null;
  const issueGroups = state.quarantine?.issueGroups ?? [];
  const quarantineRecords = state.quarantine?.records ?? [];
  const quarantineDecisionCount = state.quarantine?.decisionedRecordCount ?? state.quarantine?.decisions.length ?? 0;
  const pendingReviewRecordCount = state.quarantine?.pendingReviewRecordCount ?? Math.max(quarantineRecords.length - quarantineDecisionCount, 0);
  const replayRequestedRecordCount = state.quarantine?.replayRequestedRecordCount ?? 0;
  const ignoredRecordCount = state.quarantine?.ignoredRecordCount ?? 0;
  const cashPositionCandidateCount = state.quarantine?.cashPositionCandidateCount ?? 0;
  const syncPlanItems = state.syncPlan?.items ?? [];
  const blockedSyncItems = state.syncPlan?.blockedCount ?? syncPlanItems.filter((item) => item.isBlocked).length;
  const dueSyncItems = state.syncPlan?.dueCount ?? syncPlanItems.filter((item) => item.isDue).length;
  const stagingReviewRows = state.staging?.records.length ?? 0;
  const identityReviewRequired = (state.identity?.accountReviewRequiredCount ?? 0) + (state.identity?.securityReviewRequiredCount ?? 0);
  const promotionReady = state.promotion?.readyForReconciliationCount ?? 0;
  const promotionBlocked = state.promotion?.blockedCount ?? 0;
  const handoffCount = state.handoff?.handoffCount ?? 0;
  const criticalIssueCount = providerRuntimeCriticalIssueCount(state);
  const warningIssueCount = providerRuntimeWarningIssueCount(state);
  const receivedCount = providerRuntimeReceivedCount(state, runs);
  const acceptedCount = providerRuntimeAcceptedCount(state, runs);
  const quarantinedCount = providerRuntimeQuarantinedCount(state, runs);
  const stagedCount = providerRuntimeStagedCount(state, runs);
  const durableQuarantinedCount = providerRuntimeDurableQuarantinedCount(state, runs);
  const totalRuns = state.syncRuns?.totalSyncRuns ?? runs.length;
  const returnedRuns = state.syncRuns?.returnedSyncRuns ?? runs.length;
  const loading = state.phase === "loading";
  const replayEligibleQuarantineRecords = quarantineRecords.filter((record) =>
    !providerRuntimeLatestQuarantineDecision(state.quarantine?.decisions, record)
  );
  const quarantineReplayCount = replayEligibleQuarantineRecords.length;
  const canReplayQuarantine = !loading && quarantineReplayCount > 0 && Boolean(state.monitor?.manifestId);
  const canRunDueSync = !loading && Boolean(state.syncPlan) && dueSyncItems > 0 && blockedSyncItems === 0;
  const alreadyHandedOff = new Set((state.handoff?.records ?? []).map((record) => record.stagingRecordId));
  const handoffReadyCount = (state.promotion?.rows ?? [])
    .filter((promotionRow) => promotionRow.status === "ReadyForReconciliation" && !alreadyHandedOff.has(promotionRow.stagingRecordId))
    .length;
  const canCreateHandoff = !loading && handoffReadyCount > 0;
  const actionLabel = state.phase === "loaded" || state.phase === "error" ? "Refresh runtime" : loading ? "Loading runtime" : "Load runtime";

  return (
    <section
      className={cn("mt-3 rounded-md border px-3 py-3", diagnosticToneClass[providerRuntimePanelTone(state)])}
      aria-label={`${row.displayName} provider integration runtime evidence`}
    >
      <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
        <div className="min-w-0">
          <div className="flex flex-wrap items-center gap-2">
            <h5 className="text-xs font-semibold uppercase tracking-[0.12em] text-foreground">Runtime evidence</h5>
            <SeverityBadge
              status={toSeverity(providerRuntimeStatusVariant(state))}
              label={providerRuntimeStatusLabel(state)}
            />
          </div>
          <p className="mt-1 break-all font-mono text-[11px] text-muted-foreground">{row.integrationConnectionId}</p>
        </div>
        <div className="flex flex-wrap gap-2">
          <Button
            type="button"
            variant="outline"
            size="sm"
            className="shrink-0"
            onClick={onRunDueSync}
            disabled={!canRunDueSync}
            disabledReason={!state.syncPlan ? "Load runtime evidence before running due sync." : blockedSyncItems > 0 ? "Blocked sync-plan items must be resolved before due sync." : dueSyncItems === 0 ? "No due sync-plan items are available." : undefined}
            aria-label={`Run due provider integration sync for ${row.displayName}`}
          >
            {loading ? <LoaderCircle className="h-3.5 w-3.5 animate-spin" aria-hidden="true" /> : <Activity className="h-3.5 w-3.5" aria-hidden="true" />}
            Run due sync
          </Button>
          <Button
            type="button"
            variant="outline"
            size="sm"
            className="shrink-0"
            onClick={onCreateHandoff}
            disabled={!canCreateHandoff}
            disabledReason={!state.promotion ? "Load promotion readiness before creating a handoff." : handoffReadyCount === 0 ? "No unhanded promotion-ready staging rows are available." : undefined}
            aria-label={`Create reconciliation handoff for ${handoffReadyCount} provider integration staging rows for ${row.displayName}`}
          >
            {loading ? <LoaderCircle className="h-3.5 w-3.5 animate-spin" aria-hidden="true" /> : <GitBranch className="h-3.5 w-3.5" aria-hidden="true" />}
            Hand off ready
          </Button>
          <Button
            type="button"
            variant="outline"
            size="sm"
            className="shrink-0"
            onClick={onReplayQuarantine}
            disabled={!canReplayQuarantine}
            aria-label={`Replay ${quarantineReplayCount} quarantined provider integration records for ${row.displayName}`}
          >
            {loading ? <LoaderCircle className="h-3.5 w-3.5 animate-spin" aria-hidden="true" /> : <RefreshCcw className="h-3.5 w-3.5" aria-hidden="true" />}
            Replay quarantine
          </Button>
          <Button
            type="button"
            variant="outline"
            size="sm"
            className="shrink-0"
            onClick={onLoad}
            disabled={loading}
            aria-label={`${state.phase === "loaded" || state.phase === "error" ? "Refresh" : "Load"} provider integration runtime evidence for ${row.displayName}`}
          >
            {loading ? <LoaderCircle className="h-3.5 w-3.5 animate-spin" aria-hidden="true" /> : <RefreshCcw className="h-3.5 w-3.5" aria-hidden="true" />}
            {actionLabel}
          </Button>
        </div>
      </div>
      <ProviderOpenApiImportForm
        row={row}
        state={openApiImportState}
        onStateChange={onOpenApiImportStateChange}
        onSubmit={onImportOpenApi}
      />
      <ProviderIntegrationWorkbenchPanel
        row={row}
        runtimeState={state}
        operatorName={operatorName}
      />
      <BondImportValuePreview stagedRecords={state.staging?.records ?? []} quarantinedRecords={state.quarantine?.records ?? []} />
      <dl className="mt-3 grid gap-2 sm:grid-cols-2">
        <SettingsFieldRow
          label="Last sync"
          value={formatProviderRuntimeUtcMinute(state.syncRuns?.latestStartedAt ?? latestRun?.startedAt)}
          tone={latestRun ? providerRuntimeRunTone(latestRun) : "muted"}
        />
        <SettingsFieldRow label="Sync runs" value={`${formatProviderRuntimeNumber(returnedRuns)} / ${formatProviderRuntimeNumber(totalRuns)}`} tone={returnedRuns > 0 ? "success" : "muted"} />
        <SettingsFieldRow label="Records received" value={formatProviderRuntimeNumber(receivedCount)} tone={receivedCount > 0 ? "success" : "muted"} />
        <SettingsFieldRow
          label="Accepted / quarantined"
          value={`${formatProviderRuntimeNumber(acceptedCount)} / ${formatProviderRuntimeNumber(quarantinedCount)}`}
          tone={quarantinedCount > 0 ? providerRuntimeIssueTone(criticalIssueCount, warningIssueCount) : acceptedCount > 0 ? "success" : "muted"}
        />
        <SettingsFieldRow label="Staged retained" value={formatProviderRuntimeNumber(stagedCount)} tone={stagedCount > 0 ? "success" : "muted"} />
        <SettingsFieldRow label="Quarantine retained" value={formatProviderRuntimeNumber(durableQuarantinedCount)} tone={durableQuarantinedCount > 0 ? providerRuntimeIssueTone(criticalIssueCount, warningIssueCount) : "muted"} />
        <SettingsFieldRow label="Decisioned records" value={formatProviderRuntimeNumber(quarantineDecisionCount)} tone={quarantineDecisionCount > 0 ? "success" : "muted"} />
        <SettingsFieldRow
          label="Review posture"
          value={`${formatProviderRuntimeNumber(pendingReviewRecordCount)} pending / ${formatProviderRuntimeNumber(replayRequestedRecordCount)} replay / ${formatProviderRuntimeNumber(ignoredRecordCount)} ignored / ${formatProviderRuntimeNumber(cashPositionCandidateCount)} cash`}
          tone={pendingReviewRecordCount > 0 ? "warning" : quarantineDecisionCount > 0 ? "success" : "muted"}
        />
        <SettingsFieldRow
          label="Quarantine groups"
          value={`${formatProviderRuntimeNumber(issueGroups.length)} groups`}
          tone={issueGroups.length > 0 ? providerRuntimeIssueTone(criticalIssueCount, warningIssueCount) : "muted"}
        />
        <SettingsFieldRow
          label="Issue counts"
          value={`${formatProviderRuntimeNumber(criticalIssueCount)} critical / ${formatProviderRuntimeNumber(warningIssueCount)} warning`}
          tone={providerRuntimeIssueTone(criticalIssueCount, warningIssueCount)}
        />
        <SettingsFieldRow
          label="Sync plan"
          value={`${formatProviderRuntimeNumber(dueSyncItems)} due / ${formatProviderRuntimeNumber(blockedSyncItems)} blocked`}
          tone={blockedSyncItems > 0 ? "danger" : dueSyncItems > 0 ? "warning" : state.syncPlan ? "success" : "muted"}
        />
        <SettingsFieldRow
          label="Staging review"
          value={`${formatProviderRuntimeNumber(stagingReviewRows)} rows`}
          tone={stagingReviewRows > 0 ? "success" : "muted"}
        />
        <SettingsFieldRow
          label="Identity review"
          value={`${formatProviderRuntimeNumber(identityReviewRequired)} review required`}
          tone={identityReviewRequired > 0 ? "warning" : state.identity ? "success" : "muted"}
        />
        <SettingsFieldRow
          label="Promotion readiness"
          value={`${formatProviderRuntimeNumber(promotionReady)} ready / ${formatProviderRuntimeNumber(promotionBlocked)} blocked`}
          tone={promotionBlocked > 0 ? "danger" : promotionReady > 0 ? "success" : state.promotion ? "muted" : "muted"}
        />
        <SettingsFieldRow
          label="Reconciliation handoffs"
          value={`${formatProviderRuntimeNumber(handoffCount)} handoffs`}
          tone={handoffCount > 0 ? "success" : "muted"}
        />
      </dl>
      {state.message ? (
        <div role={state.phase === "error" ? "alert" : "status"} className={cn("mt-3 text-xs", itemToneClass[providerRuntimeMessageTone(state)])}>
          <div>{state.message}</div>
          {state.details.length > 0 ? (
            <ul className="mt-1 list-disc space-y-1 pl-4 text-[11px] text-muted-foreground">
              {state.details.map((detail) => <li key={detail}>{detail}</li>)}
            </ul>
          ) : null}
        </div>
      ) : null}
      {state.phase === "idle" ? (
        <p className="mt-3 text-xs text-muted-foreground">No runtime evidence loaded.</p>
      ) : null}
      {runs.length > 0 ? (
        <div className="mt-3 grid gap-2" aria-label={`${row.displayName} recent provider integration sync runs`}>
          {runs.slice(0, 3).map((run) => (
            <div key={run.syncRunId} className="rounded-sm border border-border/60 bg-background/35 px-2 py-2">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <span className="font-mono text-[11px] text-foreground">{run.syncRunId}</span>
                <SeverityBadge status={toSeverity(providerRuntimeRunVariant(run))} label={run.status} />
              </div>
              <div className="mt-1 text-[11px] text-muted-foreground">
                {run.capability} · {formatProviderRuntimeUtcMinute(run.startedAt)} · {formatProviderRuntimeNumber(run.recordsAccepted)} accepted / {formatProviderRuntimeNumber(run.recordsQuarantined)} quarantined
              </div>
            </div>
          ))}
        </div>
      ) : null}
      {quarantineRecords.length > 0 ? (
        <div className="mt-3 grid gap-2" aria-label={`${row.displayName} provider integration quarantine records`}>
          {quarantineRecords.slice(0, 3).map((record) => {
            const latestDecision = providerRuntimeLatestQuarantineDecision(state.quarantine?.decisions, record);
            const hasRecordedDecision = Boolean(latestDecision);
            const supportsCashDecision = record.capability === "Positions";

            return (
              <div key={record.quarantineRecordId} className="rounded-sm border border-border/60 bg-background/35 px-2 py-2">
                <div className="flex flex-wrap items-start justify-between gap-2">
                  <div className="min-w-0">
                    <div className="flex flex-wrap items-center gap-2">
                      <SeverityBadge status={toSeverity(providerRuntimeProcessingStatusVariant(record.status))} label={record.status} />
                      <span className="font-mono text-[11px] text-foreground">{record.quarantineRecordId}</span>
                    </div>
                    <p className="mt-1 text-[11px] leading-4 text-muted-foreground">
                      {record.capability} · {formatProviderRuntimeUtcMinute(record.createdAt)} · {formatProviderRuntimeNumber(record.validationErrors.length)} issues
                    </p>
                    {latestDecision ? (
                      <p className="mt-1 text-[11px] leading-4 text-success">
                        Decision: {providerRuntimeQuarantineActionLabel(latestDecision.action)} by {latestDecision.reviewedBy} · {formatProviderRuntimeUtcMinute(latestDecision.reviewedAt)}
                      </p>
                    ) : null}
                  </div>
                  <div className="flex flex-wrap justify-end gap-2">
                    {hasRecordedDecision ? (
                      <Badge variant="success">Decision recorded</Badge>
                    ) : (
                      <>
                        <Button
                          type="button"
                          variant="outline"
                          size="sm"
                          className="shrink-0"
                          onClick={() => onResolveQuarantineRecord(record, "ReviewOnly")}
                          disabled={loading}
                          aria-label={`Review quarantine record ${record.quarantineRecordId} for ${row.displayName}`}
                        >
                          {loading ? <LoaderCircle className="h-3.5 w-3.5 animate-spin" aria-hidden="true" /> : <MonitorCheck className="h-3.5 w-3.5" aria-hidden="true" />}
                          Review
                        </Button>
                        <Button
                          type="button"
                          variant="outline"
                          size="sm"
                          className="shrink-0"
                          onClick={() => onResolveQuarantineRecord(record, "ReplayAfterMappingChange")}
                          disabled={loading}
                          aria-label={`Mark quarantine record ${record.quarantineRecordId} for replay after mapping change for ${row.displayName}`}
                        >
                          {loading ? <LoaderCircle className="h-3.5 w-3.5 animate-spin" aria-hidden="true" /> : <RefreshCcw className="h-3.5 w-3.5" aria-hidden="true" />}
                          Replay later
                        </Button>
                        <Button
                          type="button"
                          variant="outline"
                          size="sm"
                          className="shrink-0"
                          onClick={() => onResolveQuarantineRecord(record, "IgnoreProviderRecord")}
                          disabled={loading}
                          aria-label={`Ignore quarantine record ${record.quarantineRecordId} for ${row.displayName}`}
                        >
                          {loading ? <LoaderCircle className="h-3.5 w-3.5 animate-spin" aria-hidden="true" /> : <Trash2 className="h-3.5 w-3.5" aria-hidden="true" />}
                          Ignore
                        </Button>
                        {supportsCashDecision ? (
                          <Button
                            type="button"
                            variant="outline"
                            size="sm"
                            className="shrink-0"
                            onClick={() => onResolveQuarantineRecord(record, "MarkAsCashPosition")}
                            disabled={loading}
                            aria-label={`Mark quarantine record ${record.quarantineRecordId} as cash position for ${row.displayName}`}
                          >
                            {loading ? <LoaderCircle className="h-3.5 w-3.5 animate-spin" aria-hidden="true" /> : <ShieldCheck className="h-3.5 w-3.5" aria-hidden="true" />}
                            Mark cash
                          </Button>
                        ) : null}
                      </>
                    )}
                  </div>
                </div>
                {record.validationErrors[0] ? (
                  <p className="mt-1 text-[11px] leading-4 text-muted-foreground">{record.validationErrors[0].message}</p>
                ) : null}
              </div>
            );
          })}
        </div>
      ) : null}
      {issueGroups.length > 0 ? (
        <div className="mt-3 grid gap-2" aria-label={`${row.displayName} provider integration quarantine issue groups`}>
          {issueGroups.slice(0, 3).map((group) => (
            <div key={`${group.issueCode}-${group.targetField ?? "record"}`} className="rounded-sm border border-border/60 bg-background/35 px-2 py-2">
              <div className="flex flex-wrap items-center gap-2">
                <SeverityBadge status={toSeverity(providerRuntimeSeverityVariant(group.severity))} label={group.severity} />
                <span className="font-mono text-[11px] text-foreground">{group.issueCode}</span>
                <span className="text-[11px] text-muted-foreground">{formatProviderRuntimeNumber(group.recordCount)} records</span>
              </div>
              <p className="mt-1 text-[11px] leading-4 text-muted-foreground">{group.message}</p>
            </div>
          ))}
        </div>
      ) : null}
      {syncPlanItems.length > 0 ? (
        <div className="mt-3 grid gap-2" aria-label={`${row.displayName} provider integration sync plan`}>
          {syncPlanItems.slice(0, 3).map((item) => (
            <div key={`${item.capability}-${item.endpointKey ?? "no-service"}`} className="rounded-sm border border-border/60 bg-background/35 px-2 py-2">
              <div className="flex flex-wrap items-center gap-2">
                <SeverityBadge
                  status={item.isBlocked ? "blocked" : item.isDue ? "action" : "ready"}
                  label={item.isBlocked ? "Blocked" : item.isDue ? "Due" : "Scheduled"}
                />
                <span className="text-[11px] font-medium text-foreground">{item.capability}</span>
                <span className="font-mono text-[11px] text-muted-foreground">{item.endpointKey ?? "no service"}</span>
              </div>
              <p className="mt-1 text-[11px] leading-4 text-muted-foreground">{item.reason}</p>
            </div>
          ))}
        </div>
      ) : null}
      {state.identity ? <ProviderIdentityEvidence preview={state.identity} onResolved={onLoad} /> : null}
      {state.promotion && state.promotion.rows.length > 0 ? (
        <div className="mt-3 grid gap-2" aria-label={`${row.displayName} provider integration promotion readiness rows`}>
          {state.promotion.rows.slice(0, 3).map((promotionRow) => (
            <div key={promotionRow.stagingRecordId} className="rounded-sm border border-border/60 bg-background/35 px-2 py-2">
              <div className="flex flex-wrap items-center gap-2">
                <SeverityBadge status={toSeverity(providerRuntimePromotionVariant(promotionRow.status))} label={promotionRow.status} />
                <span className="font-mono text-[11px] text-foreground">{promotionRow.stagingRecordId}</span>
                <span className="text-[11px] text-muted-foreground">{promotionRow.promotionTarget}</span>
              </div>
              <p className="mt-1 text-[11px] leading-4 text-muted-foreground">
                {promotionRow.providerAccountId ?? "No provider account"} · {promotionRow.securityDisplayName ?? promotionRow.internalSecurityId ?? "No security match"}
              </p>
            </div>
          ))}
        </div>
      ) : null}
    </section>
  );
}


export function ProviderIntegrationWorkbenchPanel({
  row,
  runtimeState,
  operatorName
}: {
  row: SettingsProviderConnectionRow;
  runtimeState: ProviderRuntimeEvidenceState;
  operatorName: string;
}) {
  const [state, setState] = useState<ProviderIntegrationWorkbenchState>(() => createProviderIntegrationWorkbenchState(row));
  const [activationConfirmationOpen, setActivationConfirmationOpen] = useState(false);

  useEffect(() => {
    setState(createProviderIntegrationWorkbenchState(row));
  }, [row.integrationConnectionId, row.providerId]);

  const busy = state.busyAction !== null;
  const manifestId = providerIntegrationWorkbenchManifestId(state, runtimeState);
  const connectionId = providerIntegrationWorkbenchConnectionId(row, state, runtimeState);
  const capabilityOptions = providerIntegrationWorkbenchCapabilities(state);
  const endpointOptions = providerIntegrationWorkbenchEndpoints(state, state.capability);
  const mappingPreview = providerIntegrationWorkbenchMappings(state, state.capability);
  const latestRawPayload = providerIntegrationLatestRawPayload(state, runtimeState);
  const canCheckDrift = Boolean(manifestId && connectionId && latestRawPayload && state.endpointKey.trim());
  const activationReady = Boolean(state.readiness?.isReady && state.manifestReference?.manifestId === manifestId);

  const updateField = <K extends keyof ProviderIntegrationWorkbenchState>(
    field: K,
    value: ProviderIntegrationWorkbenchState[K]
  ) => {
    setState((current) => ({
      ...current,
      [field]: value,
      message: null,
      details: [],
      tone: "default"
    }));
  };

  const loadTemplates = async () => {
    setState((current) => ({ ...current, busyAction: "templates", message: "Loading provider integration templates.", details: [], tone: "default" }));
    try {
      const templates = await getProviderIntegrationTemplates();
      setState((current) => ({
        ...current,
        templates,
        selectedManifestId: providerIntegrationDefaultSelectedManifestId(row, templates, current.selectedManifestId),
        busyAction: null,
        message: templates.length === 0 ? "No provider integration templates returned." : `${templates.length} provider integration templates loaded.`,
        details: templates.slice(0, 3).map((template) => `${template.displayName}: ${template.summary}`),
        tone: templates.length === 0 ? "warning" : "success"
      }));
    } catch (error) {
      const display = describeApiError(error, "Provider integration templates could not be loaded.");
      setState((current) => ({ ...current, busyAction: null, message: display.summary, details: display.details, tone: "danger" }));
    }
  };

  const applyTemplate = async () => {
    const selectedManifestId = state.selectedManifestId.trim();
    if (!selectedManifestId) {
      setState((current) => ({ ...current, message: "Select or enter a manifest id before loading a template.", details: [], tone: "warning" }));
      return;
    }

    setState((current) => ({ ...current, busyAction: "template", message: `Loading template ${selectedManifestId}.`, details: [], tone: "default" }));
    try {
      const manifest = await getProviderIntegrationTemplate(selectedManifestId);
      const connection = createProviderIntegrationConnectionDraft(row, manifest, operatorName);
      setState((current) => providerIntegrationWorkbenchWithDraft(row, current, manifest, connection, {
        message: `Template ${manifest.manifestId} loaded into draft setup editor.`,
        details: providerIntegrationWorkbenchDraftDetails(manifest),
        tone: "success"
      }));
    } catch (error) {
      const display = describeApiError(error, "Provider integration template could not be loaded.");
      setState((current) => ({ ...current, busyAction: null, message: display.summary, details: display.details, tone: "danger" }));
    }
  };

  const saveSetupDraft = async () => {
    const manifestDraft = parseProviderIntegrationWorkbenchJson<ProviderIntegrationManifest>(state.draftManifestJson, "Manifest draft JSON");
    const connectionDraft = parseProviderIntegrationWorkbenchJson<ProviderIntegrationConnection>(state.draftConnectionJson, "Connection draft JSON");
    if (manifestDraft.ok === false || connectionDraft.ok === false) {
      const details = [
        manifestDraft.ok === false ? manifestDraft.error : null,
        connectionDraft.ok === false ? connectionDraft.error : null
      ].filter((detail): detail is string => Boolean(detail));
      setState((current) => ({
        ...current,
        message: "Provider integration setup draft is not valid JSON.",
        details,
        tone: "warning"
      }));
      return;
    }

    const validationIssues = validateProviderIntegrationSetupDraft(manifestDraft.value, connectionDraft.value);
    if (validationIssues.length > 0) {
      setState((current) => ({
        ...current,
        message: "Provider integration setup draft failed validation. Fix the reported fields and save again.",
        details: formatProviderIntegrationSetupDraftIssues(validationIssues),
        tone: "warning"
      }));
      return;
    }

    const savedAt = new Date().toISOString();
    setState((current) => ({ ...current, busyAction: "save", message: "Saving provider integration setup draft.", details: [], tone: "default" }));
    try {
      const result = await saveProviderIntegrationSetup({
        manifest: manifestDraft.value,
        connection: connectionDraft.value,
        savedBy: operatorName,
        savedAt,
        expectedManifestReference: state.manifestReference?.manifestId === manifestDraft.value.manifestId
          ? state.manifestReference
          : undefined,
        changeReason: manifestDraft.value.changeReason ?? "Saved from the Settings Provider Connection Center guided workbench."
      });
      const savedManifest = {
        ...manifestDraft.value,
        manifestVersion: result.manifestReference?.manifestVersion ?? manifestDraft.value.manifestVersion,
        state: result.manifestState
      };
      const savedConnection = { ...connectionDraft.value, state: result.connectionState, updatedAt: savedAt };
      setState((current) => ({
        ...current,
        manifest: savedManifest,
        manifestReference: result.manifestReference ?? null,
        connection: savedConnection,
        draftManifestJson: providerIntegrationFormatJson(savedManifest),
        draftConnectionJson: providerIntegrationFormatJson(savedConnection),
        selectedManifestId: result.manifestId,
        setupResult: result,
        readiness: result.readiness,
        busyAction: null,
        message: result.message ?? `Provider integration setup saved for ${result.connectionId}.`,
        details: providerIntegrationReadinessDetails(result.readiness),
        tone: result.readiness.isReady ? "success" : "warning"
      }));
    } catch (error) {
      const display = describeApiError(error, "Provider integration setup save failed.");
      setState((current) => ({ ...current, busyAction: null, message: display.summary, details: display.details, tone: "danger" }));
    }
  };

  const checkReadiness = async () => {
    if (!manifestId) {
      setState((current) => ({
        ...current,
        message: "Load a template, import a draft, or load runtime monitor evidence before checking activation readiness.",
        details: [],
        tone: "warning"
      }));
      return;
    }

    setState((current) => ({ ...current, busyAction: "readiness", message: "Checking provider integration activation readiness.", details: [], tone: "default" }));
    try {
      const readiness = await getProviderIntegrationReadiness(manifestId, connectionId);
      setState((current) => ({
        ...current,
        readiness,
        busyAction: null,
        message: readiness.isReady ? "Activation readiness passed." : "Activation readiness requires review.",
        details: providerIntegrationReadinessDetails(readiness),
        tone: readiness.isReady ? "success" : "warning"
      }));
    } catch (error) {
      const display = describeApiError(error, "Provider integration activation readiness could not be checked.");
      setState((current) => ({ ...current, busyAction: null, message: display.summary, details: display.details, tone: "danger" }));
    }
  };

  const runCsvDryRun = async () => {
    if (!manifestId || !state.csvContent.trim()) {
      setState((current) => ({ ...current, message: "Manifest id and CSV content are required before running a manual CSV dry-run.", details: [], tone: "warning" }));
      return;
    }

    const requestedAt = new Date();
    setState((current) => ({ ...current, busyAction: "csv-dry-run", message: "Running manual CSV provider integration dry-run.", details: [], tone: "default" }));
    try {
      const result = await runManualCsvProviderIntegrationDryRun({
        syncRunId: providerIntegrationWorkbenchSyncRunId(row.integrationConnectionId, "csv", requestedAt),
        manifestId,
        connectionId,
        capability: state.capability,
        fileName: state.csvFileName.trim() || `${row.providerId}-sample.csv`,
        csvContent: state.csvContent,
        requestedBy: operatorName,
        requestedAt: requestedAt.toISOString()
      });
      setState((current) => ({
        ...current,
        dryRunResult: result,
        busyAction: null,
        message: `CSV dry-run completed: ${result.recordsAccepted} accepted / ${result.recordsQuarantined} quarantined.`,
        details: result.issues.map((issue) => issue.message),
        tone: result.recordsQuarantined > 0 || result.issues.length > 0 ? "warning" : "success"
      }));
    } catch (error) {
      const display = describeApiError(error, "Manual CSV provider integration dry-run failed.");
      setState((current) => ({ ...current, busyAction: null, message: display.summary, details: display.details, tone: "danger" }));
    }
  };

  const runRestDryRun = async () => {
    const pathParameters = parseProviderIntegrationStringRecord(state.restPathParametersJson, "REST path parameters JSON");
    const queryParameters = parseProviderIntegrationStringRecord(state.restQueryParametersJson, "REST query parameters JSON");
    if (!manifestId || !state.endpointKey.trim() || pathParameters.ok === false || queryParameters.ok === false) {
      const details = [
        pathParameters.ok === false ? pathParameters.error : null,
        queryParameters.ok === false ? queryParameters.error : null
      ].filter((detail): detail is string => Boolean(detail));
      setState((current) => ({
        ...current,
        message: "Manifest id, service key, and valid REST parameter JSON are required before running a REST dry-run.",
        details,
        tone: "warning"
      }));
      return;
    }

    const requestedAt = new Date();
    setState((current) => ({ ...current, busyAction: "rest-dry-run", message: "Running REST provider integration dry-run.", details: [], tone: "default" }));
    try {
      const result = await runRestProviderIntegrationDryRun({
        syncRunId: providerIntegrationWorkbenchSyncRunId(row.integrationConnectionId, "rest", requestedAt),
        manifestId,
        connectionId,
        capability: state.capability,
        endpointKey: state.endpointKey.trim(),
        pathParameters: pathParameters.value,
        queryParameters: queryParameters.value,
        requestedBy: operatorName,
        requestedAt: requestedAt.toISOString(),
        maxPages: 2
      });
      setState((current) => ({
        ...current,
        dryRunResult: result,
        busyAction: null,
        message: `REST dry-run completed: ${result.recordsAccepted} accepted / ${result.recordsQuarantined} quarantined.`,
        details: result.issues.map((issue) => issue.message),
        tone: result.recordsQuarantined > 0 || result.issues.length > 0 ? "warning" : "success"
      }));
    } catch (error) {
      const display = describeApiError(error, "REST provider integration dry-run failed.");
      setState((current) => ({ ...current, busyAction: null, message: display.summary, details: display.details, tone: "danger" }));
    }
  };

  const checkSchemaDrift = async () => {
    if (!manifestId || !latestRawPayload || !state.endpointKey.trim()) {
      setState((current) => ({ ...current, message: "A manifest id, service key, and dry-run or runtime sample are required before schema drift review.", details: [], tone: "warning" }));
      return;
    }

    const checkedAt = new Date();
    setState((current) => ({ ...current, busyAction: "drift", message: "Checking provider integration schema drift.", details: [], tone: "default" }));
    try {
      const result = await checkProviderIntegrationSchemaDrift({
        manifestId,
        connectionId,
        capability: latestRawPayload.capability,
        endpointKey: state.endpointKey.trim(),
        syncRunId: latestRawPayload.syncRunId,
        rawPayloadId: latestRawPayload.rawPayloadId,
        checkedBy: operatorName,
        checkedAt: checkedAt.toISOString()
      });
      setState((current) => ({
        ...current,
        driftResult: result,
        busyAction: null,
        message: result.driftDetected ? "Schema drift detected." : "Schema drift check passed.",
        details: result.issues.map((issue) => issue.message),
        tone: result.shouldPauseCapability ? "danger" : result.driftDetected ? "warning" : "success"
      }));
    } catch (error) {
      const display = describeApiError(error, "Provider integration schema drift check failed.");
      setState((current) => ({ ...current, busyAction: null, message: display.summary, details: display.details, tone: "danger" }));
    }
  };

  const activateSetup = async () => {
    if (!manifestId || !activationReady) {
      setState((current) => ({ ...current, message: "Save the manifest version and pass readiness before activation.", details: providerIntegrationReadinessDetails(current.readiness), tone: "warning" }));
      return;
    }

    const approvedAt = new Date();
    setState((current) => ({ ...current, busyAction: "activate", message: "Requesting provider integration activation.", details: [], tone: "default" }));
    try {
      const result = await activateProviderIntegration({
        manifestId,
        connectionId,
        approvedBy: operatorName,
        approvedAt: approvedAt.toISOString(),
        expectedManifestReference: state.manifestReference?.manifestId === manifestId
          ? state.manifestReference
          : undefined,
        approvalEvidenceId: providerIntegrationWorkbenchEvidenceId(row.integrationConnectionId, "activation", approvedAt),
        changeReason: "Activated from the Settings Provider Connection Center guided workbench."
      });
      setState((current) => {
        const manifest = result.activated && current.manifest
          ? {
            ...current.manifest,
            manifestVersion: result.manifestReference?.manifestVersion ?? current.manifest.manifestVersion,
            state: result.manifestState,
            approvedBy: operatorName,
            approvedAt: approvedAt.toISOString(),
            changeReason: "Activated from the Settings Provider Connection Center guided workbench."
          }
          : current.manifest;
        const draft = parseProviderIntegrationWorkbenchJson<ProviderIntegrationManifest>(current.draftManifestJson, "Manifest draft JSON");
        return {
          ...current,
          manifest,
          manifestReference: result.manifestReference ?? current.manifestReference,
          draftManifestJson: result.activated && manifest && draft.ok
            ? providerIntegrationFormatJson({ ...draft.value, manifestVersion: manifest.manifestVersion, state: manifest.state, approvedBy: manifest.approvedBy, approvedAt: manifest.approvedAt })
            : current.draftManifestJson,
          activationResult: result,
          readiness: result.readiness,
          busyAction: null,
          message: result.message ?? `Provider integration ${result.activated ? "activated" : "activation reviewed"}.`,
          details: providerIntegrationReadinessDetails(result.readiness),
          tone: result.activated ? "success" : "warning"
        };
      });
    } catch (error) {
      const display = describeApiError(error, "Provider integration activation failed.");
      setState((current) => ({ ...current, busyAction: null, message: display.summary, details: display.details, tone: "danger" }));
    }
  };

  return (
    <section className="mt-3 rounded-md border border-border/60 bg-background/35 px-3 py-3" aria-label={`${row.displayName} guided provider integration workbench`}>
      <div className="flex flex-col gap-2 lg:flex-row lg:items-start lg:justify-between">
        <div>
          <h6 className="text-xs font-semibold text-foreground">Manifest and activation workbench</h6>
          <p className="mt-1 text-[11px] leading-4 text-muted-foreground">Advanced template, dry-run, readiness, drift, and activation evidence stay on shared provider-integration endpoints.</p>
        </div>
        <div className="flex flex-wrap gap-2">
          <Button type="button" variant="outline" size="sm" onClick={() => void loadTemplates()} disabled={busy} busy={state.busyAction === "templates"} aria-label={`Load provider integration templates for ${row.displayName}`}>
            <RefreshCcw className="h-3.5 w-3.5" aria-hidden="true" />
            Load templates
          </Button>
          <Button type="button" variant="outline" size="sm" onClick={() => void applyTemplate()} disabled={busy || !state.selectedManifestId.trim()} busy={state.busyAction === "template"} aria-label={`Use selected provider integration template for ${row.displayName}`}>
            <Save className="h-3.5 w-3.5" aria-hidden="true" />
            Use template
          </Button>
          <Button type="button" variant="outline" size="sm" onClick={() => void checkReadiness()} disabled={busy || !manifestId} busy={state.busyAction === "readiness"} aria-label={`Check provider integration activation readiness for ${row.displayName}`}>
            <MonitorCheck className="h-3.5 w-3.5" aria-hidden="true" />
            Check readiness
          </Button>
        </div>
      </div>
      <div className="mt-3 grid gap-3 lg:grid-cols-[minmax(0,1fr)_minmax(0,1fr)]">
        <label className="grid gap-1 text-[11px] font-medium text-muted-foreground">
          Template or manifest ID
          <Input value={state.selectedManifestId} onChange={(event) => updateField("selectedManifestId", event.target.value)} disabled={busy} aria-label={`${row.displayName} provider integration manifest id`} />
        </label>
        <label className="grid gap-1 text-[11px] font-medium text-muted-foreground">
          Template catalog
          <Select className="text-xs" value={state.selectedManifestId} onChange={(event) => updateField("selectedManifestId", event.target.value)} disabled={busy || !state.templates || state.templates.length === 0} aria-label={`${row.displayName} provider integration template`}>
            <option value={state.selectedManifestId}>{state.selectedManifestId || "No template selected"}</option>
            {(state.templates ?? []).map((template) => <option key={template.manifestId} value={template.manifestId}>{template.displayName}</option>)}
          </Select>
        </label>
      </div>
      <div className="mt-3 grid gap-3 lg:grid-cols-2">
        <label className="grid gap-1 text-[11px] font-medium text-muted-foreground">
          Manifest draft JSON
          <TextArea className="min-h-36 font-mono text-[11px]" value={state.draftManifestJson} onChange={(event) => updateField("draftManifestJson", event.target.value)} disabled={busy} aria-label={`${row.displayName} provider integration manifest draft JSON`} spellCheck={false} />
        </label>
        <label className="grid gap-1 text-[11px] font-medium text-muted-foreground">
          Connection draft JSON
          <TextArea className="min-h-36 font-mono text-[11px]" value={state.draftConnectionJson} onChange={(event) => updateField("draftConnectionJson", event.target.value)} disabled={busy} aria-label={`${row.displayName} provider integration connection draft JSON`} spellCheck={false} />
        </label>
      </div>
      <div className="mt-3 flex flex-wrap gap-2">
        <Button type="button" variant="outline" size="sm" onClick={() => void saveSetupDraft()} disabled={busy || !state.draftManifestJson.trim() || !state.draftConnectionJson.trim()} busy={state.busyAction === "save"} aria-label={`Save provider integration setup draft for ${row.displayName}`}>
          <Save className="h-3.5 w-3.5" aria-hidden="true" />
          Save setup draft
        </Button>
        <Button type="button" variant="outline" size="sm" onClick={() => setActivationConfirmationOpen(true)} disabled={busy || !activationReady} busy={state.busyAction === "activate"} disabledReason={!activationReady ? "Save the manifest version and pass readiness before activation." : undefined} aria-label={`Activate provider integration setup for ${row.displayName}`}>
          <ShieldCheck className="h-3.5 w-3.5" aria-hidden="true" />
          Activate when ready
        </Button>
      </div>
      <SettingsMutationConfirmDialog
        confirmation={activationConfirmationOpen ? {
          id: `activate-provider-${row.providerId}`,
          title: `Activate ${row.displayName} integration?`,
          description: "Submit the readiness-approved manifest and connection for server-governed activation. This does not bypass shared approval or production-write policy.",
          confirmLabel: "Confirm activation",
          confirmAriaLabel: `Confirm provider integration activation for ${row.displayName}`,
          run: activateSetup
        } : null}
        busy={false}
        onCancel={() => setActivationConfirmationOpen(false)}
        onConfirm={() => {
          setActivationConfirmationOpen(false);
          void activateSetup();
        }}
      />
      {state.manifest ? (
        <div className="mt-3 grid gap-2 rounded-sm border border-border/60 bg-background/40 px-2 py-2" aria-label={`${row.displayName} provider integration mapping preview`}>
          <div className="flex flex-wrap items-center gap-2">
            <span className="text-xs font-semibold text-foreground">Mapping preview</span>
            <Badge variant="outline">{state.manifest.integrationType}</Badge>
            <SeverityBadge
              status={state.manifest.state === "Active" ? "ready" : "action"}
              label={state.manifest.state}
            />
          </div>
          <div className="grid gap-2 md:grid-cols-2">
            {mappingPreview.length > 0 ? mappingPreview.slice(0, 4).map((mapping) => (
              <div key={`${mapping.capability}-${mapping.sourcePath}-${mapping.targetField}`} className="rounded-sm border border-border/60 bg-secondary/20 px-2 py-2 text-[11px] text-muted-foreground">
                <span className="font-mono text-foreground">{mapping.sourcePath}</span> {" → "} <span className="font-mono text-foreground">{mapping.targetField}</span> · {mapping.confidence}{mapping.required ? " · required" : ""}
              </div>
            )) : <p className="text-[11px] text-muted-foreground">No mapping rows for the selected capability.</p>}
          </div>
        </div>
      ) : null}
      <div className="mt-3 grid gap-3 lg:grid-cols-[minmax(0,1fr)_minmax(0,1fr)_minmax(0,1fr)]">
        <label className="grid gap-1 text-[11px] font-medium text-muted-foreground">
          Capability
          <Select className="text-xs" value={state.capability} onChange={(event) => setState((current) => {
            const capability = event.target.value as ProviderIntegrationCapabilityKind;
            return { ...current, capability, endpointKey: providerIntegrationPreferredEndpointKey(current.manifest, capability, current.endpointKey), message: null, details: [], tone: "default" };
          })} disabled={busy} aria-label={`${row.displayName} provider integration dry-run capability`}>
            {capabilityOptions.map((capability) => <option key={capability} value={capability}>{capability}</option>)}
          </Select>
        </label>
        <label className="grid gap-1 text-[11px] font-medium text-muted-foreground">
          Service key
          <Input value={state.endpointKey} onChange={(event) => updateField("endpointKey", event.target.value)} list={`${row.providerId}-provider-endpoint-options`} disabled={busy} aria-label={`${row.displayName} provider integration dry-run service key`} />
          <datalist id={`${row.providerId}-provider-endpoint-options`}>{endpointOptions.map((endpoint) => <option key={endpoint.endpointKey} value={endpoint.endpointKey} />)}</datalist>
        </label>
        <label className="grid gap-1 text-[11px] font-medium text-muted-foreground">
          CSV file name
          <Input value={state.csvFileName} onChange={(event) => updateField("csvFileName", event.target.value)} disabled={busy} aria-label={`${row.displayName} provider integration CSV dry-run file name`} />
        </label>
      </div>
      <div className="mt-3 grid gap-3 lg:grid-cols-2">
        <label className="grid gap-1 text-[11px] font-medium text-muted-foreground">
          CSV content
          <TextArea className="min-h-24 font-mono text-[11px]" value={state.csvContent} onChange={(event) => updateField("csvContent", event.target.value)} disabled={busy} aria-label={`${row.displayName} provider integration CSV dry-run content`} spellCheck={false} />
        </label>
        <div className="grid gap-3">
          <label className="grid gap-1 text-[11px] font-medium text-muted-foreground">
            REST path parameters JSON
            <Input value={state.restPathParametersJson} onChange={(event) => updateField("restPathParametersJson", event.target.value)} disabled={busy} aria-label={`${row.displayName} provider integration REST path parameters JSON`} />
          </label>
          <label className="grid gap-1 text-[11px] font-medium text-muted-foreground">
            REST query parameters JSON
            <Input value={state.restQueryParametersJson} onChange={(event) => updateField("restQueryParametersJson", event.target.value)} disabled={busy} aria-label={`${row.displayName} provider integration REST query parameters JSON`} />
          </label>
        </div>
      </div>
      <div className="mt-3 flex flex-wrap gap-2">
        <Button type="button" variant="outline" size="sm" onClick={() => void runCsvDryRun()} disabled={busy || !manifestId || !state.csvContent.trim()} busy={state.busyAction === "csv-dry-run"} aria-label={`Run provider integration CSV dry-run for ${row.displayName}`}>
          <Activity className="h-3.5 w-3.5" aria-hidden="true" />
          CSV dry-run
        </Button>
        <Button type="button" variant="outline" size="sm" onClick={() => void runRestDryRun()} disabled={busy || !manifestId || !state.endpointKey.trim()} busy={state.busyAction === "rest-dry-run"} aria-label={`Run provider integration REST dry-run for ${row.displayName}`}>
          <Activity className="h-3.5 w-3.5" aria-hidden="true" />
          REST dry-run
        </Button>
        <Button type="button" variant="outline" size="sm" onClick={() => void checkSchemaDrift()} disabled={busy || !canCheckDrift} busy={state.busyAction === "drift"} disabledReason={!canCheckDrift ? "A dry-run or runtime sample is required before schema drift review." : undefined} aria-label={`Check provider integration schema drift for ${row.displayName}`}>
          <MonitorCheck className="h-3.5 w-3.5" aria-hidden="true" />
          Schema drift
        </Button>
      </div>
      <FixedIncomePaymentPreview mappedRecords={state.dryRunResult?.mappedRecordPreviews ?? []} />
      <dl className="mt-3 grid gap-2 sm:grid-cols-2">
        <SettingsFieldRow label="Manifest" value={manifestId || "Not selected"} tone={manifestId ? "success" : "muted"} />
        <SettingsFieldRow label="Connection" value={state.connection?.connectionName ?? runtimeState.monitor?.connectionName ?? "Runtime connection"} tone="muted" />
        <SettingsFieldRow label="Activation readiness" value={state.readiness ? (state.readiness.isReady ? "Ready" : "Review required") : "Not checked"} tone={state.readiness?.isReady ? "success" : state.readiness ? "warning" : "muted"} />
        <SettingsFieldRow label="Dry-run result" value={state.dryRunResult ? `${state.dryRunResult.recordsAccepted} accepted / ${state.dryRunResult.recordsQuarantined} quarantined` : "Not run"} tone={state.dryRunResult ? (state.dryRunResult.recordsQuarantined > 0 ? "warning" : "success") : "muted"} />
        <SettingsFieldRow label="Schema drift" value={state.driftResult ? (state.driftResult.driftDetected ? `${state.driftResult.issues.length} issues` : "No drift") : "Not checked"} tone={state.driftResult ? (state.driftResult.shouldPauseCapability ? "danger" : state.driftResult.driftDetected ? "warning" : "success") : "muted"} />
        <SettingsFieldRow label="Activation" value={state.activationResult ? state.activationResult.connectionState : "Not requested"} tone={state.activationResult?.activated ? "success" : "muted"} />
      </dl>
      <BondImportValuePreview mappedRecords={state.dryRunResult?.mappedRecordPreviews ?? []} />
      {state.message ? (
        <div role={state.tone === "danger" ? "alert" : "status"} className={cn("mt-3 text-xs", itemToneClass[state.tone])}>
          <div>{state.message}</div>
          {state.details.length > 0 ? (
            <ul className="mt-1 list-disc space-y-1 pl-4 text-[11px] text-muted-foreground">
              {state.details.map((detail) => <li key={detail}>{detail}</li>)}
            </ul>
          ) : null}
        </div>
      ) : null}
    </section>
  );
}

export function ProviderOpenApiImportForm({
  row,
  state,
  onStateChange,
  onSubmit
}: {
  row: SettingsProviderConnectionRow;
  state: ProviderOpenApiImportState;
  onStateChange: (updater: (state: ProviderOpenApiImportState) => ProviderOpenApiImportState) => void;
  onSubmit: () => void;
}) {
  const updateField = <K extends keyof ProviderOpenApiImportState>(
    field: K,
    value: ProviderOpenApiImportState[K]
  ) => {
    onStateChange((current) => ({
      ...current,
      [field]: value,
      message: null,
      details: [],
      tone: "default"
    }));
  };

  return (
    <form
      className="mt-3 grid gap-3 rounded-md border border-border/60 bg-background/35 px-3 py-3"
      aria-label={`${row.displayName} OpenAPI import draft`}
      onSubmit={(event) => {
        event.preventDefault();
        onSubmit();
      }}
      noValidate
    >
      <div className="flex flex-col gap-2 lg:flex-row lg:items-start lg:justify-between">
        <div>
          <h6 className="text-xs font-semibold text-foreground">OpenAPI import draft</h6>
          <p className="mt-1 text-[11px] leading-4 text-muted-foreground">
            Seed a draft manifest through the shared import endpoint; readiness, mapping, and runtime evidence stay service-owned.
          </p>
        </div>
        <Button
          type="submit"
          variant="outline"
          size="sm"
          className="shrink-0"
          disabled={state.busy}
          busy={state.busy}
          busyLabel="Importing OpenAPI"
          aria-label={`Import OpenAPI draft manifest for ${row.displayName}`}
        >
          <Save className="h-3.5 w-3.5" aria-hidden="true" />
          Import draft
        </Button>
      </div>
      <div className="grid gap-3 md:grid-cols-2">
        <label className="grid gap-1 text-[11px] font-medium text-muted-foreground">
          Manifest ID
          <Input
            value={state.manifestId}
            onChange={(event) => updateField("manifestId", event.target.value)}
            disabled={state.busy}
            aria-label={`${row.displayName} OpenAPI manifest id`}
          />
        </label>
        <label className="grid gap-1 text-[11px] font-medium text-muted-foreground">
          Display name
          <Input
            value={state.displayName}
            onChange={(event) => updateField("displayName", event.target.value)}
            disabled={state.busy}
            aria-label={`${row.displayName} OpenAPI display name`}
          />
        </label>
        <label className="grid gap-1 text-[11px] font-medium text-muted-foreground">
          Environment
          <Input
            value={state.environment}
            onChange={(event) => updateField("environment", event.target.value)}
            disabled={state.busy}
            aria-label={`${row.displayName} OpenAPI environment`}
          />
        </label>
        <FilterSelect
          label="Auth type"
          value={state.authType}
          onChange={(value) => updateField("authType", value as ProviderIntegrationAuthType)}
          options={PROVIDER_OPEN_API_AUTH_OPTIONS}
        />
        <label className="grid gap-1 text-[11px] font-medium text-muted-foreground">
          Capabilities
          <Input
            value={state.capabilities}
            onChange={(event) => updateField("capabilities", event.target.value)}
            disabled={state.busy}
            aria-label={`${row.displayName} OpenAPI capabilities`}
          />
        </label>
        <label className="grid gap-1 text-[11px] font-medium text-muted-foreground">
          OAuth token URL
          <Input
            value={state.tokenUrl}
            onChange={(event) => updateField("tokenUrl", event.target.value)}
            disabled={state.busy}
            aria-label={`${row.displayName} OpenAPI token URL`}
          />
        </label>
        <label className="grid gap-1 text-[11px] font-medium text-muted-foreground">
          Scopes
          <Input
            value={state.scopes}
            onChange={(event) => updateField("scopes", event.target.value)}
            disabled={state.busy}
            aria-label={`${row.displayName} OpenAPI scopes`}
          />
        </label>
        <label className="grid gap-1 text-[11px] font-medium text-muted-foreground">
          Change reason
          <Input
            value={state.changeReason}
            onChange={(event) => updateField("changeReason", event.target.value)}
            disabled={state.busy}
            aria-label={`${row.displayName} OpenAPI change reason`}
          />
        </label>
      </div>
      <label className="grid gap-1 text-[11px] font-medium text-muted-foreground">
        OpenAPI JSON
        <TextArea rows={2}
          className="min-h-24 font-mono text-xs"
          value={state.openApiDocumentJson}
          onChange={(event) => updateField("openApiDocumentJson", event.target.value)}
          disabled={state.busy}
          aria-label={`${row.displayName} OpenAPI JSON`}
          spellCheck={false}
        />
      </label>
      {state.message ? (
        <div role={state.tone === "danger" ? "alert" : "status"} className={cn("text-xs", itemToneClass[state.tone])}>
          <div>{state.message}</div>
          {state.result ? (
            <div className="mt-1 text-[11px] text-muted-foreground">
              {state.result.manifest.integrationType} · {state.result.manifest.state} · {state.result.readiness.isReady ? "Ready" : "Readiness review"}
            </div>
          ) : null}
          {state.details.length > 0 ? (
            <ul className="mt-1 list-disc space-y-1 pl-4 text-[11px] text-muted-foreground">
              {state.details.map((detail) => <li key={detail}>{detail}</li>)}
            </ul>
          ) : null}
        </div>
      ) : null}
    </form>
  );
}


export function providerRuntimeStateKey(row: Pick<SettingsProviderConnectionRow, "integrationConnectionId" | "providerId">): string {
  return row.integrationConnectionId || row.providerId;
}


export function providerRuntimeReplaySyncRunId(connectionId: string, requestedAt: Date): string {
  const suffix = requestedAt.toISOString().replace(/[^0-9A-Za-z]/g, "").toLowerCase();
  const normalizedConnection = (connectionId || "connection").replace(/[^0-9A-Za-z-]/g, "-").toLowerCase();
  return `provider-replay-${normalizedConnection}-${suffix}`;
}


export function providerRuntimeHandoffEvidenceId(connectionId: string, requestedAt: Date): string {
  const suffix = requestedAt.toISOString().replace(/[^0-9A-Za-z]/g, "").toLowerCase();
  const normalizedConnection = (connectionId || "connection").replace(/[^0-9A-Za-z-]/g, "-").toLowerCase();
  return `settings-provider-handoff-${normalizedConnection}-${suffix}`;
}


export function createProviderIntegrationWorkbenchState(row: SettingsProviderConnectionRow): ProviderIntegrationWorkbenchState {
  const capability = providerIntegrationDefaultCapability(row);
  return {
    templates: null,
    selectedManifestId: providerIntegrationDefaultManifestId(row),
    manifest: null,
    manifestReference: null,
    connection: null,
    draftManifestJson: "",
    draftConnectionJson: "",
    capability,
    endpointKey: providerIntegrationDefaultEndpointKey(capability),
    csvFileName: `${providerIntegrationNormalizedId(row.providerId)}-${capability.toLowerCase()}.csv`,
    csvContent: providerIntegrationSampleCsv(capability),
    restPathParametersJson: "{}",
    restQueryParametersJson: "{}",
    readiness: null,
    setupResult: null,
    dryRunResult: null,
    driftResult: null,
    activationResult: null,
    busyAction: null,
    message: null,
    details: [],
    tone: "default"
  };
}


export function providerIntegrationDefaultCapability(row: Pick<SettingsProviderConnectionRow, "capabilityGroup">): ProviderIntegrationCapabilityKind {
  return row.capabilityGroup === "accounting" ? "Transactions" : "Positions";
}


export function providerIntegrationDefaultEndpointKey(capability: ProviderIntegrationCapabilityKind): string {
  return capability.replace(/([a-z])([A-Z])/g, "$1-$2").toLowerCase();
}


export function providerIntegrationDefaultManifestId(row: Pick<SettingsProviderConnectionRow, "providerId" | "capabilityGroup">): string {
  const suffix = row.capabilityGroup === "accounting" ? "accounting" : row.capabilityGroup === "brokerage" ? "brokerage" : "data";
  return `template-${providerIntegrationNormalizedId(row.providerId)}-${suffix}-v1`;
}


export function providerIntegrationDefaultSelectedManifestId(
  row: Pick<SettingsProviderConnectionRow, "providerId" | "displayName" | "capabilityGroup">,
  templates: ProviderIntegrationTemplateCatalogEntry[],
  currentManifestId: string
): string {
  const current = currentManifestId.trim();
  if (current && templates.some((template) => template.manifestId === current)) {
    return current;
  }

  const normalizedProvider = providerIntegrationNormalizedId(row.providerId);
  const normalizedName = providerIntegrationNormalizedId(row.displayName);
  const matched = templates.find((template) => (
    providerIntegrationNormalizedId(template.providerId) === normalizedProvider ||
    providerIntegrationNormalizedId(template.displayName).includes(normalizedName) ||
    providerIntegrationNormalizedId(template.manifestId).includes(normalizedProvider)
  ));

  return matched?.manifestId ?? templates[0]?.manifestId ?? current ?? providerIntegrationDefaultManifestId(row);
}


export function createProviderIntegrationConnectionDraft(
  row: SettingsProviderConnectionRow,
  manifest: ProviderIntegrationManifest,
  operatorName: string
): ProviderIntegrationConnection {
  const now = new Date().toISOString();
  const enabledCapabilities = manifest.capabilities
    .filter((capability) => capability.enabled)
    .map((capability) => capability.capability);
  return {
    connectionId: row.integrationConnectionId || `${manifest.manifestId}-connection`,
    providerId: manifest.providerId || row.providerId,
    manifestId: manifest.manifestId,
    connectionName: `${manifest.displayName || row.displayName} ${manifest.environment || providerOpenApiEnvironment(row.environmentLabel)}`.trim(),
    environment: manifest.environment || providerOpenApiEnvironment(row.environmentLabel),
    state: manifest.state === "Active" ? "PendingApproval" : manifest.state,
    credentialSecretRef: row.credentialStatus === "not-required" ? "" : providerIntegrationCredentialReference(row),
    enabledCapabilities: enabledCapabilities.length > 0 ? enabledCapabilities : [providerIntegrationDefaultCapability(row)],
    ownerUserId: operatorName,
    createdAt: manifest.createdAt || now,
    updatedAt: now,
    approvalEvidenceId: null
  };
}


export function providerIntegrationWorkbenchWithDraft(
  row: SettingsProviderConnectionRow,
  state: ProviderIntegrationWorkbenchState,
  manifest: ProviderIntegrationManifest,
  connection: ProviderIntegrationConnection,
  status: { message: string; details: string[]; tone: ProviderIntegrationWorkbenchState["tone"] }
): ProviderIntegrationWorkbenchState {
  const capability = providerIntegrationWorkbenchCapabilities({ ...state, manifest })[0] ?? providerIntegrationDefaultCapability(row);
  return {
    ...state,
    selectedManifestId: manifest.manifestId,
    manifest,
    manifestReference: null,
    connection,
    draftManifestJson: providerIntegrationFormatJson(manifest),
    draftConnectionJson: providerIntegrationFormatJson(connection),
    capability,
    endpointKey: providerIntegrationPreferredEndpointKey(manifest, capability, state.endpointKey),
    readiness: null,
    setupResult: null,
    driftResult: null,
    activationResult: null,
    busyAction: null,
    message: status.message,
    details: status.details,
    tone: status.tone
  };
}


export function providerIntegrationWorkbenchCapabilities(state: Pick<ProviderIntegrationWorkbenchState, "manifest" | "capability">): ProviderIntegrationCapabilityKind[] {
  const capabilities = new Set<ProviderIntegrationCapabilityKind>();
  if (state.capability) {
    capabilities.add(state.capability);
  }
  state.manifest?.capabilities
    .filter((capability) => capability.enabled)
    .forEach((capability) => capabilities.add(capability.capability));
  if (capabilities.size === 0) {
    capabilities.add("Positions");
  }
  return Array.from(capabilities);
}


export function providerIntegrationWorkbenchEndpoints(
  state: Pick<ProviderIntegrationWorkbenchState, "manifest">,
  capability: ProviderIntegrationCapabilityKind
): ProviderIntegrationEndpointDefinition[] {
  return (state.manifest?.endpoints ?? []).filter((endpoint) => endpoint.capability === capability);
}


export function providerIntegrationWorkbenchMappings(
  state: Pick<ProviderIntegrationWorkbenchState, "manifest">,
  capability: ProviderIntegrationCapabilityKind
): ProviderIntegrationFieldMapping[] {
  return (state.manifest?.fieldMappings ?? []).filter((mapping) => mapping.capability === capability);
}


export function providerIntegrationPreferredEndpointKey(
  manifest: ProviderIntegrationManifest | null,
  capability: ProviderIntegrationCapabilityKind,
  currentEndpointKey: string
): string {
  const current = currentEndpointKey.trim();
  const endpoints = (manifest?.endpoints ?? []).filter((endpoint) => endpoint.capability === capability);
  if (current && endpoints.some((endpoint) => endpoint.endpointKey === current)) {
    return current;
  }
  return endpoints[0]?.endpointKey ?? (current || providerIntegrationDefaultEndpointKey(capability));
}


export function providerIntegrationWorkbenchManifestId(
  state: Pick<ProviderIntegrationWorkbenchState, "manifest" | "selectedManifestId">,
  runtimeState: ProviderRuntimeEvidenceState
): string {
  if (state.manifest?.manifestId) {
    return state.manifest.manifestId;
  }

  const selectedManifestId = state.selectedManifestId.trim();
  if (selectedManifestId) {
    return selectedManifestId;
  }

  return runtimeState.monitor?.manifestId ?? "";
}


export function providerIntegrationWorkbenchConnectionId(
  row: Pick<SettingsProviderConnectionRow, "integrationConnectionId">,
  state: Pick<ProviderIntegrationWorkbenchState, "connection">,
  runtimeState: ProviderRuntimeEvidenceState
): string {
  return state.connection?.connectionId ?? runtimeState.monitor?.connectionId ?? row.integrationConnectionId;
}


export function providerIntegrationLatestRawPayload(
  state: Pick<ProviderIntegrationWorkbenchState, "dryRunResult" | "capability" | "endpointKey">,
  runtimeState: ProviderRuntimeEvidenceState
): { syncRunId: string; rawPayloadId: string; capability: ProviderIntegrationCapabilityKind; endpointKey: string } | null {
  if (state.dryRunResult?.rawPayloadId) {
    return {
      syncRunId: state.dryRunResult.syncRunId,
      rawPayloadId: state.dryRunResult.rawPayloadId,
      capability: state.dryRunResult.capability,
      endpointKey: state.endpointKey
    };
  }

  const latestRunWithPayload = providerRuntimeRuns(runtimeState).find((run) => Boolean(run.rawPayloadId));
  if (!latestRunWithPayload?.rawPayloadId) {
    return null;
  }

  return {
    syncRunId: latestRunWithPayload.syncRunId,
    rawPayloadId: latestRunWithPayload.rawPayloadId,
    capability: latestRunWithPayload.capability,
    endpointKey: latestRunWithPayload.endpointKey
  };
}


export function providerIntegrationWorkbenchDraftDetails(manifest: ProviderIntegrationManifest): string[] {
  return [
    `${manifest.endpoints.length} endpoint definitions`,
    `${manifest.fieldMappings.length} mapping rows`,
    `${manifest.validationRules.length} validation rules`
  ];
}


export function createProviderOpenApiImportState(row: SettingsProviderConnectionRow): ProviderOpenApiImportState {
  const normalizedProvider = (row.providerId || row.integrationConnectionId || "provider")
    .replace(/[^0-9A-Za-z-]/g, "-")
    .replace(/-+/g, "-")
    .toLowerCase();
  const capability = row.capabilityGroup === "accounting" ? "Transactions" : "Positions";

  return {
    manifestId: `draft-${normalizedProvider}-openapi-v1`,
    displayName: `${row.displayName} OpenAPI`,
    environment: providerOpenApiEnvironment(row.environmentLabel),
    authType: "OAuth2",
    tokenUrl: "",
    scopes: "",
    capabilities: capability,
    openApiDocumentJson: PROVIDER_OPEN_API_SAMPLE_DOCUMENT,
    changeReason: "Imported from the Settings Provider Connection Center.",
    busy: false,
    result: null,
    message: null,
    details: [],
    tone: "default"
  };
}


export function providerOpenApiEnvironment(environmentLabel: string): string {
  const normalized = environmentLabel.trim().toLowerCase();
  return normalized && normalized !== "not set" ? normalized : "paper";
}


export function parseProviderOpenApiCapabilities(value: string): ProviderIntegrationCapabilityKind[] {
  const allowed = new Set<ProviderIntegrationCapabilityKind>(PROVIDER_OPEN_API_CAPABILITIES);
  const capabilities = value
    .split(",")
    .map((capability) => capability.trim())
    .filter((capability): capability is ProviderIntegrationCapabilityKind =>
      allowed.has(capability as ProviderIntegrationCapabilityKind));

  return Array.from(new Set(capabilities));
}


export function providerRuntimeValue<T>(result: PromiseSettledResult<T>): T | null {
  return result.status === "fulfilled" ? result.value : null;
}


export function providerRuntimeErrorDetail<T>(result: PromiseSettledResult<T>, label: string): string | null {
  if (result.status === "fulfilled") {
    return null;
  }

  const display = describeApiError(result.reason, `${label} could not be loaded.`);
  return [display.summary, ...display.details].filter(Boolean).join(" ");
}


export function providerRuntimeRuns(state: ProviderRuntimeEvidenceState): ProviderIntegrationSyncRunEvidence[] {
  const runs = new Map<string, ProviderIntegrationSyncRunEvidence>();
  const addRun = (run: ProviderIntegrationSyncRunEvidence | null | undefined) => {
    if (run) {
      runs.set(run.syncRunId, run);
    }
  };

  addRun(state.monitor?.lastSyncRun);
  state.monitor?.recentSyncRuns.forEach(addRun);
  state.syncRuns?.syncRuns.forEach(addRun);

  return Array.from(runs.values()).sort((left, right) => providerRuntimeDateValue(right.startedAt) - providerRuntimeDateValue(left.startedAt));
}


export function providerRuntimeReceivedCount(state: ProviderRuntimeEvidenceState, runs: ProviderIntegrationSyncRunEvidence[]): number {
  return state.monitor?.recentRecordsReceived ?? providerRuntimeSum(runs, (run) => run.recordsReceived);
}


export function providerRuntimeAcceptedCount(state: ProviderRuntimeEvidenceState, runs: ProviderIntegrationSyncRunEvidence[]): number {
  return state.monitor?.recentRecordsAccepted ?? providerRuntimeSum(runs, (run) => run.recordsAccepted);
}


export function providerRuntimeQuarantinedCount(state: ProviderRuntimeEvidenceState, runs: ProviderIntegrationSyncRunEvidence[]): number {
  return state.monitor?.recentRecordsQuarantined ?? state.quarantine?.totalQuarantinedRecords ?? providerRuntimeSum(runs, (run) => run.recordsQuarantined);
}


export function providerRuntimeStagedCount(state: ProviderRuntimeEvidenceState, runs: ProviderIntegrationSyncRunEvidence[]): number {
  return state.monitor?.durableStagingRecordCount ?? providerRuntimeSum(runs, (run) => run.durableStagingRecordCount);
}


export function providerRuntimeDurableQuarantinedCount(state: ProviderRuntimeEvidenceState, runs: ProviderIntegrationSyncRunEvidence[]): number {
  return state.monitor?.durableQuarantinedRecordCount ?? state.quarantine?.totalQuarantinedRecords ?? providerRuntimeSum(runs, (run) => run.durableQuarantinedRecordCount);
}


export function providerRuntimeCriticalIssueCount(state: ProviderRuntimeEvidenceState): number {
  return Math.max(
    state.quarantine?.criticalIssueCount ?? 0,
    providerRuntimeRuns(state).reduce((sum, run) => sum + run.criticalIssueCount, 0),
    state.monitor?.hasCriticalIssues ? 1 : 0
  );
}


export function providerRuntimeWarningIssueCount(state: ProviderRuntimeEvidenceState): number {
  return Math.max(
    state.quarantine?.warningIssueCount ?? 0,
    providerRuntimeRuns(state).reduce((sum, run) => sum + run.warningIssueCount, 0),
    state.identity?.accountReviewRequiredCount ?? 0,
    state.identity?.securityReviewRequiredCount ?? 0,
    state.promotion?.reviewRequiredCount ?? 0
  );
}


export function providerRuntimeSum(
  runs: ProviderIntegrationSyncRunEvidence[],
  selector: (run: ProviderIntegrationSyncRunEvidence) => number
): number {
  return runs.reduce((sum, run) => sum + selector(run), 0);
}


export function providerRuntimeDateValue(value: string | null | undefined): number {
  if (!value) {
    return 0;
  }

  const parsed = Date.parse(value);
  return Number.isNaN(parsed) ? 0 : parsed;
}


export function providerRuntimePanelTone(state: ProviderRuntimeEvidenceState): keyof typeof diagnosticToneClass {
  if (state.phase === "error") return "danger";
  if (state.phase === "loading") return "warning";
  if (providerRuntimeCriticalIssueCount(state) > 0) return "danger";
  if (providerRuntimeQuarantinedCount(state, providerRuntimeRuns(state)) > 0 || providerRuntimeWarningIssueCount(state) > 0) return "warning";
  if (state.phase === "loaded") return "success";
  return "default";
}


export function providerRuntimeStatusLabel(state: ProviderRuntimeEvidenceState): string {
  if (state.phase === "idle") return "Not loaded";
  if (state.phase === "loading") return "Loading";
  if (state.phase === "error") return "Unavailable";
  if (providerRuntimeCriticalIssueCount(state) > 0) return "Critical";
  if (providerRuntimeQuarantinedCount(state, providerRuntimeRuns(state)) > 0 || providerRuntimeWarningIssueCount(state) > 0) return "Review";
  return "Synced";
}


export function providerRuntimeStatusVariant(state: ProviderRuntimeEvidenceState): "outline" | "success" | "warning" | "danger" {
  const tone = providerRuntimePanelTone(state);
  if (tone === "success") return "success";
  if (tone === "warning") return "warning";
  if (tone === "danger") return "danger";
  return "outline";
}


export function providerRuntimeMessageTone(state: ProviderRuntimeEvidenceState): keyof typeof itemToneClass {
  if (state.phase === "error") return "danger";
  if (state.details.length > 0) return "warning";
  return "success";
}


export function providerRuntimeIssueTone(criticalIssueCount: number, warningIssueCount: number): keyof typeof itemToneClass {
  if (criticalIssueCount > 0) return "danger";
  if (warningIssueCount > 0) return "warning";
  return "success";
}


export function providerRuntimeRunVariant(run: ProviderIntegrationSyncRunEvidence): "outline" | "success" | "warning" | "danger" {
  const status = run.status.toLowerCase();
  if (status.includes("fail") || run.criticalIssueCount > 0) return "danger";
  if (run.recordsQuarantined > 0 || run.warningIssueCount > 0 || status.includes("review")) return "warning";
  if (status.includes("accepted") || status.includes("complete") || status.includes("success")) return "success";
  return "outline";
}


export function providerRuntimeProcessingStatusVariant(status: ProviderIntegrationProcessingStatus): "outline" | "success" | "warning" | "danger" {
  const normalized = status.toLowerCase();
  if (normalized.includes("block")) return "danger";
  if (normalized.includes("quarantine")) return "warning";
  if (normalized.includes("validated") || normalized.includes("loaded") || normalized.includes("published")) return "success";
  return "outline";
}


export function providerRuntimeLatestQuarantineDecision(
  decisions: readonly ProviderIntegrationQuarantineDecision[] | null | undefined,
  record: ProviderIntegrationQuarantinedRecord
): ProviderIntegrationQuarantineDecision | null {
  const matching = (decisions ?? [])
    .filter((decision) => decision.quarantineRecordId === record.quarantineRecordId && decision.syncRunId === record.syncRunId)
    .sort((left, right) => providerRuntimeDateValue(right.reviewedAt) - providerRuntimeDateValue(left.reviewedAt));

  return matching[0] ?? null;
}


export function providerRuntimeQuarantineActionLabel(action: ProviderIntegrationQuarantineResolutionAction): string {
  switch (action) {
    case "ReplayAfterMappingChange":
      return "Replay after mapping change";
    case "IgnoreProviderRecord":
      return "Ignore provider record";
    case "MarkAsCashPosition":
      return "Mark as cash position";
    case "ReviewOnly":
    default:
      return "Review";
  }
}


export function providerRuntimeQuarantineActionNote(action: ProviderIntegrationQuarantineResolutionAction): string {
  switch (action) {
    case "ReplayAfterMappingChange":
      return "Marked from the Settings Provider Connection Center for replay after mapping changes.";
    case "IgnoreProviderRecord":
      return "Ignored from the Settings Provider Connection Center after operator review.";
    case "MarkAsCashPosition":
      return "Marked from the Settings Provider Connection Center as a cash position candidate.";
    case "ReviewOnly":
    default:
      return "Reviewed from the Settings Provider Connection Center runtime evidence panel.";
  }
}


export function providerRuntimeRunTone(run: ProviderIntegrationSyncRunEvidence): keyof typeof itemToneClass {
  const variant = providerRuntimeRunVariant(run);
  if (variant === "danger") return "danger";
  if (variant === "warning") return "warning";
  if (variant === "success") return "success";
  return "muted";
}


export function providerRuntimeSeverityVariant(severity: string): "outline" | "success" | "warning" | "danger" {
  const normalized = severity.toLowerCase();
  if (normalized === "critical" || normalized === "error") return "danger";
  if (normalized === "warning") return "warning";
  if (normalized === "info") return "outline";
  return "outline";
}


export function providerRuntimePromotionVariant(status: string): "outline" | "success" | "warning" | "danger" {
  const normalized = status.toLowerCase();
  if (normalized.includes("blocked")) return "danger";
  if (normalized.includes("review")) return "warning";
  if (normalized.includes("ready")) return "success";
  return "outline";
}


export function formatProviderRuntimeNumber(value: number): string {
  return formatCount(value);
}


export function formatProviderRuntimeUtcMinute(value: string | Date | null | undefined, unavailableLabel = "Not synced"): string {
  if (!value) {
    return unavailableLabel;
  }

  const date = typeof value === "string" ? new Date(value) : value;
  if (Number.isNaN(date.getTime())) {
    return unavailableLabel;
  }

  return formatUtcTimestamp(date);
}


export const PROVIDER_OPEN_API_AUTH_OPTIONS: { value: ProviderIntegrationAuthType; label: string }[] = [
  { value: "OAuth2", label: "OAuth2" },
  { value: "ApiKey", label: "API key" },
  { value: "BearerToken", label: "Bearer token" },
  { value: "ClientCredentials", label: "Client credentials" },
  { value: "Basic", label: "Basic" },
  { value: "None", label: "None" }
];


export const PROVIDER_OPEN_API_CAPABILITIES: ProviderIntegrationCapabilityKind[] = [
  "Accounts",
  "Balances",
  "Positions",
  "Holdings",
  "Transactions",
  "TaxLots",
  "SecurityReferenceData",
  "MarketPrices",
  "CorporateActions",
  "Documents",
  "Alerts",
  "Events",
  "OrderPreview",
  "OrderPlacement",
  "OrderCancellation",
  "OrderStatus",
  "Executions"
];


export const PROVIDER_OPEN_API_SAMPLE_DOCUMENT = `{
  "openapi": "3.0.3",
  "info": { "title": "Provider API", "version": "draft" },
  "paths": {}
}`;
