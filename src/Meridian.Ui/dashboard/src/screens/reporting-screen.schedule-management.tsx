import { Plus, RotateCcw, Send, Trash2 } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import {
  ReportingCommandStatusView,
  ReportingScheduleField,
  type ReportingCommandStatus
} from "@/screens/reporting-screen.shared-components";
import type {
  GovernanceReportArtifactFormat,
  ReportPackDeliveryMode,
  ReportPackDistributionRecord,
  ReportWriterDatasetSource
} from "@/types";
import type {
  ReportingScheduleDeliveryPlanRow,
  ReportingScheduleRow
} from "@/screens/reporting-screen.view-model";

export type ReportingScheduleArtifactFormat = Extract<GovernanceReportArtifactFormat, "Pdf" | "Xlsx" | "Csv">;

export type ReportingScheduleDraftField =
  | "scheduleId"
  | "templateId"
  | "cronExpression"
  | "nextAsOfDate"
  | "dueAtUtc"
  | "maxRetries"
  | "requestedBy"
  | "distributionId"
  | "deliveryMode"
  | "description"
  | "deliveryNote"
  | "datasetSourceId";

export interface ReportingScheduleDraftState {
  scheduleId: string;
  templateId: string;
  cronExpression: string;
  nextAsOfDate: string;
  dueAtUtc: string;
  maxRetries: string;
  requestedBy: string;
  distributionId: string;
  deliveryMode: ReportPackDeliveryMode;
  description: string;
  deliveryNote: string;
  formats: Record<ReportingScheduleArtifactFormat, boolean>;
  deliveryTargets: ReportingScheduleDraftTarget[];
  datasetSourceId: string;
}

export interface ReportingScheduleDraftTarget {
  distributionId: string;
  deliveryMode: ReportPackDeliveryMode;
  deliveryNote: string;
  formats: Record<ReportingScheduleArtifactFormat, boolean>;
}

export const reportingScheduleArtifactFormats: ReportingScheduleArtifactFormat[] = ["Pdf", "Xlsx", "Csv"];
export const reportingScheduleDeliveryModes: ReportPackDeliveryMode[] = ["SecurePortal", "EmailLink", "EvidenceVault", "InternalRoute"];

export interface ReportingScheduleManagementModel {
  scheduleSummary: string;
  hasScheduleRows: boolean;
  scheduleListLabel: string;
  scheduleRows: ReportingScheduleRow[];
  scheduleEmptyText: string;
  scheduleDeliveryPlanSummary: string;
  scheduleDeliveryPlanRows: ReportingScheduleDeliveryPlanRow[];
  hasScheduleDeliveryPlanRows: boolean;
  scheduleDeliveryPlanListLabel: string;
  scheduleDeliveryPlanEmptyText: string;
}

interface ReportingScheduleManagementPanelProps {
  model: ReportingScheduleManagementModel;
  scheduleDraft: ReportingScheduleDraftState;
  distributionOptions: ReportPackDistributionRecord[];
  datasetSources: ReportWriterDatasetSource[];
  status: ReportingCommandStatus | null;
  runningScheduleActionId: string | null;
  onDraftChange: (field: ReportingScheduleDraftField, value: string) => void;
  onToggleFormat: (format: ReportingScheduleArtifactFormat, isSelected: boolean) => void;
  onStageTarget: () => void;
  onRemoveTarget: (distributionId: string) => void;
  onSaveDraft: () => void | Promise<void>;
  onRunDue: () => void | Promise<void>;
  onScheduleAction: (schedule: ReportingScheduleRow, action: "pause" | "resume" | "run") => void | Promise<void>;
  onSchedulePlanRun: (plan: ReportingScheduleDeliveryPlanRow) => void | Promise<void>;
}

export function ReportingScheduleManagementPanel({
  model,
  scheduleDraft,
  distributionOptions,
  datasetSources,
  status,
  runningScheduleActionId,
  onDraftChange,
  onToggleFormat,
  onStageTarget,
  onRemoveTarget,
  onSaveDraft,
  onRunDue,
  onScheduleAction,
  onSchedulePlanRun
}: ReportingScheduleManagementPanelProps) {
  return (
    <Card className="panel-surface">
      <CardHeader>
        <div className="eyebrow-label">Scheduling</div>
        <CardTitle>Reporting schedules</CardTitle>
        <CardDescription>{model.scheduleSummary}</CardDescription>
      </CardHeader>
      <CardContent className="space-y-3">
        <div role="group" aria-label="Schedule report distribution" className="rounded-md border border-border/70 bg-background/30 px-3 py-3">
          <div className="flex flex-wrap items-start justify-between gap-3">
            <div className="min-w-0">
              <h4 className="text-sm font-semibold text-foreground">Schedule delivery</h4>
              <p className="mt-1 text-xs leading-5 text-muted-foreground">
                Save or update governed report-pack schedules with PDF/XLSX/CSV delivery targets.
              </p>
            </div>
            <Badge variant="outline">{scheduleDraft.deliveryMode}</Badge>
          </div>
          <div className="mt-3 grid gap-2 md:grid-cols-3">
            <label className="space-y-1">
              <span className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">Schedule ID</span>
              <Input
                value={scheduleDraft.scheduleId}
                onChange={(event) => onDraftChange("scheduleId", event.target.value)}
                aria-label="Reporting schedule ID"
                className="font-mono"
              />
            </label>
            <label className="space-y-1">
              <span className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">Template ID</span>
              <Input
                value={scheduleDraft.templateId}
                onChange={(event) => onDraftChange("templateId", event.target.value)}
                aria-label="Reporting schedule template ID"
                className="font-mono"
              />
            </label>
            <label className="space-y-1">
              <span className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">Cron</span>
              <Input
                value={scheduleDraft.cronExpression}
                onChange={(event) => onDraftChange("cronExpression", event.target.value)}
                aria-label="Reporting schedule cron expression"
                className="font-mono"
              />
            </label>
            <label className="space-y-1">
              <span className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">Next as of</span>
              <Input
                value={scheduleDraft.nextAsOfDate}
                onChange={(event) => onDraftChange("nextAsOfDate", event.target.value)}
                aria-label="Reporting schedule next as-of date"
                className="font-mono"
              />
            </label>
            <label className="space-y-1">
              <span className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">Due UTC</span>
              <Input
                value={scheduleDraft.dueAtUtc}
                onChange={(event) => onDraftChange("dueAtUtc", event.target.value)}
                aria-label="Reporting schedule due at UTC"
                className="font-mono"
              />
            </label>
            <label className="space-y-1">
              <span className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">Retries</span>
              <Input
                type="number"
                min={0}
                value={scheduleDraft.maxRetries}
                onChange={(event) => onDraftChange("maxRetries", event.target.value)}
                aria-label="Reporting schedule max retries"
                className="font-mono"
              />
            </label>
            <label className="space-y-1">
              <span className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">Requested by</span>
              <Input
                value={scheduleDraft.requestedBy}
                onChange={(event) => onDraftChange("requestedBy", event.target.value)}
                aria-label="Reporting schedule requested by"
                className="font-mono"
              />
            </label>
            <label className="space-y-1">
              <span className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">Dataset source</span>
              <Select
                value={scheduleDraft.datasetSourceId}
                onChange={(event) => onDraftChange("datasetSourceId", event.target.value)}
                aria-label="Reporting schedule dataset source"
              >
                <option value="">Default retained dataset</option>
                {datasetSources.map((source) => (
                  <option key={source.sourceId} value={source.sourceId}>
                    {source.label} ({source.rowCount})
                  </option>
                ))}
              </Select>
            </label>
            <label className="space-y-1">
              <span className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">Distribution</span>
              <Select
                value={scheduleDraft.distributionId}
                onChange={(event) => onDraftChange("distributionId", event.target.value)}
                aria-label="Reporting schedule distribution"
              >
                {distributionOptions.some((target) => target.distributionId === scheduleDraft.distributionId) ? null : (
                  <option value={scheduleDraft.distributionId}>{scheduleDraft.distributionId}</option>
                )}
                {distributionOptions.map((target) => (
                  <option key={target.distributionId} value={target.distributionId}>
                    {target.recipient}
                  </option>
                ))}
              </Select>
            </label>
            <label className="space-y-1">
              <span className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">Delivery mode</span>
              <Select
                value={scheduleDraft.deliveryMode}
                onChange={(event) => onDraftChange("deliveryMode", event.target.value)}
                aria-label="Reporting schedule delivery mode"
              >
                {reportingScheduleDeliveryModes.map((mode) => (
                  <option key={mode} value={mode}>{mode}</option>
                ))}
              </Select>
            </label>
          </div>
          <label className="mt-2 block space-y-1">
            <span className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">Description</span>
            <Input
              value={scheduleDraft.description}
              onChange={(event) => onDraftChange("description", event.target.value)}
              aria-label="Reporting schedule description"
            />
          </label>
          <label className="mt-2 block space-y-1">
            <span className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">Delivery note</span>
            <Input
              value={scheduleDraft.deliveryNote}
              onChange={(event) => onDraftChange("deliveryNote", event.target.value)}
              aria-label="Reporting schedule delivery note"
            />
          </label>
          <div className="mt-3 flex flex-wrap items-center justify-between gap-3">
            <div role="group" aria-label="Reporting schedule formats" className="flex flex-wrap gap-2">
              {reportingScheduleArtifactFormats.map((format) => (
                <div
                  key={format}
                  className="rounded-sm border border-border/70 bg-secondary/25 px-2.5 py-1.5 text-xs text-foreground"
                >
                  <Checkbox
                    checked={scheduleDraft.formats[format]}
                    onCheckedChange={(checked) => onToggleFormat(format, checked === true)}
                    aria-label={`Reporting schedule ${format} format`}
                    label={format}
                  />
                </div>
              ))}
            </div>
            <Button
              type="button"
              size="sm"
              variant="outline"
              disabled={Boolean(runningScheduleActionId)}
              onClick={onStageTarget}
              aria-label="Stage reporting schedule delivery target"
            >
              <Plus className="h-4 w-4" aria-hidden="true" />
              Stage target
            </Button>
            <Button
              type="button"
              size="sm"
              busy={runningScheduleActionId === "schedule-draft:save"}
              busyLabel="Saving"
              disabled={Boolean(runningScheduleActionId)}
              onClick={() => void onSaveDraft()}
              aria-label="Save reporting schedule"
            >
              <Send className="h-4 w-4" aria-hidden="true" />
              Save schedule
            </Button>
            <Button
              type="button"
              size="sm"
              variant="outline"
              busy={runningScheduleActionId === "schedule-due:run"}
              busyLabel="Running"
              disabled={Boolean(runningScheduleActionId)}
              onClick={() => void onRunDue()}
              aria-label="Run due reporting schedules"
            >
              <RotateCcw className="h-4 w-4" aria-hidden="true" />
              Run due
            </Button>
          </div>
          {scheduleDraft.deliveryTargets.length > 0 ? (
            <div className="mt-3 rounded-md border border-border/70 bg-background/30 px-3 py-2">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <span className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">Staged delivery targets</span>
                <Badge variant="outline">{scheduleDraft.deliveryTargets.length}</Badge>
              </div>
              <ul aria-label="Staged reporting schedule delivery targets" className="mt-2 space-y-1.5">
                {scheduleDraft.deliveryTargets.map((target) => (
                  <li
                    key={target.distributionId}
                    className="flex flex-wrap items-center justify-between gap-2 rounded-sm border border-border/70 bg-secondary/20 px-2 py-1.5 text-xs"
                  >
                    <span className="min-w-0">
                      <span className="block break-all font-mono text-foreground">{target.distributionId}</span>
                      <span className="mt-0.5 block text-muted-foreground">
                        {target.deliveryMode} · {formatScheduleDraftTargetFormats(target)}
                      </span>
                    </span>
                    <Button
                      type="button"
                      size="icon"
                      variant="ghost"
                      disabled={Boolean(runningScheduleActionId)}
                      onClick={() => onRemoveTarget(target.distributionId)}
                      aria-label={`Remove staged delivery target ${target.distributionId}`}
                    >
                      <Trash2 className="h-4 w-4" aria-hidden="true" />
                    </Button>
                  </li>
                ))}
              </ul>
            </div>
          ) : null}
        </div>
        {model.hasScheduleRows ? (
          <div role="list" aria-label={model.scheduleListLabel} className="space-y-2">
            {model.scheduleRows.map((schedule) => (
              <div
                key={schedule.id}
                role="listitem"
                aria-label={schedule.ariaLabel}
                className="rounded-md border border-border/70 bg-secondary/20 px-3 py-2"
              >
                <div className="flex flex-wrap items-start justify-between gap-3">
                  <span className="min-w-0">
                    <span className="block font-semibold text-foreground">{schedule.templateId}</span>
                    <span className="mt-1 block break-all font-mono text-[11px] text-muted-foreground">
                      {schedule.id}
                    </span>
                  </span>
                  <Badge variant={schedule.stateVariant}>{schedule.state}</Badge>
                </div>
                <p className="mt-2 text-xs leading-5 text-muted-foreground">{schedule.description}</p>
                <dl className="mt-3 grid gap-2 sm:grid-cols-2">
                  <ReportingScheduleField label="Cron" value={schedule.cronLabel} />
                  <ReportingScheduleField label="Due" value={schedule.dueLabel} />
                  <ReportingScheduleField label="As of" value={schedule.nextAsOfLabel} />
                  <ReportingScheduleField label="Last run" value={schedule.lastRunLabel} />
                  <ReportingScheduleField label="Delivery" value={schedule.deliveryTargetLabel} />
                  <ReportingScheduleField label="Dataset" value={schedule.datasetSourceLabel} />
                </dl>
                <div className="mt-3 flex flex-wrap items-center gap-2">
                  <Badge variant="outline">{schedule.runCountLabel}</Badge>
                  <Button
                    size="sm"
                    variant="outline"
                    busy={runningScheduleActionId === `${schedule.id}:run`}
                    busyLabel="Running"
                    disabled={Boolean(runningScheduleActionId)}
                    onClick={() => void onScheduleAction(schedule, "run")}
                  >
                    Run now
                  </Button>
                  <Button
                    size="sm"
                    variant="outline"
                    busy={runningScheduleActionId === `${schedule.id}:pause`}
                    busyLabel="Pausing"
                    disabled={!schedule.canPause || Boolean(runningScheduleActionId)}
                    disabledReason={schedule.canPause ? null : "Only active schedules can be paused."}
                    onClick={() => void onScheduleAction(schedule, "pause")}
                  >
                    Pause
                  </Button>
                  <Button
                    size="sm"
                    variant="outline"
                    busy={runningScheduleActionId === `${schedule.id}:resume`}
                    busyLabel="Resuming"
                    disabled={!schedule.canResume || Boolean(runningScheduleActionId)}
                    disabledReason={schedule.canResume ? null : "Only paused schedules can be resumed."}
                    onClick={() => void onScheduleAction(schedule, "resume")}
                  >
                    Resume
                  </Button>
                </div>
              </div>
            ))}
          </div>
        ) : (
          <p role="status" className="rounded-md border border-warning/30 bg-warning/10 px-3 py-2 text-sm text-warning">
            {model.scheduleEmptyText}
          </p>
        )}
        {status ? (
          <ReportingCommandStatusView status={status} />
        ) : null}
        <div className="border-t border-border/70 pt-3">
          <div className="flex flex-wrap items-start justify-between gap-3">
            <div className="min-w-0">
              <h4 className="text-sm font-semibold text-foreground">Delivery plans</h4>
              <p className="mt-1 text-xs leading-5 text-muted-foreground">{model.scheduleDeliveryPlanSummary}</p>
            </div>
            <Badge variant="outline">{model.scheduleDeliveryPlanRows.length} target{model.scheduleDeliveryPlanRows.length === 1 ? "" : "s"}</Badge>
          </div>
          {model.hasScheduleDeliveryPlanRows ? (
            <div role="list" aria-label={model.scheduleDeliveryPlanListLabel} className="mt-3 space-y-2">
              {model.scheduleDeliveryPlanRows.map((plan) => (
                <div
                  key={plan.id}
                  role="listitem"
                  aria-label={plan.ariaLabel}
                  className="rounded-md border border-border/70 bg-background/40 px-3 py-2"
                >
                  <div className="flex flex-wrap items-start justify-between gap-3">
                    <span className="min-w-0">
                      <span className="block font-semibold text-foreground">{plan.recipient}</span>
                      <span className="mt-1 block text-xs text-muted-foreground">
                        {plan.recipientRole} · {plan.channel}
                      </span>
                    </span>
                    <Badge variant={plan.readinessVariant}>{plan.deliveryMode}</Badge>
                  </div>
                  <p className="mt-2 text-xs leading-5 text-muted-foreground">{plan.readinessSummary}</p>
                  <dl className="mt-3 grid gap-2 sm:grid-cols-2">
                    <ReportingScheduleField label="Formats" value={plan.formatsLabel} />
                    <ReportingScheduleField label="Due" value={plan.dueLabel} />
                    <ReportingScheduleField label="As of" value={plan.nextAsOfLabel} />
                    <ReportingScheduleField label="Owner" value={plan.ownerLabel} />
                    <ReportingScheduleField label="Last delivery" value={plan.lastDeliveryLabel} />
                    <ReportingScheduleField label="Access expiry" value={plan.accessExpiryLabel} />
                    <ReportingScheduleField label="Access" value={plan.accessSummaryLabel} />
                    <ReportingScheduleField label="Channel" value={plan.channelSummaryLabel} />
                    <ReportingScheduleField label="Downloads" value={plan.downloadSummaryLabel} />
                    <ReportingScheduleField label="Notifications" value={plan.notificationSummaryLabel} />
                    <ReportingScheduleField label="Writer dataset" value={plan.reportWriterDatasetSummaryLabel} />
                    <ReportingScheduleField label="Writer grids" value={plan.reportWriterGridSummaryLabel} />
                    <ReportingScheduleField label="Artifact integrity" value={plan.integrityLabel} />
                    <ReportingScheduleField label="Entitlement" value={plan.entitlementLabel} />
                    <ReportingScheduleField label="Branding" value={plan.brandingLabel} />
                    <ReportingScheduleField label="Schedule" value={plan.scheduleId} />
                  </dl>
                  {plan.integritySummary ? (
                    <p className="mt-2 text-xs leading-5 text-muted-foreground">{plan.integritySummary}</p>
                  ) : null}
                  {plan.note ? (
                    <p className="mt-2 text-xs leading-5 text-muted-foreground">{plan.note}</p>
                  ) : null}
                  <div className="mt-2 flex flex-wrap gap-2 text-[11px]">
                    <a className="break-all font-mono text-primary underline-offset-2 hover:underline" href={plan.route}>
                      {plan.route}
                    </a>
                  </div>
                  <div className="mt-3 flex flex-wrap items-center gap-2">
                    <Button
                      variant="outline"
                      size="sm"
                      busy={runningScheduleActionId === `${plan.id}:run`}
                      busyLabel="Running"
                      disabled={plan.readinessVariant !== "success" || Boolean(runningScheduleActionId)}
                      disabledReason={plan.readinessVariant !== "success" ? plan.readinessSummary : null}
                      onClick={() => void onSchedulePlanRun(plan)}
                    >
                      Run schedule for recipient
                    </Button>
                  </div>
                  {plan.lastDeliveryLinks.length > 0 ? (
                    <div className="mt-2 flex flex-wrap gap-1.5" aria-label={`${plan.id} retained delivery access links`}>
                      {plan.lastDeliveryLinks.map((link) => (
                        <a
                          key={link.id}
                          href={link.href}
                          aria-label={link.ariaLabel}
                          className="inline-flex min-w-0 items-center gap-1.5 rounded-sm border border-border/60 bg-secondary/20 px-2 py-1 text-[11px] text-muted-foreground hover:bg-secondary/45 focus:outline-none focus:ring-2 focus:ring-primary/40"
                        >
                          <span className="max-w-[12rem] truncate text-foreground">{link.label}</span>
                          <Badge variant="outline">{link.tokenLabel}</Badge>
                          {link.expiresLabel ? <span>{link.expiresLabel}</span> : null}
                        </a>
                      ))}
                    </div>
                  ) : plan.lastDeliveryHref ? (
                    <div className="mt-2 flex flex-wrap gap-2 text-[11px]">
                      <a className="break-all font-mono text-primary underline-offset-2 hover:underline" href={plan.lastDeliveryHref}>
                        {plan.lastDeliveryHref}
                      </a>
                    </div>
                  ) : null}
                  {plan.versionStamp ? (
                    <p className="mt-2 break-all font-mono text-[11px] text-muted-foreground">{plan.versionStamp}</p>
                  ) : null}
                </div>
              ))}
            </div>
          ) : (
            <p role="status" className="mt-3 rounded-md border border-warning/30 bg-warning/10 px-3 py-2 text-sm text-warning">
              {model.scheduleDeliveryPlanEmptyText}
            </p>
          )}
        </div>
      </CardContent>
    </Card>
  );
}

function formatScheduleDraftTargetFormats(target: ReportingScheduleDraftTarget): string {
  const formats = reportingScheduleArtifactFormats.filter((format) => target.formats[format]);
  return formats.length > 0 ? formats.join("/") : "No formats";
}
