import { WorkspaceWorkbenchShell } from "@/components/meridian/workspace-workbench-shell";
import { AlertTriangle, ArrowUpRight, FileText, TableProperties } from "lucide-react";
import { useEffect, useRef, useState } from "react";
import { useInRouterContext, useLocation, useNavigate } from "react-router-dom";
import { Badge } from "@/components/ui/badge";
import { SectionDescription } from "@/components/ui/section-description";
import { Button } from "@/components/ui/button";
import { SeverityBadge } from "@/components/operations";
import { DetailPane } from "@/components/meridian/detail-pane";
import { DenseDataTable, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import {
  readReportingWorkSelection,
  reportingWorkScopeKey,
  withReportingWorkSelection
} from "@/components/meridian/reporting-hub.selection";
import { cn } from "@/lib/utils";
import type { ReportingHubCard, ReportingHubDailyWorkItem, ReportingHubModel } from "@/lib/reporting-hub";
import { statusTextClass, toSeverity } from "@/design-system/status-styles";

export interface ReportingHubProps {
  model: ReportingHubModel;
  className?: string;
}

const REPORTING_WORK_DETAIL_ID = "reporting-work-detail";
const reportingWorkColumns: DenseDataTableColumn<ReportingHubDailyWorkItem>[] = [
  {
    id: "work",
    label: "Work item",
    render: (item) => (
      <div className="min-w-0">
        <div className="text-sm font-semibold text-foreground">{item.title}</div>
        <div className="mt-1 text-xs text-muted-foreground">{item.kindLabel} · {item.affectedOutputLabel}</div>
      </div>
    )
  },
  {
    id: "state",
    label: "State",
    render: (item) => <SeverityBadge status={toSeverity(item.badgeVariant)} label={item.statusLabel} />
  },
  { id: "owner", label: "Owner", render: (item) => <span className="text-xs text-muted-foreground">{item.owner}</span> },
  { id: "due", label: "Due", render: (item) => <span className="text-xs text-muted-foreground">{item.dueLabel ?? "Not supplied"}</span> }
];

const reportFamilyColumns: DenseDataTableColumn<ReportingHubCard>[] = [
  {
    id: "family",
    label: "Family",
    sortValue: (card) => card.family,
    render: (card) => <span className="font-semibold text-foreground">{card.family}</span>
  },
  {
    id: "readiness",
    label: "Readiness",
    // Families that need attention come first in ascending order.
    sortValue: (card) => `${card.needsAttention ? 0 : 1} ${card.statusLabel}`,
    render: (card) => <SeverityBadge status={toSeverity(card.badgeVariant)} label={card.statusLabel} />
  },
  {
    id: "approved",
    label: "Latest approved",
    hideable: true,
    render: (card) => <span className={cn("text-xs", statusTextClass(card.statusTone))}>{card.approvedAsOfLabel}</span>
  },
  {
    id: "activity",
    label: "Activity",
    hideable: true,
    sortValue: (card) => card.runCount,
    render: (card) => (
      <span className="text-xs text-muted-foreground">
        {card.detail}
        {card.latestRunId ? (
          <>
            {" · latest "}
            <span className="font-mono">{card.latestAsOfLabel}</span>
          </>
        ) : null}
      </span>
    )
  },
  {
    id: "action",
    label: "Action",
    render: (card) => (
      <Button asChild variant="outline" size="sm">
        <a href={card.nextActionHref} aria-label={`${card.nextActionLabel} for ${card.family}`}>
          <FileText className="h-4 w-4" aria-hidden="true" />
          {card.nextActionLabel}
          <ArrowUpRight className="h-3.5 w-3.5" aria-hidden="true" />
        </a>
      </Button>
    )
  }
];

/**
 * Reporting launch surface with daily work first and report-family launch cards below.
 */
export function ReportingHub(props: ReportingHubProps) {
  return useInRouterContext() ? <RoutedReportingHub {...props} /> : <LocalReportingHub {...props} />;
}

function RoutedReportingHub(props: ReportingHubProps) {
  const location = useLocation();
  const navigate = useNavigate();
  const requestedWorkId = readReportingWorkSelection(location.search);
  const scopeKey = reportingWorkScopeKey(location.pathname, location.search);
  const previousScope = useRef(scopeKey);
  const [unselectedScope, setUnselectedScope] = useState<string | null>(null);
  const [pendingSelection, setPendingSelection] = useState<{
    workItemId: string;
    scopeKey: string;
    search: string;
  } | null>(null);
  const scopeChanged = previousScope.current !== scopeKey;

  useEffect(() => {
    if (pendingSelection && (pendingSelection.scopeKey !== scopeKey || pendingSelection.search !== location.search)) {
      setPendingSelection(null);
    }
  }, [pendingSelection, scopeKey, location.search]);

  useEffect(() => {
    if (previousScope.current === scopeKey) return;
    previousScope.current = scopeKey;
    setUnselectedScope(scopeKey);
    if (readReportingWorkSelection(location.search) !== null) {
      navigate({
        pathname: location.pathname,
        search: withReportingWorkSelection(location.search, null),
        hash: location.hash
      }, { replace: true, preventScrollReset: true });
    }
  }, [scopeKey, location.pathname, location.search, location.hash, navigate]);

  const selectionNotice = scopeChanged || unselectedScope === scopeKey
    ? "Reporting scope changed. Select a work item to review its current evidence and actions."
    : null;
  // Router updates may be deferred. Apply the row choice immediately so Enter
  // and Escape use the chosen record while its shareable URL is being updated.
  const selectedWorkId = pendingSelection?.scopeKey === scopeKey && pendingSelection.search === location.search
    ? pendingSelection.workItemId
    : requestedWorkId;

  return (
    <ReportingHubContent
      {...props}
      requestedWorkId={selectedWorkId}
      selectionNotice={selectionNotice}
      onSelect={(item) => {
        setPendingSelection({ workItemId: item.workItemId, scopeKey, search: location.search });
        setUnselectedScope(null);
        navigate({
          pathname: location.pathname,
          search: withReportingWorkSelection(location.search, item.workItemId),
          hash: location.hash
        }, { replace: true, preventScrollReset: true });
      }}
    />
  );
}

function LocalReportingHub(props: ReportingHubProps) {
  const [requestedWorkId, setRequestedWorkId] = useState<string | null>(null);
  return <ReportingHubContent {...props} requestedWorkId={requestedWorkId} onSelect={(item) => setRequestedWorkId(item.workItemId)} />;
}

function ReportingHubContent({
  model,
  className,
  requestedWorkId,
  onSelect,
  selectionNotice = null
}: ReportingHubProps & {
  requestedWorkId: string | null;
  onSelect: (item: ReportingHubDailyWorkItem) => void;
  selectionNotice?: string | null;
}) {
  const selectedWork = selectionNotice ? null : requestedWorkId === null
    ? model.dailyWork[0] ?? null
    : model.dailyWork.find((item) => item.workItemId === requestedWorkId) ?? null;
  const unavailableSelection = selectionNotice ?? (requestedWorkId !== null && !selectedWork
    ? "This reporting work item is no longer in the current queue. Select another item."
    : null);

  if (model.isEmpty && !unavailableSelection) return null;

  const triageQueue = (
    <section className="rounded-md border border-border/70 bg-background/75" aria-label="Daily reporting triage queue">
      <div className="flex flex-wrap items-center justify-between gap-2 border-b border-border/70 px-3 py-2">
        <div className="flex items-center gap-2">
          <AlertTriangle className="h-4 w-4 text-warning" aria-hidden="true" />
          <h3 className="text-sm font-semibold text-foreground">Triage queue</h3>
        </div>
        <Badge variant={model.dailyWork.length > 0 || model.attentionCount > 0 ? "warning" : "success"}>
          {model.dailyWorkSummaryLabel}
        </Badge>
      </div>
      {model.dailyWork.length > 0 ? (
        <DenseDataTable
          columns={reportingWorkColumns}
          rows={model.dailyWork}
          getRowId={(item) => item.workItemId}
          getRowAriaLabel={(item) => item.ariaLabel}
          getRowSelectAriaLabel={(item) => `Select reporting work: ${item.title}`}
          getRowAriaControls={() => REPORTING_WORK_DETAIL_ID}
          getRowAriaExpanded={(item) => item.workItemId === selectedWork?.workItemId}
          selectedRowId={selectedWork?.workItemId ?? null}
          onRowSelect={onSelect}
          emptyText="No reporting work is queued."
          ariaLabel="Daily reporting work"
          caption="Select a work item to inspect its blockers, evidence and next action."
        />
      ) : (
        <div className="px-3 py-4 text-sm text-muted-foreground">
          {model.attentionCount > 0
            ? `No dedicated daily work items are loaded. ${model.attentionCount} report ${model.attentionCount === 1 ? "family still needs" : "families still need"} review in the family organizer.`
            : "No due packages, approvals, delivery failures, restatements, or evidence gaps are queued."}
        </div>
      )}
    </section>
  );

  return (
    <section role="region" aria-label="Daily reporting cockpit" className={cn("workspace-section-band", className)}>
      <div className="workspace-section-subheader">
        <div className="flex min-w-0 flex-wrap items-center gap-x-2 gap-y-1">
          <h2 className="workspace-section-title">Daily reporting cockpit</h2>
          <SectionDescription descriptionKey="reporting-daily-cockpit" label="About the daily reporting cockpit">
            Select work to review its evidence and next action.
          </SectionDescription>
        </div>
        <div className="flex flex-wrap gap-2">
          {model.cards.length > 0 ? (
            <Badge variant={model.attentionCount > 0 ? "warning" : "success"}>{model.summaryLabel}</Badge>
          ) : null}
        </div>
      </div>

      <WorkspaceWorkbenchShell label="Reporting review workbench" switchInspector inspectorRole="region" inspectorLabel="Reporting inspection" inspector={(
        <DetailPane
          id={REPORTING_WORK_DETAIL_ID}
          ariaLabel="Selected reporting work detail"
          objectType={selectedWork?.kindLabel ?? "Reporting work"}
          title={selectedWork?.title}
          status={selectedWork ? { label: selectedWork.statusLabel, tone: selectedWork.badgeVariant } : null}
          summary={selectedWork?.detail}
          actions={selectedWork && (selectedWork.primaryActionHref || (selectedWork.secondaryActionHref && selectedWork.secondaryActionLabel)) ? (
            <>
              {selectedWork.primaryActionHref ? (
                <Button asChild size="sm">
                  <a href={selectedWork.primaryActionHref} aria-label={`${selectedWork.primaryActionLabel}: ${selectedWork.title}`}>
                    <FileText className="h-4 w-4" aria-hidden="true" />
                    {selectedWork.primaryActionLabel}
                  </a>
                </Button>
              ) : null}
              {selectedWork.secondaryActionHref && selectedWork.secondaryActionLabel ? (
                <Button asChild variant="outline" size="sm">
                  <a href={selectedWork.secondaryActionHref} aria-label={`${selectedWork.secondaryActionLabel}: ${selectedWork.title}`}>
                    {selectedWork.secondaryActionLabel}
                    <ArrowUpRight className="h-3.5 w-3.5" aria-hidden="true" />
                  </a>
                </Button>
              ) : null}
            </>
          ) : null}
          fields={selectedWork
            ? [
                { label: "Blocked", value: selectedWork.blockedLabel, tone: selectedWork.tone },
                { label: "Owner", value: selectedWork.owner },
                { label: "Affected output", value: selectedWork.affectedOutputLabel },
                { label: "Next action", value: selectedWork.nextActionLabel },
                { label: "Proof posture", value: selectedWork.proofLabel, tone: selectedWork.evidenceGaps.length > 0 ? "warning" : "success" }
              ]
            : undefined}
          fieldsLabel={selectedWork ? `${selectedWork.title} decision facts` : undefined}
          empty={{
            text: unavailableSelection ?? "No urgent reporting work is queued. Use the report family organizer to run, review, or set up the next output."
          }}
          className="bg-background/75"
        >
          {selectedWork ? (
            <div className="space-y-3">
              {selectedWork.context.length > 0 ? (
                <div className="flex flex-wrap gap-1.5">
                  {selectedWork.context.map((context) => (
                    <Badge key={context} variant="outline">{context}</Badge>
                  ))}
                </div>
              ) : null}
              {selectedWork.evidenceGaps.length > 0 ? (
                <ul className="grid gap-1 rounded-md border border-warning/30 bg-warning/10 px-3 py-2 text-xs leading-5 text-warning" aria-label={`${selectedWork.title} evidence gaps`}>
                  {selectedWork.evidenceGaps.map((gap) => (
                    <li key={gap}>{gap}</li>
                  ))}
                </ul>
              ) : null}
            </div>
          ) : null}
        </DetailPane>
      )}>

        <div className="space-y-4">
          {model.dailyWork.length > 0 || model.cards.length === 0 ? triageQueue : null}

          {model.cards.length > 0 ? (
            <section className="rounded-md border border-border/70 bg-background/75" aria-label="Report family organizer">
              <div className="flex flex-wrap items-center justify-between gap-2 border-b border-border/70 px-3 py-2">
                <div className="flex items-center gap-2">
                  <TableProperties className="h-4 w-4 text-primary" aria-hidden="true" />
                  <h3 className="text-sm font-semibold text-foreground">Report family organizer</h3>
                </div>
                <Badge variant="outline">{model.summaryLabel}</Badge>
              </div>
              <DenseDataTable
                tableId="report-family-organizer"
                columns={reportFamilyColumns}
                rows={model.cards}
                getRowId={(card) => card.familyKey}
                getRowAriaLabel={(card) => card.ariaLabel}
                emptyText="No report families are configured."
                ariaLabel="Report families"
                maxVisibleRows={null}
              />
            </section>
          ) : null}
          {model.dailyWork.length === 0 && model.cards.length > 0 ? triageQueue : null}
        </div>

      </WorkspaceWorkbenchShell>
    </section>
  );
}
