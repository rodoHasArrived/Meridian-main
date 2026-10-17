import { AlertTriangle, ArrowUpRight, FileText, PanelRight, TableProperties } from "lucide-react";
import { useEffect, useRef, useState } from "react";
import { useInRouterContext, useLocation, useNavigate } from "react-router-dom";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { SeverityBadge } from "@/components/operations";
import { DenseRowDetailPanel } from "@/components/meridian/dense-row-detail-accessibility";
import { DenseDataTable, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import {
  readReportingWorkSelection,
  reportingWorkScopeKey,
  withReportingWorkSelection
} from "@/components/meridian/reporting-hub.selection";
import { cn } from "@/lib/utils";
import { badgeVariantToSeverityStatus, semanticToneToTextClass } from "@/lib/shared-tone-mappings";
import type { ReportingHubDailyWorkItem, ReportingHubModel, ReportingHubTone } from "@/lib/reporting-hub";

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
    render: (item) => <SeverityBadge status={badgeVariantToSeverityStatus(item.badgeVariant)} label={item.statusLabel} />
  },
  { id: "owner", label: "Owner", render: (item) => <span className="text-xs text-muted-foreground">{item.owner}</span> },
  { id: "due", label: "Due", render: (item) => <span className="text-xs text-muted-foreground">{item.dueLabel ?? "Not supplied"}</span> }
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

  return (
    <section role="region" aria-label="Daily reporting cockpit" className={cn("workspace-section-band", className)}>
      <div className="workspace-section-subheader">
        <div className="min-w-0">
          <h2 className="workspace-section-title">Daily reporting cockpit</h2>
          <p className="workspace-section-summary">
            Select work to review its evidence and next action.
          </p>
        </div>
        <div className="flex flex-wrap gap-2">
          {model.cards.length > 0 ? (
            <Badge variant={model.attentionCount > 0 ? "warning" : "success"}>{model.summaryLabel}</Badge>
          ) : null}
        </div>
      </div>

      <div className="grid gap-4 xl:grid-cols-[minmax(0,1fr)_360px]">
        <div className="space-y-4">
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
                  ? `No dedicated daily work items are loaded. ${model.attentionCount} report ${model.attentionCount === 1 ? "family still needs" : "families still need"} review in the organizer below.`
                  : "No due packages, approvals, delivery failures, restatements, or evidence gaps are queued."}
              </div>
            )}
          </section>

          {model.cards.length > 0 ? (
            <section className="rounded-md border border-border/70 bg-background/75" aria-label="Report family organizer">
              <div className="flex flex-wrap items-center justify-between gap-2 border-b border-border/70 px-3 py-2">
                <div className="flex items-center gap-2">
                  <TableProperties className="h-4 w-4 text-primary" aria-hidden="true" />
                  <h3 className="text-sm font-semibold text-foreground">Report family organizer</h3>
                </div>
                <Badge variant="outline">{model.summaryLabel}</Badge>
              </div>
              <div className="overflow-x-auto">
                <table className="min-w-full text-sm">
                  <thead className="bg-secondary/30 text-xs uppercase text-muted-foreground">
                    <tr>
                      <th className="px-3 py-2 text-left">Family</th>
                      <th className="px-3 py-2 text-left">Readiness</th>
                      <th className="px-3 py-2 text-left">Latest approved</th>
                      <th className="px-3 py-2 text-left">Activity</th>
                      <th className="px-3 py-2 text-left">Action</th>
                    </tr>
                  </thead>
                  <tbody>
                    {model.cards.map((card) => (
                      <tr key={card.familyKey} aria-label={card.ariaLabel} className="border-t border-border/70">
                        <td className="px-3 py-2 font-semibold text-foreground">{card.family}</td>
                        <td className="px-3 py-2">
                          <SeverityBadge status={badgeVariantToSeverityStatus(card.badgeVariant)} label={card.statusLabel} />
                        </td>
                        <td className={cn("px-3 py-2 text-xs", semanticToneToTextClass(card.statusTone))}>{card.approvedAsOfLabel}</td>
                        <td className="px-3 py-2 text-xs text-muted-foreground">
                          {card.detail}
                          {card.latestRunId ? (
                            <>
                              {" · latest "}
                              <span className="font-mono">{card.latestAsOfLabel}</span>
                            </>
                          ) : null}
                        </td>
                        <td className="px-3 py-2">
                          <Button asChild variant={card.needsAttention ? "default" : "outline"} size="sm">
                            <a href={card.nextActionHref} aria-label={`${card.nextActionLabel} for ${card.family}`}>
                              <FileText className="h-4 w-4" aria-hidden="true" />
                              {card.nextActionLabel}
                              <ArrowUpRight className="h-3.5 w-3.5" aria-hidden="true" />
                            </a>
                          </Button>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </section>
          ) : null}
        </div>

        <DenseRowDetailPanel id={REPORTING_WORK_DETAIL_ID} className="rounded-md border border-border/70 bg-background/75 p-3" ariaLabel="Selected reporting work detail" selectedSourceLabel={selectedWork?.title ?? "No reporting work selected"}>
          <div className="flex items-center gap-2 text-sm font-semibold text-foreground">
            <PanelRight className="h-4 w-4 text-primary" aria-hidden="true" />
            Selected work
          </div>
          {selectedWork ? (
            <div className="mt-3 space-y-3">
              <div>
                <div className="flex flex-wrap items-center gap-2">
                  <SeverityBadge status={badgeVariantToSeverityStatus(selectedWork.badgeVariant)} label={selectedWork.statusLabel} />
                  <Badge variant="outline">{selectedWork.kindLabel}</Badge>
                </div>
                <h3 className="mt-3 text-base font-semibold leading-snug text-foreground">{selectedWork.title}</h3>
                <p className="mt-2 text-sm leading-6 text-muted-foreground">{selectedWork.detail}</p>
              </div>
              <div className="flex flex-wrap gap-2 pt-1">
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
              </div>
              <dl className="grid gap-2 text-xs" aria-label={`${selectedWork.title} decision facts`}>
                <ReportingHubFact label="Blocked" value={selectedWork.blockedLabel} tone={selectedWork.tone} />
                <ReportingHubFact label="Owner" value={selectedWork.owner} />
                <ReportingHubFact label="Affected output" value={selectedWork.affectedOutputLabel} />
                <ReportingHubFact label="Next action" value={selectedWork.nextActionLabel} />
                <ReportingHubFact label="Proof posture" value={selectedWork.proofLabel} tone={selectedWork.evidenceGaps.length > 0 ? "warning" : "success"} />
              </dl>
              <div className="flex flex-wrap gap-1.5">
                {selectedWork.context.map((context) => (
                  <Badge key={context} variant="outline">{context}</Badge>
                ))}
              </div>
              {selectedWork.evidenceGaps.length > 0 ? (
                <ul className="grid gap-1 rounded-md border border-warning/30 bg-warning/10 px-3 py-2 text-xs leading-5 text-warning" aria-label={`${selectedWork.title} evidence gaps`}>
                  {selectedWork.evidenceGaps.map((gap) => (
                    <li key={gap}>{gap}</li>
                  ))}
                </ul>
              ) : null}

            </div>
          ) : (
            <p className="mt-3 text-sm leading-6 text-muted-foreground">
              {unavailableSelection ?? "No urgent reporting work is queued. Use the report family organizer to run, review, or set up the next output."}
            </p>
          )}
        </DenseRowDetailPanel>
      </div>
    </section>
  );
}

function ReportingHubFact({
  label,
  value,
  tone = "muted"
}: {
  label: string;
  value: string;
  tone?: ReportingHubTone;
}) {
  return (
    <div className="rounded-md border border-border/60 bg-background/50 px-2.5 py-2">
      <dt className="text-xs font-medium text-muted-foreground">{label}</dt>
      <dd className={cn("mt-1 break-words text-xs leading-5", semanticToneToTextClass(tone))}>{value}</dd>
    </div>
  );
}
