import { Plus, Send, Trash2 } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import { TechnicalDetails } from "@/components/ui/technical-details";
import { safeReportingHref } from "@/lib/reporting-link-safety";
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
  ReportingScheduleRow,
  ReportingTemplateRow
} from "@/screens/reporting-screen.view-model";
import type {
  ReportRunParameterDraftField,
  ReportRunParameterDraftState
} from "@/screens/report-run-parameters-screen.view-model";

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
  | "recipientPrincipalId"
  | "recipientPrincipalKind"
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
  recipientPrincipalId: string;
  recipientPrincipalKind: ReportingScheduleRecipientPrincipalKind | "";
  description: string;
  deliveryNote: string;
  formats: Record<ReportingScheduleArtifactFormat, boolean>;
  deliveryTargets: ReportingScheduleDraftTarget[];
  datasetSourceId: string;
  templateVersion: number;
  runParameters: ReportRunParameterDraftState;
}

export interface ReportingScheduleDraftTarget {
  distributionId: string;
  deliveryMode: ReportPackDeliveryMode;
  recipientPrincipalId: string;
  recipientPrincipalKind: ReportingScheduleRecipientPrincipalKind | "";
  deliveryNote: string;
  formats: Record<ReportingScheduleArtifactFormat, boolean>;
}

export type ReportingScheduleRecipientPrincipalKind = "User" | "Group" | "Company";

export const reportingScheduleArtifactFormats: ReportingScheduleArtifactFormat[] = ["Pdf", "Xlsx", "Csv"];
export const reportingScheduleDeliveryModes: ReportPackDeliveryMode[] = ["SecurePortal", "EmailLink", "EvidenceVault", "InternalRoute"];
const reportingScheduleCadences = [
  { value: "0 8 * * 1-5", label: "Every weekday" },
  { value: "0 8 * * 1", label: "Every Monday" },
  { value: "0 8 1 * *", label: "First day of each month" },
  { value: "0 8 1 1,4,7,10 *", label: "Quarterly" }
] as const;

export function buildReportingScheduleTemplateOptions(templates: readonly ReportingTemplateRow[]): ReportingTemplateRow[] {
  return [...templates]
    .filter((template) => template.canRunOnDemand)
    .sort((left, right) => left.name.localeCompare(right.name) || right.versionNumber - left.versionNumber);
}

function buildScheduleTimingGuidance(cronExpression: string): string {
  if (cronExpression === "0 8 1 * *") {
    return "Runs on the first day of each month. The report uses the period-end date selected beside the cadence.";
  }

  return "The cadence controls when the schedule runs; the report period end controls the data cut used by that run.";
}

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
  templates: ReportingTemplateRow[];
  status: ReportingCommandStatus | null;
  runningScheduleActionId: string | null;
  onDraftChange: (field: ReportingScheduleDraftField, value: string) => void;
  onRunParameterChange: (field: ReportRunParameterDraftField, value: string | boolean) => void;
  onToggleFormat: (format: ReportingScheduleArtifactFormat, isSelected: boolean) => void;
  onStageTarget: () => void;
  onRemoveTarget: (distributionId: string) => void;
  onSaveDraft: () => void | Promise<void>;
  onScheduleAction: (schedule: ReportingScheduleRow, action: "pause" | "resume" | "run") => void | Promise<void>;
  onSchedulePlanRun: (plan: ReportingScheduleDeliveryPlanRow) => void | Promise<void>;
}

export function ReportingScheduleManagementPanel({
  model,
  scheduleDraft,
  distributionOptions,
  datasetSources,
  templates,
  status,
  runningScheduleActionId,
  onDraftChange,
  onRunParameterChange,
  onToggleFormat,
  onStageTarget,
  onRemoveTarget,
  onSaveDraft,
  onScheduleAction,
  onSchedulePlanRun
}: ReportingScheduleManagementPanelProps) {
  const scheduleTemplateOptions = buildReportingScheduleTemplateOptions(templates);
  const selectedScheduleTemplate = scheduleTemplateOptions.find((template) =>
    template.templateName === scheduleDraft.templateId && template.versionNumber === scheduleDraft.templateVersion) ?? null;
  const selectedScheduleTemplateRowId = selectedScheduleTemplate?.id
    ?? `${scheduleDraft.templateId}:${scheduleDraft.templateVersion}`;
  const hasExplicitTypedRecipient = Boolean(
    scheduleDraft.recipientPrincipalId.trim() && scheduleDraft.recipientPrincipalKind
  );

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
            <Badge variant="outline">{presentScheduleDeliveryMode(scheduleDraft.deliveryMode)}</Badge>
          </div>
          <div className="mt-3 grid gap-2 sm:grid-cols-2">
            <label className="min-w-0 space-y-1 sm:col-span-2">
              <span className="text-xs font-medium text-muted-foreground">Report template</span>
              <Select
                value={selectedScheduleTemplateRowId}
                onChange={(event) => onDraftChange("templateId", event.target.value)}
                aria-label="Reporting schedule template ID"
              >
                {selectedScheduleTemplate ? null : (
                  <option value={selectedScheduleTemplateRowId} disabled>Current scheduled template version (review required)</option>
                )}
                {scheduleTemplateOptions.map((template) => (
                  <option key={template.id} value={template.id}>{template.name} · v{template.version}</option>
                ))}
              </Select>
            </label>
            <label className="min-w-0 space-y-1">
              <span className="text-xs font-medium text-muted-foreground">Run cadence</span>
              <Select
                value={scheduleDraft.cronExpression}
                onChange={(event) => onDraftChange("cronExpression", event.target.value)}
                aria-label="Reporting schedule cadence"
                aria-describedby="reporting-schedule-timing-guidance"
              >
                {reportingScheduleCadences.some((cadence) => cadence.value === scheduleDraft.cronExpression) ? null : (
                  <option value={scheduleDraft.cronExpression}>Custom cadence</option>
                )}
                {reportingScheduleCadences.map((cadence) => (
                  <option key={cadence.value} value={cadence.value}>{cadence.label}</option>
                ))}
              </Select>
            </label>
            <label className="min-w-0 space-y-1">
              <span className="text-xs font-medium text-muted-foreground">Next report period end</span>
              <Input
                type="date"
                value={scheduleDraft.nextAsOfDate}
                onChange={(event) => onDraftChange("nextAsOfDate", event.target.value)}
                aria-label="Reporting schedule next report period end"
                aria-describedby="reporting-schedule-timing-guidance"
              />
            </label>
            <p id="reporting-schedule-timing-guidance" className="text-xs leading-5 text-muted-foreground sm:col-span-2">
              {buildScheduleTimingGuidance(scheduleDraft.cronExpression)}
            </p>
            <label className="min-w-0 space-y-1">
              <span className="text-xs font-medium text-muted-foreground">Distribution</span>
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
            <label className="min-w-0 space-y-1">
              <span className="text-xs font-medium text-muted-foreground">Delivery mode</span>
              <Select
                value={scheduleDraft.deliveryMode}
                onChange={(event) => onDraftChange("deliveryMode", event.target.value)}
                aria-label="Reporting schedule delivery mode"
              >
                {reportingScheduleDeliveryModes.map((mode) => (
                  <option key={mode} value={mode}>{presentScheduleDeliveryMode(mode)}</option>
                ))}
              </Select>
            </label>
            <label className="min-w-0 space-y-1">
              <span className="text-xs font-medium text-muted-foreground">Recipient kind</span>
              <Select
                value={scheduleDraft.recipientPrincipalKind}
                onChange={(event) => onDraftChange("recipientPrincipalKind", event.target.value)}
                aria-label="Reporting schedule recipient principal kind"
                required
              >
                <option value="">Select recipient kind</option>
                <option value="User">User</option>
                <option value="Group">Group</option>
                <option value="Company">Company</option>
              </Select>
            </label>
            <label className="min-w-0 space-y-1">
              <span className="text-xs font-medium text-muted-foreground">Recipient principal ID</span>
              <Input
                value={scheduleDraft.recipientPrincipalId}
                onChange={(event) => onDraftChange("recipientPrincipalId", event.target.value)}
                aria-label="Reporting schedule recipient principal ID"
                autoComplete="off"
                required
              />
            </label>
          </div>
          <TechnicalDetails
            label="Advanced schedule controls"
            description="System identifiers, exact timing, retry policy, retained dataset selection, and the immutable run scope."
            className="mt-3"
          >
            <div className="grid gap-2 md:grid-cols-3">
              <label className="space-y-1">
                <span className="text-xs font-medium text-muted-foreground">Schedule ID</span>
                <Input value={scheduleDraft.scheduleId} onChange={(event) => onDraftChange("scheduleId", event.target.value)} aria-label="Reporting schedule ID" className="font-mono" />
              </label>
              <label className="space-y-1">
                <span className="text-xs font-medium text-muted-foreground">Cron expression</span>
                <Input value={scheduleDraft.cronExpression} onChange={(event) => onDraftChange("cronExpression", event.target.value)} aria-label="Reporting schedule cron expression" className="font-mono" />
              </label>
              <label className="space-y-1">
                <span className="text-xs font-medium text-muted-foreground">Due at UTC</span>
                <Input value={scheduleDraft.dueAtUtc} onChange={(event) => onDraftChange("dueAtUtc", event.target.value)} aria-label="Reporting schedule due at UTC" className="font-mono" />
              </label>
              <label className="space-y-1">
                <span className="text-xs font-medium text-muted-foreground">Retries</span>
                <Input type="number" min={0} value={scheduleDraft.maxRetries} onChange={(event) => onDraftChange("maxRetries", event.target.value)} aria-label="Reporting schedule max retries" className="font-mono" />
              </label>
              <label className="space-y-1">
                <span className="text-xs font-medium text-muted-foreground">Requested by</span>
                <Input value={scheduleDraft.requestedBy} onChange={(event) => onDraftChange("requestedBy", event.target.value)} aria-label="Reporting schedule requested by" className="font-mono" />
              </label>
              <label className="space-y-1">
                <span className="text-xs font-medium text-muted-foreground">Dataset source</span>
                <Select value={scheduleDraft.datasetSourceId} onChange={(event) => onDraftChange("datasetSourceId", event.target.value)} aria-label="Reporting schedule dataset source">
                  <option value="">Default retained dataset</option>
                  {datasetSources.map((source) => (
                    <option key={source.sourceId} value={source.sourceId}>{source.label} ({source.rowCount})</option>
                  ))}
                </Select>
              </label>
            </div>
            <div className="mt-4 border-t border-border/70 pt-4">
              <h5 className="text-xs font-semibold text-foreground">Run scope and output</h5>
              <p className="mt-1 text-xs leading-5 text-muted-foreground">
                These parameters are retained with every scheduled run and revalidated by the server before execution.
              </p>
              <div className="mt-3 grid gap-2 md:grid-cols-3">
                <label className="space-y-1">
                  <span className="text-xs font-medium text-muted-foreground">Fund profile</span>
                  <Input value={scheduleDraft.runParameters.fundProfileId} onChange={(event) => onRunParameterChange("fundProfileId", event.target.value)} aria-label="Reporting schedule fund profile" required />
                </label>
                <label className="space-y-1">
                  <span className="text-xs font-medium text-muted-foreground">Entity scope</span>
                  <Select value={scheduleDraft.runParameters.entityScopeKind} onChange={(event) => onRunParameterChange("entityScopeKind", event.target.value)} aria-label="Reporting schedule entity scope">
                    <option value="AllEntities">All entities</option>
                    <option value="Entity">Entity</option>
                    <option value="Portfolio">Portfolio</option>
                    <option value="Investor">Investor</option>
                  </Select>
                </label>
                {scheduleDraft.runParameters.entityScopeKind === "Entity" ? (
                  <label className="space-y-1">
                    <span className="text-xs font-medium text-muted-foreground">Entity ID</span>
                    <Input value={scheduleDraft.runParameters.entityId} onChange={(event) => onRunParameterChange("entityId", event.target.value)} aria-label="Reporting schedule entity ID" required />
                  </label>
                ) : null}
                {scheduleDraft.runParameters.entityScopeKind === "Portfolio" ? (
                  <label className="space-y-1">
                    <span className="text-xs font-medium text-muted-foreground">Portfolio ID</span>
                    <Input value={scheduleDraft.runParameters.portfolioId} onChange={(event) => onRunParameterChange("portfolioId", event.target.value)} aria-label="Reporting schedule portfolio ID" required />
                  </label>
                ) : null}
                {scheduleDraft.runParameters.entityScopeKind === "Investor" ? (
                  <label className="space-y-1">
                    <span className="text-xs font-medium text-muted-foreground">Investor ID</span>
                    <Input value={scheduleDraft.runParameters.investorId} onChange={(event) => onRunParameterChange("investorId", event.target.value)} aria-label="Reporting schedule investor ID" required />
                  </label>
                ) : null}
                <label className="space-y-1">
                  <span className="text-xs font-medium text-muted-foreground">Accounting period ID</span>
                  <Input value={scheduleDraft.runParameters.periodId} onChange={(event) => onRunParameterChange("periodId", event.target.value)} aria-label="Reporting schedule accounting period ID" required />
                </label>
                <label className="space-y-1">
                  <span className="text-xs font-medium text-muted-foreground">Ledger book code</span>
                  <Input value={scheduleDraft.runParameters.ledgerBookCode} onChange={(event) => onRunParameterChange("ledgerBookCode", event.target.value)} aria-label="Reporting schedule ledger book code" />
                </label>
                <label className="space-y-1">
                  <span className="text-xs font-medium text-muted-foreground">Ledger book ID</span>
                  <Input value={scheduleDraft.runParameters.ledgerBookId} onChange={(event) => onRunParameterChange("ledgerBookId", event.target.value)} aria-label="Reporting schedule ledger book ID" />
                </label>
                <label className="space-y-1">
                  <span className="text-xs font-medium text-muted-foreground">Accounting basis</span>
                  <Select value={scheduleDraft.runParameters.accountingBasis} onChange={(event) => onRunParameterChange("accountingBasis", event.target.value)} aria-label="Reporting schedule accounting basis">
                    <option value="Gaap">GAAP</option>
                    <option value="Tax">Tax</option>
                    <option value="Management">Management</option>
                    <option value="Cash">Cash</option>
                    <option value="Statutory">Statutory</option>
                  </Select>
                </label>
                <label className="space-y-1">
                  <span className="text-xs font-medium text-muted-foreground">Presentation currency</span>
                  <Input value={scheduleDraft.runParameters.presentationCurrency} onChange={(event) => onRunParameterChange("presentationCurrency", event.target.value)} aria-label="Reporting schedule presentation currency" maxLength={3} required />
                </label>
                <label className="space-y-1">
                  <span className="text-xs font-medium text-muted-foreground">Consolidation level</span>
                  <Select value={scheduleDraft.runParameters.consolidationLevel} onChange={(event) => onRunParameterChange("consolidationLevel", event.target.value)} aria-label="Reporting schedule consolidation level">
                    <option value="Fund">Fund</option>
                    <option value="Entity">Entity</option>
                    <option value="Portfolio">Portfolio</option>
                    <option value="Investor">Investor</option>
                  </Select>
                </label>
                <label className="space-y-1">
                  <span className="text-xs font-medium text-muted-foreground">Output format</span>
                  <Select value={scheduleDraft.runParameters.outputFormat} onChange={(event) => onRunParameterChange("outputFormat", event.target.value)} aria-label="Reporting schedule output format">
                    <option value="Pdf">PDF</option>
                    <option value="Xlsx">XLSX</option>
                    <option value="Csv">CSV</option>
                    <option value="EvidenceVault">Evidence Vault</option>
                    <option value="ClientPackage">Client Package</option>
                  </Select>
                </label>
                <label className="space-y-1">
                  <span className="text-xs font-medium text-muted-foreground">Finality</span>
                  <Select value={scheduleDraft.runParameters.finality} onChange={(event) => onRunParameterChange("finality", event.target.value)} aria-label="Reporting schedule finality">
                    <option value="Draft">Draft</option>
                    <option value="Final">Final</option>
                  </Select>
                </label>
              </div>
              <div className="mt-3 flex flex-wrap gap-4">
                <Checkbox label="Schedule supporting schedules" checked={scheduleDraft.runParameters.includeSupportingSchedules} onCheckedChange={(checked) => onRunParameterChange("includeSupportingSchedules", checked)} />
                <Checkbox label="Schedule evidence appendix" checked={scheduleDraft.runParameters.includeEvidenceAppendix} onCheckedChange={(checked) => onRunParameterChange("includeEvidenceAppendix", checked)} />
              </div>
              <label className="mt-3 block space-y-1">
                <span className="text-xs font-medium text-muted-foreground">Ledger dimensions (JSON)</span>
                <textarea
                  className="min-h-24 w-full rounded-sm border border-input bg-background px-3 py-2 font-mono text-sm text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
                  value={scheduleDraft.runParameters.dimensionsJson}
                  onChange={(event) => onRunParameterChange("dimensionsJson", event.target.value)}
                  aria-label="Reporting schedule ledger dimensions (JSON)"
                  spellCheck={false}
                />
                <span className="block text-xs leading-5 text-muted-foreground">
                  Use supported scalar dimension IDs and an optional externalGlDimensions string map. Book IDs must be UUIDs; a code-only ledger selection can be resolved by the server.
                </span>
              </label>
              <label className="mt-3 block space-y-1">
                <span className="text-xs font-medium text-muted-foreground">Template parameters (JSON)</span>
                <textarea
                  className="min-h-20 w-full rounded-sm border border-input bg-background px-3 py-2 font-mono text-sm text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
                  value={scheduleDraft.runParameters.templateParametersJson}
                  onChange={(event) => onRunParameterChange("templateParametersJson", event.target.value)}
                  aria-label="Reporting schedule template parameters (JSON)"
                />
              </label>
            </div>
          </TechnicalDetails>
          <label className="mt-2 block space-y-1">
            <span className="text-xs font-medium text-muted-foreground">Description</span>
            <Input
              value={scheduleDraft.description}
              onChange={(event) => onDraftChange("description", event.target.value)}
              aria-label="Reporting schedule description"
            />
          </label>
          <label className="mt-2 block space-y-1">
            <span className="text-xs font-medium text-muted-foreground">Delivery note</span>
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
              disabled={Boolean(runningScheduleActionId) || !hasExplicitTypedRecipient}
              disabledReason={!hasExplicitTypedRecipient
                ? "Select a recipient kind and enter its explicit principal ID before staging this target."
                : null}
              onClick={onStageTarget}
              aria-label="Stage reporting schedule delivery target"
            >
              <Plus className="h-4 w-4" aria-hidden="true" />
              Add recipient
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
          </div>
          <p role="status" className="mt-3 rounded-md border border-primary/25 bg-primary/10 px-3 py-2 text-xs leading-5 text-primary">
            Due schedules are leased and executed by Meridian's hosted reporting worker. Use Run now on one saved schedule only for an intentional operator-triggered run.
          </p>
          {scheduleDraft.deliveryTargets.length > 0 ? (
            <div className="mt-3 rounded-md border border-border/70 bg-background/30 px-3 py-2">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <span className="text-xs font-medium text-muted-foreground">Staged delivery targets</span>
                <Badge variant="outline">{scheduleDraft.deliveryTargets.length}</Badge>
              </div>
              <ul aria-label="Staged reporting schedule delivery targets" className="mt-2 space-y-1.5">
                {scheduleDraft.deliveryTargets.map((target) => (
                  <li
                    key={target.distributionId}
                    className="flex flex-wrap items-center justify-between gap-2 rounded-sm border border-border/70 bg-secondary/20 px-2 py-1.5 text-xs"
                  >
                    <span className="min-w-0">
                      <span className="block font-medium text-foreground">
                        {distributionOptions.find((option) => option.distributionId === target.distributionId)?.recipient ?? "Configured recipient"}
                      </span>
                      <span className="mt-0.5 block text-muted-foreground">
                        {target.recipientPrincipalKind || "Missing kind"}:{target.recipientPrincipalId || "Missing principal"}
                        {" · "}{presentScheduleDeliveryMode(target.deliveryMode)} · {formatScheduleDraftTargetFormats(target)}
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
                    <span className="block font-semibold text-foreground">
                      {templates.find((template) => template.templateName === schedule.templateId)?.name ?? "Scheduled report"}
                    </span>
                  </span>
                  <span className="flex flex-wrap items-center gap-1.5">
                    <Badge variant={schedule.stateVariant}>{schedule.state}</Badge>
                    <Badge variant={schedule.releaseGateVariant}>
                      {schedule.releaseGateVariant === "success" ? "Release delivery ready" : "Release delivery gated"}
                    </Badge>
                  </span>
                </div>
                <p className="mt-2 text-xs leading-5 text-muted-foreground">{schedule.description}</p>
                <dl className="mt-3 grid gap-2 sm:grid-cols-2">
                  <ReportingScheduleField label="Next scheduled run" value={schedule.dueLabel} />
                  <ReportingScheduleField label="Report period end" value={schedule.nextAsOfLabel} />
                </dl>
                <details className="mt-3 rounded-md border border-border/60 bg-background/35">
                  <summary className="cursor-pointer px-3 py-2 text-xs font-semibold text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40">
                    Schedule details
                  </summary>
                  <dl className="grid gap-2 border-t border-border/60 px-3 py-3 sm:grid-cols-2">
                    <ReportingScheduleField label="Schedule ID" value={schedule.id} />
                    <ReportingScheduleField label="Template ID" value={schedule.templateId} />
                    <ReportingScheduleField label="Cron" value={schedule.cronLabel} />
                    <ReportingScheduleField label="Last run" value={schedule.lastRunLabel} />
                    <ReportingScheduleField label="Delivery" value={schedule.deliveryTargetLabel} />
                    <ReportingScheduleField label="Dataset" value={schedule.datasetSourceLabel} />
                    <ReportingScheduleField label="Access policy snapshot" value={schedule.accessPolicySnapshotLabel} />
                    <ReportingScheduleField label="Release delivery gate" value={schedule.releaseGateLabel} />
                  </dl>
                  {schedule.releaseHandoffs.length > 0 ? (
                    <section
                      aria-label={`${schedule.id} release delivery handoff history`}
                      className="border-t border-border/60 px-3 py-3"
                    >
                      <h5 className="text-xs font-semibold text-foreground">Release delivery handoff history</h5>
                      <ul className="mt-2 space-y-2">
                        {schedule.releaseHandoffs.map((handoff) => (
                          <li
                            key={handoff.id}
                            aria-label={handoff.ariaLabel}
                            className="rounded-sm border border-border/60 bg-background/30 px-2.5 py-2"
                          >
                            <div className="flex flex-wrap items-center justify-between gap-2">
                              <span className="break-all font-mono text-xs text-foreground">{handoff.id}</span>
                              <Badge variant={handoff.stateVariant}>{handoff.state}</Badge>
                            </div>
                            <dl className="mt-2 grid gap-x-3 gap-y-1 text-xs sm:grid-cols-2">
                              <div><dt className="inline text-muted-foreground">Run: </dt><dd className="inline break-all font-mono">{handoff.runId}</dd></div>
                              <div><dt className="inline text-muted-foreground">Distribution: </dt><dd className="inline break-all font-mono">{handoff.distributionLabel}</dd></div>
                              <div><dt className="inline text-muted-foreground">Transport: </dt><dd className="inline break-all font-mono">{handoff.transportId}</dd></div>
                              <div><dt className="inline text-muted-foreground">Recipient: </dt><dd className="inline break-all">{handoff.recipientLabel}</dd></div>
                              <div><dt className="inline text-muted-foreground">Formats: </dt><dd className="inline">{handoff.formatsLabel}</dd></div>
                              <div><dt className="inline text-muted-foreground">Retained: </dt><dd className="inline">{handoff.createdLabel}</dd></div>
                              <div><dt className="inline text-muted-foreground">Queue: </dt><dd className="inline break-all">{handoff.enqueuedLabel}</dd></div>
                            </dl>
                          </li>
                        ))}
                      </ul>
                    </section>
                  ) : null}
                </details>
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
                    <Badge variant={plan.readinessVariant}>{presentScheduleDeliveryMode(plan.deliveryMode)}</Badge>
                  </div>
                  <p className="mt-2 text-xs leading-5 text-muted-foreground">{plan.readinessSummary}</p>
                  <dl className="mt-3 grid gap-2 sm:grid-cols-2">
                    <ReportingScheduleField label="Scheduled run" value={plan.dueLabel} />
                    <ReportingScheduleField label="Owner" value={plan.ownerLabel} />
                  </dl>
                  <details className="mt-3 rounded-md border border-border/60 bg-secondary/15">
                    <summary className="cursor-pointer px-3 py-2 text-xs font-semibold text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40">
                      Delivery metadata and retained access
                    </summary>
                    <div className="space-y-2 border-t border-border/60 px-3 py-3">
                      <dl className="grid gap-2 sm:grid-cols-2">
                        <ReportingScheduleField label="Formats" value={plan.formatsLabel} />
                        <ReportingScheduleField label="Report period end" value={plan.nextAsOfLabel} />
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
                        <p className="text-xs leading-5 text-muted-foreground">{plan.integritySummary}</p>
                      ) : null}
                      {plan.note ? (
                        <p className="text-xs leading-5 text-muted-foreground">{plan.note}</p>
                      ) : null}
                      {safeReportingHref(plan.route) ? (
                        <a
                          className="block break-all font-mono text-xs text-primary underline-offset-2 hover:underline"
                          href={safeReportingHref(plan.route)!}
                        >
                          Open retained schedule route
                        </a>
                      ) : (
                        <p className="text-xs text-warning">Unsafe schedule route suppressed.</p>
                      )}
                    </div>
                  </details>
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
                      {plan.lastDeliveryLinks.map((link) => {
                        const safeHref = safeReportingHref(link.href, {
                          requireOpaqueFragment: link.requiresOpaqueFragment
                        });
                        return safeHref ? (
                          <a
                            key={link.id}
                            href={safeHref}
                            aria-label={link.ariaLabel}
                            className="inline-flex min-h-9 min-w-0 items-center gap-1.5 rounded-sm border border-border/60 bg-secondary/20 px-2.5 py-1.5 text-xs text-muted-foreground hover:bg-secondary/45 focus:outline-none focus:ring-2 focus:ring-primary/40"
                          >
                            <span className="max-w-[12rem] truncate text-foreground">{link.label}</span>
                            <Badge variant="outline">{link.tokenLabel}</Badge>
                            {link.expiresLabel ? <span>{link.expiresLabel}</span> : null}
                          </a>
                        ) : (
                          <span key={link.id} className="inline-flex min-h-9 items-center rounded-sm border border-warning/40 px-2.5 py-1.5 text-xs text-warning">
                            {link.label} · unsafe link suppressed
                          </span>
                        );
                      })}
                    </div>
                  ) : safeReportingHref(plan.lastDeliveryHref) ? (
                    <div className="mt-2 flex flex-wrap gap-2 text-xs">
                      <a className="text-primary underline-offset-2 hover:underline" href={safeReportingHref(plan.lastDeliveryHref)!}>
                        Open retained delivery
                      </a>
                    </div>
                  ) : null}
                  {plan.versionStamp ? (
                    <TechnicalDetails label="Version details" className="mt-2">
                      <p className="break-all font-mono text-xs text-muted-foreground">{plan.versionStamp}</p>
                    </TechnicalDetails>
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

function presentScheduleDeliveryMode(mode: string): string {
  return mode
    .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
    .replace(/[_-]+/g, " ")
    .replace(/^./, (character) => character.toUpperCase());
}
