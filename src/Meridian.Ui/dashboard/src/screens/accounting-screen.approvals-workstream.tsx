// Accounting approvals workstream panel, extracted from accounting-screen.tsx.
// Pure approval helpers live in accounting-screen.approvals.ts.
import { RefreshCcw, UserCheck } from "lucide-react";
import { useLocation } from "react-router-dom";
import { useEffect, useMemo, useState } from "react";
import { ApprovalCollaborationPanel } from "@/components/meridian/approval-collaboration-panel";
import { focusDenseRowDetailPanel } from "@/components/meridian/dense-row-detail-accessibility";
import { DetailRail } from "@/components/meridian/detail-rail";
import { TableWorkbench } from "@/components/meridian/table-workbench";
import { DesignSystemStatus } from "@/design-system/status";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { StatusBanner } from "@/components/ui/status-banner";
import { TechnicalDetails } from "@/components/ui/technical-details";
import { approveOperationsContinuityWorkflow, getOperationsContinuityWorkflow, getOperationsContinuityWorkflows, rejectOperationsContinuityWorkflow } from "@/lib/api";
import { cn } from "@/lib/utils";
import type { OperationsApproval, OperationsContinuityWorkflow, OperationsContinuityWorkflowSummary, OperationsTimelineEntry } from "@/types";
import { approvalBlockedReason, approvalEvidenceSummary, approvalNextAction, approvalSignerLabel, approvalWorkflowStatusLabel, buildApprovalActionDisabledReason, formatApprovalDate, formatApprovalError, missingApprovalEvidenceLabel, resolveApprovalReportPackId, splitApprovalWords } from "@/screens/accounting-screen.approvals";
import { AccountingValue } from "@/screens/accounting-screen.accounting-value";

export function AccountingApprovalsWorkstream() {
  const { search } = useLocation();
  const [workflows, setWorkflows] = useState<OperationsContinuityWorkflowSummary[]>([]);
  const [selectedWorkflowId, setSelectedWorkflowId] = useState<string | null>(null);
  const [loadedDetail, setDetail] = useState<OperationsContinuityWorkflow | null>(null);
  const [detailReload, setDetailReload] = useState(0);
  const detail = loadedDetail?.workflowId === selectedWorkflowId ? loadedDetail : null;
  const [loading, setLoading] = useState(true);
  const [detailLoading, setDetailLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [detailError, setDetailError] = useState<string | null>(null);
  const [action, setAction] = useState<"approve" | "reject" | "request-changes" | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  const requestedApprovalId = useMemo(() => new URLSearchParams(search).get("approvalId"), [search]);

  const refreshWorkflows = async () => {
    setLoading(true);
    setError(null);
    try {
      const rows = await getOperationsContinuityWorkflows();
      const sorted = [...rows].sort((left, right) => right.updatedAtUtc.localeCompare(left.updatedAtUtc));
      setWorkflows(sorted);
      setSelectedWorkflowId((current) => current && sorted.some((row) => row.workflowId === current)
        ? current
        : sorted[0]?.workflowId ?? null);
    } catch (err) {
      setError(formatApprovalError(err, "Approval queue could not be loaded."));
      setWorkflows([]);
      setSelectedWorkflowId(null);
      setDetail(null);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    void refreshWorkflows();
  }, []);

  useEffect(() => {
    if (!selectedWorkflowId) {
      setDetail(null);
      return;
    }

    let cancelled = false;
    setDetailLoading(true);
    setDetailError(null);
    getOperationsContinuityWorkflow(selectedWorkflowId)
      .then((workflow) => {
        if (!cancelled) {
          setDetail(workflow);
        }
      })
      .catch((err) => {
        if (!cancelled) {
          setDetail(null);
          setDetailError(formatApprovalError(err, "Approval detail could not be loaded."));
        }
      })
      .finally(() => {
        if (!cancelled) {
          setDetailLoading(false);
        }
      });

    return () => {
      cancelled = true;
    };
  }, [selectedWorkflowId, detailReload]);

  const selectedApproval = selectApproval(detail, requestedApprovalId);
  const selectedWorkflow = detail ?? workflows.find((workflow) => workflow.workflowId === selectedWorkflowId) ?? null;
  const approveDisabledReason = buildApprovalActionDisabledReason(selectedWorkflow, selectedApproval, action);
  const rejectDisabledReason = action ? "Wait for the current approval action to finish." : selectedWorkflow ? null : "Select an approval before rejecting.";
  const requestChangesDisabledReason = action ? "Wait for the current approval action to finish." : selectedWorkflow ? null : "Select an approval before requesting changes.";

  const runDecision = async (decision: "approve" | "reject" | "request-changes") => {
    if (!selectedWorkflow) {
      return;
    }

    setAction(decision);
    setActionError(null);
    try {
      const rationale = decision === "approve"
        ? "Approved from Accounting approvals workstream."
        : decision === "request-changes"
          ? "Request changes from Accounting approvals workstream."
          : "Rejected from Accounting approvals workstream.";
      if (decision === "approve") {
        const reportPackId = resolveApprovalReportPackId(selectedWorkflow);
        if (!reportPackId) {
          throw new Error("Approval requires a report pack id from the selected workflow.");
        }

        await approveOperationsContinuityWorkflow(selectedWorkflow.workflowId, {
          expectedVersion: selectedWorkflow.version,
          actor: "browser-operator",
          reviewer: "browser-operator",
          rationale,
          reportPackId
        });
      } else {
        await rejectOperationsContinuityWorkflow(selectedWorkflow.workflowId, {
          expectedVersion: selectedWorkflow.version,
          actor: "browser-operator",
          reviewer: "browser-operator",
          rationale,
          reasonCode: decision === "request-changes" ? "request-changes" : "operator-rejected"
        });
      }

      await refreshWorkflows();
      const refreshedDetail = await getOperationsContinuityWorkflow(selectedWorkflow.workflowId);
      setSelectedWorkflowId(refreshedDetail.workflowId);
      setDetail(refreshedDetail);
    } catch (err) {
      setActionError(formatApprovalError(err, "Approval action failed."));
    } finally {
      setAction(null);
    }
  };

  return (
    <section id="accounting-approvals" className="workspace-section-band" aria-labelledby="accounting-approvals-heading"
      onKeyDownCapture={(event) => {
        if (event.defaultPrevented || event.altKey || event.ctrlKey || event.metaKey || event.shiftKey
          || event.nativeEvent.isComposing || !(event.target instanceof HTMLElement)
          || event.target.closest("input,textarea,select,button,a,[contenteditable=true],[role=dialog],[role=menu]")) return;
        const rowId = event.target.closest<HTMLElement>("[data-dense-row-id]")?.dataset.denseRowId;
        if (!rowId || !workflows.some((workflow) => workflow.workflowId === rowId)) return;
        if (event.key === "r") {
          event.preventDefault(); event.stopPropagation();
          setSelectedWorkflowId(rowId);
          focusDenseRowDetailPanel("accounting-approval-detail");
        } else if (event.key === "a" && detail?.workflowId === rowId) {
          const reviewer = event.currentTarget.querySelector<HTMLInputElement>("[data-approval-reviewer-input]");
          if (!reviewer || reviewer.disabled) return;
          event.preventDefault(); event.stopPropagation();
          const disclosure = reviewer.closest("details");
          if (disclosure) disclosure.open = true;
          reviewer.focus();
        }
      }}>
      <div className="workspace-section-subheader">
        <div className="min-w-0">
          <p className="eyebrow-label">Approvals</p>
          <h3 id="accounting-approvals-heading" className="workspace-section-title">Approval queue and audit gate</h3>
          <p className="workspace-section-summary">Close approvals, missing evidence, signer context, and audit history come from the operations-continuity workflow.</p>
        </div>
        <Button type="button" size="sm" variant="outline" disabled={loading} busy={loading} busyLabel="Refreshing approvals" onClick={() => void refreshWorkflows()}>
          <RefreshCcw className={cn("h-3.5 w-3.5", loading && "animate-spin")} aria-hidden="true" />
          Refresh
        </Button>
      </div>
      <div className="grid gap-4 xl:grid-cols-[minmax(0,1fr)_26rem]">
        <Card className="panel-surface">
          <CardHeader>
            <CardTitle className="flex items-center gap-2 text-base">
              <UserCheck className="h-4 w-4 text-primary" aria-hidden="true" />
              Approval queue
            </CardTitle>
            <CardDescription>Pending, blocked, approved, and rejected close workflows ready for sign-off review.</CardDescription>
          </CardHeader>
          <CardContent className="space-y-3">
            <p className="text-xs text-muted-foreground">With a queue row focused: j/k or arrow keys move; Enter or r reviews; a opens reviewer assignment when available. Escape returns from detail to the selected row.</p>
            <ApprovalStatusMessage loading={loading} error={error} empty={!loading && workflows.length === 0} />
            {workflows.length > 0 ? (
              <div className="overflow-hidden rounded-md border border-border/70" role="region" aria-label="Accounting approval queue">
                <TableWorkbench<OperationsContinuityWorkflowSummary>
                  workbenchId="accounting-approval-workflows" sourceKey={new URLSearchParams(search).get("fundAccountId") ?? "authorized-workflows"}
                  sourceDescription="All authorized operations-continuity workflow summaries in the current approval queue."
                  completePopulation={!loading && !error} rows={workflows} getRowId={(workflow) => workflow.workflowId}
                  getSearchText={(workflow) => `${workflow.periodId} ${workflow.status} ${workflow.updatedAtUtc}`}
                  ariaLabel="Accounting approval queue" caption="Accounting approval queue backed by operations continuity workflows."
                  emptyText="No approval workflows are available." selectedRowId={selectedWorkflowId} onRowSelect={(workflow) => setSelectedWorkflowId(workflow.workflowId)}
                  getRowAriaControls={() => "accounting-approval-detail"} getRowAriaExpanded={(workflow) => workflow.workflowId === selectedWorkflowId}
                  getRowAriaLabel={(workflow) => `${workflow.periodId}, ${approvalWorkflowStatusLabel(workflow, detail?.workflowId === workflow.workflowId ? detail : null)}`}
                  columns={[
                    { id: "period", label: "Period", render: (workflow) => workflow.periodId, exportValue: (workflow) => workflow.periodId },
                    { id: "status", label: "Status", render: (workflow) => <DesignSystemStatus status={workflow.status}>{approvalWorkflowStatusLabel(workflow, detail?.workflowId === workflow.workflowId ? detail : null)}</DesignSystemStatus>, exportValue: (workflow) => workflow.status },
                    { id: "signer", label: "Signer", render: (workflow) => approvalSignerLabel(detail?.workflowId === workflow.workflowId ? detail.approvals : []), exportValue: (workflow) => detail?.workflowId === workflow.workflowId ? approvalSignerLabel(detail.approvals) : null },
                    { id: "evidence", label: "Evidence", render: (workflow) => approvalEvidenceSummary(detail?.workflowId === workflow.workflowId ? detail : null), exportValue: (workflow) => detail?.workflowId === workflow.workflowId ? approvalEvidenceSummary(detail) : null },
                    { id: "updated", label: "Updated", render: (workflow) => <span className="whitespace-nowrap">{formatApprovalDate(workflow.updatedAtUtc)}</span>, exportValue: (workflow) => workflow.updatedAtUtc }
                  ]}
                />
              </div>
            ) : null}
          </CardContent>
        </Card>

        <DetailRail id="accounting-approval-detail" ariaLabel="Selected approval detail"
          title={selectedWorkflow ? `${selectedWorkflow.periodId} close workflow` : "Choose an approval"}
          status={selectedWorkflow ? <DesignSystemStatus status={approvalWorkflowStatusLabel(selectedWorkflow, detail)}>{approvalWorkflowStatusLabel(selectedWorkflow, detail)}</DesignSystemStatus> : undefined}
          fields={selectedWorkflow ? [
            { label: "Period", value: selectedWorkflow.periodId },
            { label: "Workflow state", value: approvalWorkflowStatusLabel(selectedWorkflow, detail) },
            { label: "Approval decision", value: selectedApproval ? splitApprovalWords(selectedApproval.status) : "Not submitted" },
            { label: "Required signers", value: approvalSignerLabel(detail?.approvals ?? []) },
            { label: "Missing evidence", value: missingApprovalEvidenceLabel(detail) }
          ] : []}
          actions={selectedWorkflow ? <>
            <Button size="sm" disabled={approveDisabledReason !== null} disabledReason={approveDisabledReason} busy={action === "approve"} busyLabel="Approving" onClick={() => void runDecision("approve")}>Approve</Button>
            <Button size="sm" variant="outline" disabled={rejectDisabledReason !== null} disabledReason={rejectDisabledReason} busy={action === "reject"} busyLabel="Rejecting" onClick={() => void runDecision("reject")}>Reject</Button>
            <Button size="sm" variant="ghost" disabled={requestChangesDisabledReason !== null} disabledReason={requestChangesDisabledReason} busy={action === "request-changes"} busyLabel="Requesting changes" onClick={() => void runDecision("request-changes")}>Request changes</Button>
          </> : undefined}>
          <div className="mt-4 space-y-4 text-sm">
            {detailLoading ? <p role="status" className="text-muted-foreground">Loading selected approval detail...</p> : null}
            {detailError ? <StatusBanner tone="danger" role="alert" title={detailError} /> : null}
            {actionError ? <StatusBanner tone="danger" role="alert" title={actionError} /> : null}
            {selectedWorkflow ? (
              <>
                <TechnicalDetails label="Workflow identifiers">
                  <div className="grid gap-2 sm:grid-cols-2">
                    <AccountingValue label="Workflow" value={selectedWorkflow.workflowId} />
                    <AccountingValue label="Approval ID" value={selectedApproval?.approvalId ?? requestedApprovalId ?? "Not yet created"} />
                    <AccountingValue label="Fund account" value={selectedWorkflow.fundAccountId} />
                  </div>
                </TechnicalDetails>
                <div className="rounded-md border border-border/70 bg-secondary/20 px-3 py-3">
                  <div className="text-xs font-semibold uppercase text-muted-foreground">Why blocked</div>
                  <p className="mt-2 leading-6 text-muted-foreground">{approvalBlockedReason(detail)}</p>
                </div>
                <div className="rounded-md border border-border/70 bg-secondary/20 px-3 py-3">
                  <div className="text-xs font-semibold uppercase text-muted-foreground">Next action</div>
                  <p className="mt-2 leading-6 text-muted-foreground">{approvalNextAction(detail, selectedWorkflow)}</p>
                </div>
                {detail?.workflowId === selectedWorkflowId ? <ApprovalCollaborationPanel key={detail.workflowId} workflowId={detail.workflowId} onChanged={() => { setDetailReload((value) => value + 1); void refreshWorkflows(); }} /> : null}
              </>
            ) : (
              <p className="text-muted-foreground">No approval workflow is selected.</p>
            )}
          </div>
        </DetailRail>
      </div>

      <Card className="panel-surface" role="region" aria-label="Approval audit trail">
        <CardHeader>
          <CardTitle className="text-base">Full audit trail</CardTitle>
          <CardDescription>Hash-linked workflow timeline for the selected approval item.</CardDescription>
        </CardHeader>
        <CardContent>
          {detail?.timeline.length ? (
            <div className="space-y-2">
              {detail.timeline.map((entry) => <ApprovalTimelineRow key={entry.auditId} entry={entry} />)}
            </div>
          ) : (
            <p className="text-sm text-muted-foreground">{detailLoading ? "Loading audit trail..." : "No audit trail rows are available for the selected approval."}</p>
          )}
        </CardContent>
      </Card>
    </section>
  );
}

function ApprovalStatusMessage({ loading, error, empty }: { loading: boolean; error: string | null; empty: boolean }) {
  if (loading) {
    return <p role="status" className="text-sm text-muted-foreground">Loading approval queue...</p>;
  }

  if (error) {
    return <StatusBanner tone="danger" role="alert" title={error} />;
  }

  if (empty) {
    return <p className="rounded-md border border-border/70 bg-secondary/25 px-3 py-3 text-sm text-muted-foreground">No approval workflows are available.</p>;
  }

  return null;
}

function ApprovalTimelineRow({ entry }: { entry: OperationsTimelineEntry }) {
  return (
    <div className="rounded-md border border-border/70 px-3 py-2 text-sm">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <span className="font-semibold text-foreground">{splitApprovalWords(entry.eventType)}</span>
        <span className="font-mono text-[11px] text-muted-foreground">{formatApprovalDate(entry.occurredAtUtc)}</span>
      </div>
      <p className="mt-1 text-muted-foreground">{entry.rationale || `${splitApprovalWords(entry.fromState)} -> ${splitApprovalWords(entry.toState)}`}</p>
      <div className="mt-2 text-[11px] text-muted-foreground">Evidence: {entry.references.length}</div>
      <TechnicalDetails label="Audit metadata" className="mt-2">
        <div className="grid gap-1 text-[11px] text-muted-foreground sm:grid-cols-2">
          <span>Actor: {entry.actor || "unknown"}</span>
          <span className="break-all">Hash: {entry.currentHash || "pending"}</span>
          <span className="break-all sm:col-span-2">Audit ID: {entry.auditId}</span>
        </div>
      </TechnicalDetails>
    </div>
  );
}

function selectApproval(workflow: OperationsContinuityWorkflow | null, requestedApprovalId: string | null): OperationsApproval | null {
  if (!workflow) {
    return null;
  }

  if (requestedApprovalId) {
    const requested = workflow.approvals.find((approval) => approval.approvalId === requestedApprovalId);
    if (requested) {
      return requested;
    }
  }

  return [...workflow.approvals].sort((left, right) => {
    const leftTime = left.decidedAtUtc ?? left.submittedAtUtc ?? "";
    const rightTime = right.decidedAtUtc ?? right.submittedAtUtc ?? "";
    return rightTime.localeCompare(leftTime);
  })[0] ?? null;
}
