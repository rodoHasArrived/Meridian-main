import { type KeyboardEvent, useEffect, useRef, useState } from "react";
import { FileText, Landmark, Network, PencilLine } from "lucide-react";
import { Link, useLocation } from "react-router-dom";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { MetricCard } from "@/components/meridian/metric-card";
import { DenseDataTable, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import {
  apiPostJson,
  deliverReportPack,
  pauseReportingSchedule,
  resumeReportingSchedule,
  runReportingNow,
  runReportingScheduleNow
} from "@/lib/api";
import { describeApiError } from "@/lib/api-errors";
import { cn } from "@/lib/utils";
import {
  resolveReportPackProfileKeyCommand,
  useReportingScreenViewModel,
  type ReportingProfileRow,
  type ReportingRunActionRow,
  type ReportingRunStatusRow,
  type ReportingScheduleRow,
  type ReportingTemplateRow
} from "@/screens/reporting-screen.view-model";
import type { AccountingWorkspaceResponse, ReportingWorkflowEvidenceLink } from "@/types";

interface ReportingScreenProps {
  data: AccountingWorkspaceResponse | null;
}

interface ReportingCommandStatus {
  id: string;
  label: string;
  state: "running" | "success" | "error";
  message: string;
  details: string[];
}

const reportingProfileColumns: DenseDataTableColumn<ReportingProfileRow>[] = [
  {
    id: "profile",
    label: "Profile",
    render: (profile) => (
      <span className="block min-w-0">
        <span className="block font-semibold text-foreground">{profile.name}</span>
        <span className="mt-1 block break-all font-mono text-[11px] text-muted-foreground">{profile.id}</span>
      </span>
    )
  },
  {
    id: "target",
    label: "Target",
    render: (profile) => (
      <span className="block min-w-0">
        <span className="block font-medium text-foreground">{profile.targetLabel}</span>
        <span className="mt-1 block break-words text-xs leading-5 text-muted-foreground">{profile.description}</span>
      </span>
    )
  },
  {
    id: "format",
    label: "Format",
    render: (profile) => <span className="font-mono text-xs text-foreground">{profile.formatLabel}</span>
  },
  {
    id: "evidence",
    label: "Evidence",
    render: (profile) => (
      <span className="flex flex-wrap gap-1.5">
        {profile.badges.length > 0 ? profile.badges.map((badge) => (
          <Badge key={badge.label} variant={badge.variant}>
            {badge.label}
          </Badge>
        )) : (
          <Badge variant="outline">Evidence pending</Badge>
        )}
      </span>
    )
  }
];

export function ReportingScreen({ data }: ReportingScreenProps) {
  const { pathname } = useLocation();
  const vm = useReportingScreenViewModel(data?.reporting ?? null, undefined, pathname);
  const reportPackProfileButtonRefs = useRef<Record<string, HTMLButtonElement | null>>({});
  const shouldFocusReportPackProfile = useRef(false);
  const [runActionStatus, setRunActionStatus] = useState<ReportingCommandStatus | null>(null);
  const [templateRunStatus, setTemplateRunStatus] = useState<ReportingCommandStatus | null>(null);
  const [scheduleActionStatus, setScheduleActionStatus] = useState<ReportingCommandStatus | null>(null);
  const runningRunActionId = runActionStatus?.state === "running" ? runActionStatus.id : null;
  const runningTemplateRunId = templateRunStatus?.state === "running" ? templateRunStatus.id : null;
  const runningScheduleActionId = scheduleActionStatus?.state === "running" ? scheduleActionStatus.id : null;

  useEffect(() => {
    if (!shouldFocusReportPackProfile.current) {
      return;
    }

    shouldFocusReportPackProfile.current = false;
    const selectedProfileId = vm.workflowTaskPanel?.selectedProfileId;
    if (selectedProfileId) {
      reportPackProfileButtonRefs.current[selectedProfileId]?.focus();
    }
  }, [vm.workflowTaskPanel?.selectedProfileId]);

  function handleReportPackProfileKeyDown(event: KeyboardEvent<HTMLDivElement>) {
    const command = resolveReportPackProfileKeyCommand(event.key);
    if (!command) {
      return;
    }

    event.preventDefault();
    shouldFocusReportPackProfile.current = true;
    vm.selectAdjacentReportPackProfile(command);
  }

  async function handleRunAction(run: ReportingRunStatusRow, action: ReportingRunActionRow) {
    if (!action.isEnabled || action.method !== "POST" || runningRunActionId) {
      return;
    }

    if (action.kind === "restatement") {
      setRunActionStatus({
        id: action.id,
        label: action.label,
        state: "error",
        message: "Restatement requires changed-line evidence before it can be submitted.",
        details: ["Open the report-pack workflow and attach changed report lines with retained evidence."]
      });
      return;
    }

    setRunActionStatus({
      id: action.id,
      label: action.label,
      state: "running",
      message: `${action.label} is running.`,
      details: []
    });

    try {
      await executeRunAction(run, action);
      setRunActionStatus({
        id: action.id,
        label: action.label,
        state: "success",
        message: `${action.label} completed.`,
        details: []
      });
    } catch (error) {
      const display = describeApiError(error, `${action.label} failed.`);
      setRunActionStatus({
        id: action.id,
        label: action.label,
        state: "error",
        message: display.summary,
        details: display.details
      });
    }
  }

  async function handleTemplateRun(template: ReportingTemplateRow) {
    if (!template.canRunOnDemand || runningTemplateRunId) {
      return;
    }

    setTemplateRunStatus({
      id: template.id,
      label: template.runActionLabel,
      state: "running",
      message: `${template.name} is running.`,
      details: []
    });

    try {
      const result = await runReportingNow({
        templateId: template.id,
        asOfDate: new Date().toISOString().slice(0, 10),
        maxRetries: 0
      });
      setTemplateRunStatus({
        id: template.id,
        label: template.runActionLabel,
        state: "success",
        message: `${template.name} run created.`,
        details: [`Run ID: ${result.run.runId}`, `Status: ${result.run.status}`]
      });
    } catch (error) {
      const display = describeApiError(error, `${template.name} run failed.`);
      setTemplateRunStatus({
        id: template.id,
        label: template.runActionLabel,
        state: "error",
        message: display.summary,
        details: display.details
      });
    }
  }

  async function handleScheduleAction(schedule: ReportingScheduleRow, action: "pause" | "resume" | "run") {
    const statusId = `${schedule.id}:${action}`;
    if (runningScheduleActionId) {
      return;
    }

    const label = action === "run"
      ? `Run ${schedule.id}`
      : action === "pause"
        ? `Pause ${schedule.id}`
        : `Resume ${schedule.id}`;
    setScheduleActionStatus({
      id: statusId,
      label,
      state: "running",
      message: `${label} is running.`,
      details: []
    });

    try {
      if (action === "pause") {
        await pauseReportingSchedule(schedule.id);
      } else if (action === "resume") {
        await resumeReportingSchedule(schedule.id);
      } else {
        await runReportingScheduleNow(schedule.id);
      }

      setScheduleActionStatus({
        id: statusId,
        label,
        state: "success",
        message: `${label} completed.`,
        details: []
      });
    } catch (error) {
      const display = describeApiError(error, `${label} failed.`);
      setScheduleActionStatus({
        id: statusId,
        label,
        state: "error",
        message: display.summary,
        details: display.details
      });
    }
  }

  if (!data) {
    return (
      <Card
        role={vm.loadingState.role}
        aria-busy={vm.loadingState.ariaBusy}
        aria-live={vm.loadingState.ariaLive}
        aria-labelledby={vm.loadingState.titleId}
        aria-describedby={vm.loadingState.detailId}
        className="panel-surface border-[var(--state-pending-bd)] bg-[var(--state-pending-bg)]"
      >
        <CardHeader className="space-y-3">
          <div className="flex flex-wrap items-center gap-2">
            <Badge
              variant="outline"
              className="border-[var(--state-pending-bd)] bg-[var(--state-pending-bg)] text-[var(--state-pending-fg)]"
              dot
            >
              {vm.loadingState.badgeLabel}
            </Badge>
            <ReportingChip label="Route" value={vm.loadingState.routeLabel} />
          </div>
          <CardTitle id={vm.loadingState.titleId}>{vm.loadingState.title}</CardTitle>
          <CardDescription id={vm.loadingState.detailId}>{vm.loadingState.detail}</CardDescription>
        </CardHeader>
      </Card>
    );
  }

  return (
    <div className="space-y-8">
      <section
        role="region"
        aria-label="Reporting workbench context"
        className="panel-surface-strong flex flex-wrap items-center justify-between gap-3 px-4 py-4"
      >
        <div className="min-w-0">
          <div className="eyebrow-label">Reporting lane</div>
          <h2 className="mt-2 font-display text-[1.375rem] font-semibold leading-tight text-foreground">
            Governed export workbench
          </h2>
          <p className="mt-1 max-w-3xl text-sm leading-6 text-muted-foreground">
            Report packs, export routes, and review evidence stay in one cockpit so governed output can be
            checked before it leaves Reporting.
          </p>
        </div>
        <div className="flex flex-wrap items-center justify-end gap-2">
          {vm.workbenchActions.map((action) => (
            <Button key={action.id} asChild variant="outline" size="sm">
              <Link to={action.href} aria-label={action.ariaLabel}>
                <Network className="h-4 w-4" aria-hidden="true" />
                {action.label}
              </Link>
            </Button>
          ))}
          {vm.workbenchChips.map((chip) => (
            <ReportingChip key={chip.label} label={chip.label} value={chip.value} />
          ))}
        </div>
      </section>

      <section className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
        {data.metrics.map((metric) => (
          <MetricCard key={metric.id} {...metric} />
        ))}
      </section>

      <section className="grid gap-4 xl:grid-cols-[0.9fr_1.1fr]">
        <Card className="panel-surface">
          <CardHeader>
            <div className="eyebrow-label">Template families</div>
            <CardTitle>Governed report templates</CardTitle>
            <CardDescription>Investor statements, SEC packets, and shadow NAV packs share the same run contract.</CardDescription>
          </CardHeader>
          <CardContent className="space-y-2">
            {vm.templateRows.map((template) => (
              <div key={template.id} className="rounded-md border border-border/70 bg-secondary/20 px-3 py-2">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <span className="font-semibold text-foreground">{template.name}</span>
                  <span className="flex flex-wrap items-center gap-1.5">
                    <Badge variant={template.statusVariant}>{template.statusLabel}</Badge>
                    <Badge variant="outline">{template.sourceLabel}</Badge>
                    <Badge variant="outline">{template.family}</Badge>
                  </span>
                </div>
                <p className="mt-1 text-xs text-muted-foreground">
                  {template.version} · {template.sectionSummary} · <span className="font-mono">{template.id}</span>
                </p>
                <div className="mt-2 flex flex-wrap items-center justify-between gap-2">
                  <p className="min-w-0 flex-1 text-xs leading-5 text-muted-foreground">
                    {template.approvalSummary}
                  </p>
                  <span className="flex flex-wrap items-center gap-2">
                    <Button
                      variant="outline"
                      size="sm"
                      aria-label={template.runActionAriaLabel}
                      disabled={!template.canRunOnDemand || Boolean(runningTemplateRunId)}
                      disabledReason={template.runDisabledReason}
                      busy={runningTemplateRunId === template.id}
                      busyLabel="Running"
                      onClick={() => void handleTemplateRun(template)}
                    >
                      <FileText className="h-4 w-4" aria-hidden="true" />
                      {template.runActionLabel}
                    </Button>
                    <Button asChild variant="outline" size="sm">
                      <a href={template.authoringHref} target="_blank" rel="noreferrer" aria-label={template.actionAriaLabel}>
                        <PencilLine className="h-4 w-4" aria-hidden="true" />
                        {template.actionLabel}
                      </a>
                    </Button>
                  </span>
                </div>
              </div>
            ))}
            {templateRunStatus ? (
              <ReportingCommandStatusView status={templateRunStatus} />
            ) : null}
          </CardContent>
        </Card>

        <Card className="panel-surface">
          <CardHeader>
            <div className="eyebrow-label">Run status</div>
            <CardTitle>Report run audit and lineage</CardTitle>
            <CardDescription>Recent manifests keep approval status, retry attempts, and dataset lineage visible.</CardDescription>
          </CardHeader>
          <CardContent className="space-y-2">
            {vm.hasRunStatusRows ? vm.runStatusRows.map((run) => (
              <div key={run.id} className="rounded-md border border-border/70 bg-secondary/20 px-3 py-2">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <span className="font-mono text-sm text-foreground">{run.id}</span>
                  <Badge variant={run.status === "Failed" ? "warning" : "outline"}>{run.status}</Badge>
                </div>
                <p className="mt-1 text-xs leading-5 text-muted-foreground">
                  {run.family} · {run.trigger} · {run.lineageSummary} · {run.auditSummary}
                </p>
                {run.failureReason ? <p className="mt-1 text-xs text-warning">{run.failureReason}</p> : null}
                {run.hasDrilldownLinks ? (
                  <div className="mt-2 flex flex-wrap gap-1.5" aria-label={`${run.id} drilldown links`}>
                    {run.drilldownLinks.map((link) => link.isBrowserNavigable ? (
                      <a
                        key={link.id}
                        href={link.href}
                        target="_blank"
                        rel="noreferrer"
                        aria-label={link.ariaLabel}
                        className="inline-flex min-w-0 items-center gap-1.5 rounded-sm border border-border/70 bg-secondary/35 px-2 py-1 text-[11px] text-foreground hover:bg-secondary/55 focus:outline-none focus:ring-2 focus:ring-primary/40"
                      >
                        <Badge variant="outline">{link.kind}</Badge>
                        <span className="truncate">{link.label}</span>
                      </a>
                    ) : (
                      <span
                        key={link.id}
                        role="group"
                        aria-label={link.ariaLabel}
                        className="inline-flex min-w-0 items-center gap-1.5 rounded-sm border border-border/60 bg-secondary/20 px-2 py-1 text-[11px] text-muted-foreground"
                      >
                        <Badge variant="outline">{link.kind}</Badge>
                        <span className="truncate">{link.label}</span>
                      </span>
                    ))}
                  </div>
                ) : null}
                {run.hasNextActions ? (
                  <div className="mt-2 flex flex-wrap gap-1.5" aria-label={`${run.id} next actions`}>
                    {run.nextActions.map((action) => (
                      <Button
                        key={action.id}
                        aria-label={action.ariaLabel}
                        disabled={!action.isEnabled || action.method !== "POST" || action.kind === "restatement" || Boolean(runningRunActionId)}
                        busy={runningRunActionId === action.id}
                        busyLabel="Running"
                        disabledReason={action.disabledReason ?? (action.kind === "restatement" ? "Restatement requires changed-line evidence." : null)}
                        onClick={() => void handleRunAction(run, action)}
                        size="sm"
                        variant={action.isEnabled ? "outline" : "ghost"}
                        className={cn(
                          "min-w-0 justify-start px-2 py-1 text-[11px]",
                          action.isEnabled
                            ? "border-primary/35 bg-primary/10 text-primary hover:bg-primary/15"
                            : "border-border/60 bg-secondary/20 text-muted-foreground"
                        )}
                      >
                        <Badge variant="outline">{action.method}</Badge>
                        <span className="truncate">{action.label}</span>
                      </Button>
                    ))}
                  </div>
                ) : null}
              </div>
            )) : (
              <p role="status" className="rounded-md border border-warning/30 bg-warning/10 px-3 py-2 text-sm text-warning">
                No report runs have been generated yet.
              </p>
            )}
            {runActionStatus ? (
              <ReportingCommandStatusView status={runActionStatus} />
            ) : null}
          </CardContent>
        </Card>
      </section>

      <section className="grid gap-4 xl:grid-cols-[0.95fr_1.05fr]">
        <Card className="panel-surface">
          <CardHeader>
            <div className="eyebrow-label">Scheduling</div>
            <CardTitle>Reporting schedules</CardTitle>
            <CardDescription>{vm.scheduleSummary}</CardDescription>
          </CardHeader>
          <CardContent className="space-y-3">
            {vm.hasScheduleRows ? (
              <div role="list" aria-label={vm.scheduleListLabel} className="space-y-2">
                {vm.scheduleRows.map((schedule) => (
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
                    </dl>
                    <div className="mt-3 flex flex-wrap items-center gap-2">
                      <Badge variant="outline">{schedule.runCountLabel}</Badge>
                      <Button
                        size="sm"
                        variant="outline"
                        busy={runningScheduleActionId === `${schedule.id}:run`}
                        busyLabel="Running"
                        disabled={Boolean(runningScheduleActionId)}
                        onClick={() => void handleScheduleAction(schedule, "run")}
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
                        onClick={() => void handleScheduleAction(schedule, "pause")}
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
                        onClick={() => void handleScheduleAction(schedule, "resume")}
                      >
                        Resume
                      </Button>
                    </div>
                  </div>
                ))}
              </div>
            ) : (
              <p role="status" className="rounded-md border border-warning/30 bg-warning/10 px-3 py-2 text-sm text-warning">
                {vm.scheduleEmptyText}
              </p>
            )}
            {scheduleActionStatus ? (
              <ReportingCommandStatusView status={scheduleActionStatus} />
            ) : null}
          </CardContent>
        </Card>

        <Card className="panel-surface">
          <CardHeader>
            <div className="eyebrow-label">Delivery history</div>
            <CardTitle>Distribution attempts</CardTitle>
            <CardDescription>Published report-pack delivery attempts are retained by recipient and channel.</CardDescription>
          </CardHeader>
          <CardContent className="space-y-2">
            {(data.reporting.deliveryAttempts ?? []).length > 0 ? (
              <div role="list" aria-label="Report-pack delivery attempts" className="space-y-2">
                {(data.reporting.deliveryAttempts ?? []).slice(0, 6).map((attempt) => (
                  <div
                    key={attempt.attemptId}
                    role="listitem"
                    aria-label={`${attempt.recipient} delivery attempt ${attempt.state}`}
                    className="rounded-md border border-border/70 bg-secondary/20 px-3 py-2"
                  >
                    <div className="flex flex-wrap items-start justify-between gap-3">
                      <span className="min-w-0">
                        <span className="block font-semibold text-foreground">{attempt.recipient}</span>
                        <span className="mt-1 block text-xs text-muted-foreground">{attempt.recipientRole} · {attempt.channel}</span>
                      </span>
                      <Badge variant={attempt.state === "Failed" ? "warning" : "success"}>{attempt.state}</Badge>
                    </div>
                    <p className="mt-2 break-all font-mono text-[11px] text-muted-foreground">{attempt.deliveryReference}</p>
                    <p className="mt-1 text-xs leading-5 text-muted-foreground">
                      Attempt {attempt.attemptNumber} by {attempt.actor} at {attempt.attemptedAtUtc}
                    </p>
                    {attempt.failureReason ? <p className="mt-1 text-xs text-warning">{attempt.failureReason}</p> : null}
                  </div>
                ))}
              </div>
            ) : (
              <p role="status" className="rounded-md border border-warning/30 bg-warning/10 px-3 py-2 text-sm text-warning">
                No report-pack delivery attempts have been retained yet.
              </p>
            )}
          </CardContent>
        </Card>
      </section>

      {vm.workflowTaskPanel ? (
        <section
          role="region"
          aria-label={vm.workflowTaskPanel.regionLabel}
          className="panel-surface-strong grid gap-4 px-4 py-4 xl:grid-cols-[1.05fr_0.95fr]"
        >
          <div className="min-w-0 space-y-4">
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div className="min-w-0">
                <div className="eyebrow-label">{vm.workflowTaskPanel.eyebrow}</div>
                <h3 className="mt-2 text-lg font-semibold text-foreground">{vm.workflowTaskPanel.title}</h3>
                <p className="mt-2 max-w-3xl text-sm leading-6 text-muted-foreground">
                  {vm.workflowTaskPanel.description}
                </p>
              </div>
              <Badge variant={vm.workflowTaskPanel.statusVariant}>
                {vm.workflowTaskPanel.statusLabel}
              </Badge>
            </div>
            <div className="flex flex-wrap gap-2">
              {vm.workflowTaskPanel.chips.map((chip) => (
                <ReportingChip key={chip.label} label={chip.label} value={chip.value} />
              ))}
            </div>
            <div
              role="status"
              id={vm.workflowTaskPanel.selectedSummaryId}
              aria-label="Selected report-pack profile"
              className="rounded-md border border-primary/25 bg-primary/10 px-3 py-2 text-sm leading-6 text-primary"
            >
              {vm.workflowTaskPanel.selectedSummary}
            </div>
            <div
              role="region"
              aria-label={vm.workflowTaskPanel.publicationReview.regionLabel}
              className="rounded-md border border-border/70 bg-secondary/20 px-3 py-3"
            >
              <div className="flex flex-wrap items-start justify-between gap-3">
                <div className="min-w-0">
                  <h4 className="text-sm font-semibold text-foreground">{vm.workflowTaskPanel.publicationReview.title}</h4>
                  <p className="mt-1 text-xs leading-5 text-muted-foreground">
                    {vm.workflowTaskPanel.publicationReview.description}
                  </p>
                </div>
                <Badge variant={vm.workflowTaskPanel.publicationReview.statusVariant}>
                  {vm.workflowTaskPanel.publicationReview.statusLabel}
                </Badge>
              </div>
              <p className="mt-3 rounded-md border border-border/70 bg-background/40 px-3 py-2 text-sm leading-6 text-foreground">
                {vm.workflowTaskPanel.publicationReview.summaryText}
              </p>
              <div className="mt-3 grid gap-2 sm:grid-cols-2">
                {vm.workflowTaskPanel.publicationReview.fields.map((field) => (
                  <div key={field.label} className="rounded-md border border-border/70 bg-background/40 px-3 py-2">
                    <span className="block text-[11px] uppercase tracking-wide text-muted-foreground">{field.label}</span>
                    <span className={cn("mt-1 block break-all font-mono text-xs", field.className)}>{field.value}</span>
                  </div>
                ))}
              </div>
              <div className="mt-3">
                <Badge variant="outline">{vm.workflowTaskPanel.publicationReview.evidenceSummary}</Badge>
              </div>
            </div>
            <div
              role="region"
              aria-label={vm.workflowTaskPanel.restatementReview.regionLabel}
              className="rounded-md border border-border/70 bg-secondary/20 px-3 py-3"
            >
              <div className="flex flex-wrap items-start justify-between gap-3">
                <div className="min-w-0">
                  <h4 className="text-sm font-semibold text-foreground">{vm.workflowTaskPanel.restatementReview.title}</h4>
                  <p className="mt-1 text-xs leading-5 text-muted-foreground">
                    {vm.workflowTaskPanel.restatementReview.description}
                  </p>
                </div>
                <Badge variant={vm.workflowTaskPanel.restatementReview.statusVariant}>
                  {vm.workflowTaskPanel.restatementReview.statusLabel}
                </Badge>
              </div>
              <p className="mt-3 rounded-md border border-border/70 bg-background/40 px-3 py-2 text-sm leading-6 text-foreground">
                {vm.workflowTaskPanel.restatementReview.summaryText}
              </p>
              <div className="mt-3 grid gap-2 sm:grid-cols-2">
                {vm.workflowTaskPanel.restatementReview.fields.map((field) => (
                  <div key={field.label} className="rounded-md border border-border/70 bg-background/40 px-3 py-2">
                    <span className="block text-[11px] uppercase tracking-wide text-muted-foreground">{field.label}</span>
                    <span className={cn("mt-1 block break-all font-mono text-xs", field.className)}>{field.value}</span>
                  </div>
                ))}
              </div>
              <div className="mt-3 flex flex-wrap items-center justify-between gap-2">
                <div className="eyebrow-label">{vm.workflowTaskPanel.restatementReview.changedLinesLabel}</div>
                <Badge variant="outline">{vm.workflowTaskPanel.restatementReview.evidenceSummary}</Badge>
              </div>
              {vm.workflowTaskPanel.restatementReview.hasChangedLines ? (
                <div role="list" aria-label={vm.workflowTaskPanel.restatementReview.changedLinesLabel} className="mt-2 grid gap-2">
                  {vm.workflowTaskPanel.restatementReview.changedLines.map((line) => (
                    <div
                      key={line.id}
                      role="listitem"
                      aria-label={line.ariaLabel}
                      className="rounded-md border border-border/70 bg-background/40 px-3 py-2"
                    >
                      <div className="flex flex-wrap items-center justify-between gap-2">
                        <span className="font-mono text-xs text-foreground">{line.lineKey}</span>
                        <Badge variant="warning">{line.valueBridge}</Badge>
                      </div>
                      {line.evidenceHref ? (
                        <a
                          href={line.evidenceHref}
                          className="mt-2 inline-flex text-xs text-primary hover:underline focus:outline-none focus:ring-2 focus:ring-primary/40"
                          aria-label={`Open evidence for ${line.lineKey}`}
                        >
                          {line.evidenceLabel}
                        </a>
                      ) : (
                        <span className="mt-2 block text-xs text-muted-foreground">{line.evidenceLabel}</span>
                      )}
                    </div>
                  ))}
                </div>
              ) : (
                <p role="status" className="mt-2 rounded-md border border-border/70 bg-background/40 px-3 py-2 text-sm leading-6 text-muted-foreground">
                  {vm.workflowTaskPanel.restatementReview.emptyText}
                </p>
              )}
            </div>
            <div>
              <div className="eyebrow-label">Actions</div>
              {vm.workflowTaskPanel.hasActions ? (
                <div
                  id={vm.workflowTaskPanel.actionPanelId}
                  role="list"
                  aria-label={vm.workflowTaskPanel.actionListLabel}
                  className="mt-2 grid gap-2 sm:grid-cols-2"
                >
                  {vm.workflowTaskPanel.actions.map((action) => (
                    <div
                      key={action.id}
                      role="listitem"
                      className="rounded-md border border-border/70 bg-secondary/20 px-3 py-2"
                    >
                      <div className="flex flex-wrap items-center gap-2">
                        <Button
                          asChild={action.method === "GET" && !action.isDisabled}
                          disabled={action.isDisabled}
                          busy={action.isRunning}
                          busyLabel={action.busyLabel}
                          disabledReason={action.disabledReason}
                          size="sm"
                          variant={action.variant}
                          aria-label={action.ariaLabel}
                          aria-describedby={action.describedById}
                          onClick={
                            action.method === "POST" && vm.selectedProfile
                              ? () => void vm.runExport(action.profileId, vm.selectedProfile!.title)
                              : undefined
                          }
                        >
                          {action.isDisabled ? (
                            action.label
                          ) : action.method === "POST" ? (
                            action.label
                          ) : (
                            <a href={action.href} target="_blank" rel="noreferrer" aria-label={action.ariaLabel}>
                              {action.label}
                            </a>
                          )}
                        </Button>
                        <Badge variant={action.statusBadgeVariant} aria-label={action.statusBadgeAriaLabel}>
                          {action.statusBadgeLabel}
                        </Badge>
                      </div>
                      <p id={action.describedById} className="mt-2 text-xs leading-5 text-muted-foreground">
                        {action.descriptionText}
                      </p>
                    </div>
                  ))}
                </div>
              ) : (
                <p
                  id={vm.workflowTaskPanel.actionPanelId}
                  role="status"
                  aria-label={vm.workflowTaskPanel.actionsEmptyAriaLabel}
                  className="mt-2 rounded-md border border-warning/30 bg-warning/10 px-3 py-2 text-sm leading-6 text-warning"
                >
                  {vm.workflowTaskPanel.actionsEmptyText}
                </p>
              )}
            </div>
            <div className="grid gap-3 md:grid-cols-2">
              <div>
                <div className="eyebrow-label">Recipients</div>
                {vm.workflowTaskPanel.hasTargets ? (
                  <div role="list" aria-label={vm.workflowTaskPanel.targetsLabel} className="mt-2 grid gap-2">
                    {vm.workflowTaskPanel.targets.map((target) => (
                      <div
                        key={target.id}
                        role="listitem"
                        aria-label={target.ariaLabel}
                        className="rounded-md border border-border/70 bg-secondary/25 px-3 py-2"
                      >
                        <div className="flex flex-wrap items-center justify-between gap-2">
                          <span className="text-sm font-semibold text-foreground">{target.label}</span>
                          <Badge variant={target.stateLabel.includes("Pending") || target.stateLabel === "Blocked" ? "warning" : "outline"}>
                            {target.stateLabel}
                          </Badge>
                        </div>
                        <p className="mt-1 text-xs leading-5 text-muted-foreground">
                          {target.channel} · {target.ownerLabel} · {target.pendingSummary}
                        </p>
                      </div>
                    ))}
                  </div>
                ) : (
                  <p
                    role="status"
                    aria-label={vm.workflowTaskPanel.targetsEmptyAriaLabel}
                    className="mt-2 rounded-md border border-warning/30 bg-warning/10 px-3 py-2 text-sm leading-6 text-warning"
                  >
                    {vm.workflowTaskPanel.targetsEmptyText}
                  </p>
                )}
              </div>
              <div>
                <div className="eyebrow-label">Backend</div>
                <div
                  id={vm.workflowTaskPanel.backendPanelId}
                  aria-label={vm.workflowTaskPanel.backendLinksLabel}
                  className="mt-2 grid gap-2"
                >
                  {vm.workflowTaskPanel.backendLinks.map((link) => link.isBrowserNavigable ? (
                    <a
                      key={link.id}
                      href={link.href}
                      target="_blank"
                      rel="noreferrer"
                      aria-label={link.ariaLabel}
                      className="flex min-w-0 items-center gap-2 rounded-md border border-border/70 bg-secondary/25 px-3 py-2 text-sm hover:bg-secondary/45 focus:outline-none focus:ring-2 focus:ring-primary/40"
                    >
                      <ReportingBackendReference link={link} />
                    </a>
                  ) : (
                    <div
                      key={link.id}
                      role="group"
                      aria-label={link.ariaLabel}
                      className="flex min-w-0 items-center gap-2 rounded-md border border-border/70 bg-secondary/20 px-3 py-2 text-sm"
                    >
                      <ReportingBackendReference link={link} />
                    </div>
                  ))}
                </div>
              </div>
            </div>
          </div>
          <div>
            <div className="eyebrow-label">Approval profile</div>
            {vm.workflowTaskPanel.hasProfiles ? (
              <div
                role="list"
                aria-label={vm.workflowTaskPanel.profileListLabel}
                aria-describedby={vm.workflowTaskPanel.profileKeyboardHelpId}
                className="mt-3 grid gap-2"
                onKeyDown={handleReportPackProfileKeyDown}
              >
                <span id={vm.workflowTaskPanel.profileKeyboardHelpId} className="sr-only">
                  {vm.workflowTaskPanel.profileKeyboardHelpText}
                </span>
                {vm.workflowTaskPanel.profiles.map((profile) => (
                  <div key={profile.id} role="listitem">
                    <button
                      ref={(node) => {
                        reportPackProfileButtonRefs.current[profile.id] = node;
                      }}
                      type="button"
                      aria-pressed={profile.isSelected}
                      aria-controls={profile.controlsId}
                      aria-expanded={profile.isExpanded}
                      aria-describedby={profile.descriptionId}
                      aria-label={profile.selectAriaLabel}
                      tabIndex={profile.tabIndex}
                      onClick={() => vm.selectProfile(profile.id)}
                      className={cn(
                        "w-full rounded-md border px-3 py-3 text-left transition-colors focus:outline-none focus:ring-2 focus:ring-primary/40",
                        profile.isSelected
                          ? "border-primary/45 bg-primary/10"
                          : "border-border/70 bg-secondary/25 hover:bg-secondary/45"
                      )}
                    >
                      <span className="flex items-start justify-between gap-3">
                        <span>
                          <span className="block font-semibold text-foreground">{profile.name}</span>
                          <span id={profile.descriptionId} className="mt-1 block text-xs text-muted-foreground">
                            {profile.summary}
                          </span>
                        </span>
                        <Badge variant={profile.readinessVariant}>{profile.readinessLabel}</Badge>
                      </span>
                    </button>
                  </div>
                ))}
              </div>
            ) : (
              <p
                role="status"
                aria-label={vm.workflowTaskPanel.profilesEmptyAriaLabel}
                className="mt-3 rounded-md border border-warning/30 bg-warning/10 px-3 py-2 text-sm leading-6 text-warning"
              >
                {vm.workflowTaskPanel.profilesEmptyText}
              </p>
            )}
          </div>
        </section>
      ) : null}

      <section className="grid gap-4 xl:grid-cols-[1.2fr_0.8fr]">
        <Card className="panel-surface">
          <CardHeader>
            <div className="eyebrow-label">Reporting Lane</div>
            <CardTitle className="flex items-center gap-2">
              <FileText className="h-5 w-5 text-primary" />
              Evidence and distribution
            </CardTitle>
            <CardDescription>
              Export profiles stay tied to accounting evidence, loader posture, and governed distribution recipients.
            </CardDescription>
          </CardHeader>
          <CardContent className="grid gap-3 md:grid-cols-3">
            <ReportingHighlight
              title="Profile wiring"
              description="Each queue row keeps profile ID, format, target tool, and loader posture visible before export."
            />
            <ReportingHighlight
              title="Pack routing"
              description="Report-pack distribution records show the recipient, delivery channel, and pending work."
            />
            <ReportingHighlight
              title="Review posture"
              description="Dictionary and loader evidence remain attached so governed output can be reviewed without context switching."
            />
          </CardContent>
        </Card>

        <Card className="panel-surface-strong text-foreground">
          <CardHeader>
            <div className="eyebrow-label">Distribution</div>
            <CardTitle>Report-pack recipients</CardTitle>
            <CardDescription>{vm.packTargetsSummary}</CardDescription>
          </CardHeader>
          <CardContent className="space-y-3 text-sm text-foreground/85">
            <div className="flex flex-wrap gap-2">
              {vm.packTargetChips.map((chip) => (
                <ReportingChip key={chip.label} label={chip.label} value={chip.value} />
              ))}
            </div>
            {vm.hasPackTargets ? (
              <div
                role="list"
                aria-label={vm.packTargetsListLabel}
                className="data-grid-surface space-y-2 border-0 bg-background/40 p-3"
              >
                {vm.packTargets.map((target) => (
                  <div
                    key={target.id}
                    role="listitem"
                    aria-label={target.ariaLabel}
                    className="rounded-md border border-border/70 bg-background/20 px-3 py-2"
                  >
                    <div className="flex flex-wrap items-start justify-between gap-3">
                      <span className="inline-flex min-w-0 items-start gap-2">
                        <FileText className="mt-0.5 h-3.5 w-3.5 shrink-0 text-muted-foreground" aria-hidden="true" />
                        <span className="min-w-0">
                          <span className="block font-semibold text-foreground">{target.label}</span>
                          <span className="mt-1 block text-xs leading-5 text-muted-foreground">
                            {target.recipientRole} · {target.channel}
                          </span>
                        </span>
                      </span>
                      <Badge variant={target.stateLabel.includes("Pending") || target.stateLabel === "Blocked" ? "warning" : "outline"}>
                        {target.pendingItemsLabel}
                      </Badge>
                    </div>
                    <p className="mt-2 text-xs leading-5 text-muted-foreground">{target.pendingSummary}</p>
                    <div className="mt-2 flex flex-wrap gap-2 text-xs text-muted-foreground">
                      <span>Owner: {target.ownerLabel}</span>
                      <span>Due: {target.dueLabel}</span>
                    </div>
                  </div>
                ))}
              </div>
            ) : (
              <p
                role="status"
                aria-label={vm.packTargetsEmptyState.ariaLabel}
                className="rounded-md border border-warning/30 bg-warning/10 px-3 py-2 text-sm leading-6 text-warning"
              >
                {vm.packTargetsEmptyState.text}
              </p>
            )}
          </CardContent>
        </Card>
      </section>

      <section className="grid gap-4 xl:grid-cols-[1.15fr_0.85fr]">
        <Card className="panel-surface">
          <CardHeader>
            <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
              <div>
                <CardTitle className="flex items-center gap-2 text-base">
                  <Landmark className="h-4 w-4 text-primary" />
                  Governed export queue
                </CardTitle>
                <CardDescription className="mt-2">{vm.description}</CardDescription>
              </div>
              <Badge variant="outline">{vm.countLabel}</Badge>
            </div>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="flex flex-wrap items-center gap-2">
              {vm.queueChips.map((chip) => (
                <ReportingChip key={chip.label} label={chip.label} value={chip.value} />
              ))}
            </div>
            <div>
              {vm.hasRows ? (
                <DenseDataTable
                  columns={reportingProfileColumns}
                  rows={vm.rows}
                  getRowId={(profile) => profile.id}
                  getRowAriaLabel={(profile) => profile.selectAriaLabel}
                  getRowSelectAriaLabel={(profile) => profile.selectAriaLabel}
                  getRowAriaControls={(profile) => profile.controlsId}
                  getRowAriaExpanded={(profile) => profile.isExpanded}
                  onRowSelect={(profile) => vm.selectProfile(profile.id)}
                  selectedRowId={vm.selectedProfile?.id ?? null}
                  emptyText={vm.emptyText}
                  ariaLabel={vm.listLabel}
                  caption={`${vm.listLabel}. Select a row to inspect export evidence and actions.`}
                />
              ) : (
                <div
                  role="status"
                  className="rounded-lg border border-warning/30 bg-warning/10 px-4 py-3 text-sm text-warning"
                >
                  {vm.emptyText}
                </div>
              )}
            </div>
          </CardContent>
        </Card>

        <aside
          id={vm.detailId}
          role="region"
          aria-label={vm.statusTitle}
          aria-live="polite"
          className="row-detail-panel h-fit min-w-0"
        >
          <div className="head flex items-center justify-between gap-3">
            <span>Selected profile inspector</span>
            <Badge variant={vm.statusBadgeVariant}>
              {vm.statusBadgeLabel}
            </Badge>
          </div>
          <div className="body">
            <div className="min-w-0">
              <h3 className="text-sm font-semibold text-foreground">
                {vm.selectedProfile?.title ?? vm.statusTitle}
              </h3>
              <p className="mt-1 font-mono text-xs text-muted-foreground">
                {vm.selectedProfile ? `${vm.selectedProfile.id} · ${vm.selectedProfile.subtitle}` : vm.nextAction}
              </p>
            </div>
            <p className="mt-3 text-sm leading-6 text-muted-foreground">{vm.statusDetail}</p>
            {vm.exportStatus ? (
              <div
                role="status"
                aria-label={vm.exportStatus.ariaLabel}
                className={cn("mt-3 space-y-3 rounded-md border px-3 py-2 text-sm leading-6", vm.exportStatus.className)}
              >
                <p>{vm.exportStatus.text}</p>
                {vm.exportStatus.fields.length > 0 ? (
                  <dl className="grid gap-2 sm:grid-cols-2">
                    {vm.exportStatus.fields.map((field) => (
                      <div
                        key={field.label}
                        className="rounded-sm border border-border/60 bg-background/25 px-2.5 py-2"
                      >
                        <dt className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">
                          {field.label}
                        </dt>
                        <dd className={cn("mt-1 break-words font-mono text-xs", field.className)}>
                          {field.value}
                        </dd>
                      </div>
                    ))}
                  </dl>
                ) : null}
                {vm.exportStatus.warnings.length > 0 ? (
                  <ul className="space-y-1 rounded-sm border border-warning/30 bg-warning/10 px-2.5 py-2 text-xs text-warning">
                    {vm.exportStatus.warnings.map((warning) => (
                      <li key={warning}>{warning}</li>
                    ))}
                  </ul>
                ) : null}
                {vm.exportStatus.artifacts.length > 0 ? (
                  <dl
                    aria-label="Export artifacts"
                    className="space-y-1 rounded-sm border border-border/60 bg-background/25 px-2.5 py-2"
                  >
                    {vm.exportStatus.artifacts.map((artifact) => (
                      <div key={`${artifact.label}-${artifact.value}`} className="grid gap-1">
                        <dt className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">
                          {artifact.label}
                        </dt>
                        <dd className={cn("break-words font-mono text-xs", artifact.className)}>
                          {artifact.value}
                        </dd>
                      </div>
                    ))}
                  </dl>
                ) : null}
              </div>
            ) : null}
            <p className="mt-3 font-mono text-xs text-muted-foreground">{vm.nextAction}</p>
            {vm.selectedProfile ? (
              <div className="mt-4 space-y-3 border-t border-border/70 pt-4">
                <p className="text-sm leading-6 text-muted-foreground">{vm.selectedProfile.description}</p>
                <div
                  role="status"
                  aria-label={`${vm.selectedProfile.title} readiness`}
                  className="rounded-md border border-primary/25 bg-primary/10 px-3 py-2 text-sm leading-6 text-primary"
                >
                  {vm.selectedProfile.readinessSummary}
                </div>
                <dl className="grid gap-2">
                  {vm.selectedProfile.fields.map((field) => (
                    <div
                      key={field.label}
                      className="grid grid-cols-[minmax(0,0.6fr)_minmax(0,1fr)] items-start gap-3 rounded-md border border-border/60 bg-secondary/25 px-3 py-2"
                    >
                      <dt className="text-xs text-muted-foreground">{field.label}</dt>
                      <dd className={cn("text-right font-mono text-xs", field.className)}>
                        {field.value}
                      </dd>
                    </div>
                  ))}
                </dl>
                <div className="grid gap-2 pt-2" role="list" aria-label={`${vm.selectedProfile.title} export actions`}>
                  {vm.selectedProfile.actions.map((action) => (
                    <div
                      key={action.id}
                      role="listitem"
                      className="rounded-md border border-border/60 bg-secondary/20 px-3 py-2"
                    >
                      <div className="flex flex-wrap items-center gap-2">
                        <Button
                          asChild={action.method === "GET" && !action.isDisabled}
                          disabled={action.isDisabled}
                          busy={action.isRunning}
                          busyLabel={action.busyLabel}
                          disabledReason={action.disabledReason}
                          size="sm"
                          variant={action.variant}
                          aria-label={action.ariaLabel}
                          aria-describedby={action.describedById}
                          onClick={
                            action.method === "POST"
                              ? () => void vm.runExport(action.profileId, vm.selectedProfile!.title)
                              : undefined
                          }
                        >
                          {action.isDisabled ? (
                            action.label
                          ) : action.method === "POST" ? (
                            action.label
                          ) : (
                            <a href={action.href} target="_blank" rel="noreferrer" aria-label={action.ariaLabel}>
                              {action.label}
                            </a>
                          )}
                        </Button>
                        <Badge variant={action.statusBadgeVariant} aria-label={action.statusBadgeAriaLabel}>
                          {action.statusBadgeLabel}
                        </Badge>
                      </div>
                      <p id={action.describedById} className="mt-2 text-xs leading-5 text-muted-foreground">
                        {action.descriptionText}
                      </p>
                    </div>
                  ))}
                </div>
              </div>
            ) : null}
          </div>
        </aside>
      </section>
    </div>
  );
}

function ReportingHighlight({ title, description }: { title: string; description: string }) {
  return (
    <div className="rounded-lg border border-border/70 bg-secondary/35 p-4">
      <div className="font-semibold">{title}</div>
      <p className="mt-2 text-sm leading-6 text-muted-foreground">{description}</p>
    </div>
  );
}

function ReportingChip({ label, value }: { label: string; value: string }) {
  return (
    <div className="toolbar-chip" aria-label={`${label} ${value}`}>
      <span className="text-muted-foreground">{label}</span>
      <span className="font-mono text-foreground">{value}</span>
    </div>
  );
}

function ReportingScheduleField({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-sm border border-border/60 bg-background/30 px-2.5 py-2">
      <dt className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">{label}</dt>
      <dd className="mt-1 break-words font-mono text-xs text-foreground">{value}</dd>
    </div>
  );
}

function ReportingCommandStatusView({ status }: { status: ReportingCommandStatus }) {
  return (
    <div
      role="status"
      aria-label={`${status.label} status`}
      className={cn(
        "rounded-md border px-3 py-2 text-sm leading-6",
        status.state === "success"
          ? "border-success/30 bg-success/10 text-success"
          : status.state === "error"
            ? "border-warning/35 bg-warning/10 text-warning"
            : "border-primary/30 bg-primary/10 text-primary"
      )}
    >
      <p>{status.message}</p>
      {status.details.length > 0 ? (
        <ul className="mt-2 space-y-1 text-xs">
          {status.details.map((detail) => (
            <li key={detail}>{detail}</li>
          ))}
        </ul>
      ) : null}
    </div>
  );
}

async function executeRunAction(run: ReportingRunStatusRow, action: ReportingRunActionRow): Promise<void> {
  if (action.kind.startsWith("delivery:")) {
    const reportId = extractReportPackId(run, action);
    const distributionId = action.kind.slice("delivery:".length);
    await deliverReportPack(reportId, {
      distributionId,
      actor: "browser-workstation",
      note: "Delivered from browser Reporting workspace.",
      evidenceLinks: buildEvidenceLinksFromRun(run)
    });
    return;
  }

  if (action.kind === "approval-reject") {
    await apiPostJson<unknown>(action.href, {
      reason: "Returned from browser Reporting workspace.",
      actor: "browser-workstation",
      actorRole: "Reviewer",
      evidenceLinks: buildEvidenceLinksFromRun(run)
    });
    return;
  }

  if (action.kind === "publication") {
    const reportId = extractReportPackId(run, action);
    await apiPostJson<unknown>(action.href, {
      signedOffBy: "browser-workstation",
      evidenceHash: `sha256:${normalizeEvidenceToken(run.id)}`,
      manifestId: `browser-${normalizeEvidenceToken(reportId)}`,
      retainedManifestPath: `workstation/reporting/${normalizeEvidenceToken(reportId)}/manifest.json`,
      evidenceLinks: buildEvidenceLinksFromRun(run),
      note: "Published from browser Reporting workspace."
    });
    return;
  }

  await apiPostJson<unknown>(action.href);
}

function extractReportPackId(run: ReportingRunStatusRow, action: ReportingRunActionRow): string {
  const hrefMatch = action.href.match(/\/reporting\/packs\/([0-9a-fA-F-]{36})(?:\/|$)/);
  if (hrefMatch?.[1]) {
    return hrefMatch[1];
  }

  if (run.id.startsWith("report-pack:")) {
    return run.id.slice("report-pack:".length);
  }

  return run.id;
}

function buildEvidenceLinksFromRun(run: ReportingRunStatusRow): ReportingWorkflowEvidenceLink[] {
  const links = run.drilldownLinks
    .filter((link) => link.kind.includes("evidence") || link.href.includes("/evidence"))
    .map((link) => ({
      evidenceId: normalizeEvidenceToken(link.id),
      label: link.label,
      route: link.href,
      source: link.source || "reporting",
      capturedAtUtc: null
    }));

  if (links.length > 0) {
    return links;
  }

  return [{
    evidenceId: normalizeEvidenceToken(run.id),
    label: `${run.templateId} report run`,
    route: null,
    source: "reporting",
    capturedAtUtc: null
  }];
}

function normalizeEvidenceToken(value: string): string {
  const normalized = value.toLowerCase().replace(/[^a-z0-9-]+/g, "-").replace(/^-+|-+$/g, "");
  return normalized || "reporting-evidence";
}

function ReportingBackendReference({
  link
}: {
  link: {
    method: string;
    label: string;
    href: string;
    interactionLabel: string;
  };
}) {
  return (
    <>
      <Badge variant="outline">{link.method}</Badge>
      <span className="min-w-0">
        <span className="block font-semibold text-foreground">{link.label}</span>
        <span className="block break-all font-mono text-[11px] text-muted-foreground">{link.href}</span>
        <span className="mt-1 inline-flex rounded-sm border border-border/60 px-1.5 py-0.5 text-[10px] uppercase text-muted-foreground">
          {link.interactionLabel}
        </span>
      </span>
    </>
  );
}
