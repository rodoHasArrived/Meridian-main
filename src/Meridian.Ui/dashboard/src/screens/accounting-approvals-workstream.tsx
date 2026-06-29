import { RefreshCcw, UserCheck } from "lucide-react";
import { useEffect, useMemo, useState } from "react";
import { useLocation } from "react-router-dom";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import {
  approveOperationsContinuityWorkflow,
  getOperationsContinuityWorkflow,
  getOperationsContinuityWorkflows,
  rejectOperationsContinuityWorkflow
} from "@/lib/api";
import { cn } from "@/lib/utils";
import type {
  OperationsApproval,
  OperationsApprovalState,
  OperationsContinuityWorkflow,
  OperationsContinuityWorkflowSummary,
  OperationsTimelineEntry,
  OperationsWorkflowBlocker
} from "@/types";

export function AccountingApprovalsWorkstream() {
  const { search } = useLocation();
  const [workflows, setWorkflows] = useState<OperationsContinuityWorkflowSummary[]>([]);
  const [selectedWorkflowId, setSelectedWorkflowId] = useState<string | null>(null);
  const [detail, setDetail] = useState<OperationsContinuityWorkflow | null>(null);
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
  }, [selectedWorkflowId]);

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
    <section id="accounting-approvals" className="workspace-section-band" aria-labelledby="accounting-approvals-heading">
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
            <ApprovalStatusMessage loading={loading} error={error} empty={!loading && workflows.length === 0} />
            {workflows.length > 0 ? (
              <div className="overflow-hidden rounded-md border border-border/70" role="region" aria-label="Accounting approval queue">
                <table className="w-full text-left text-sm">
                  <caption className="sr-only">Accounting approval queue backed by operations continuity workflows.</caption>
                  <thead className="bg-secondary/40 text-xs uppercase text-muted-foreground">
                    <tr>
                      <th className="px-3 py-2 font-medium">Period</th>
                      <th className="px-3 py-2 font-medium">Status</th>
                      <th className="px-3 py-2 font-medium">Signer</th>
                      <th className="px-3 py-2 font-medium">Evidence</th>
                      <th className="px-3 py-2 font-medium">Updated</th>
                    </tr>
                  </thead>
                  <tbody>
                    {workflows.map((workflow) => {
                      const selected = workflow.workflowId === selectedWorkflowId;
                      return (
                        <tr
                          key={workflow.workflowId}
                          tabIndex={0}
                          aria-selected={selected}
                          className={cn("cursor-pointer border-t border-border/60 hover:bg-secondary/30", selected && "bg-primary/10")}
                          onClick={() => setSelectedWorkflowId(workflow.workflowId)}
                          onKeyDown={(event) => {
                            if (event.key === "Enter" || event.key === " ") {
                              event.preventDefault();
                              setSelectedWorkflowId(workflow.workflowId);
                            }
                          }}
                        >
                          <td className="px-3 py-2">
                            <span className="block font-semibold text-foreground">{workflow.periodId}</span>
                            <span className="block font-mono text-[11px] text-muted-foreground">{workflow.workflowId}</span>
                          </td>
                          <td className="px-3 py-2"><Badge variant={approvalQueueStatusTone(workflow)}>{approvalQueueStatusLabel(workflow)}</Badge></td>
                          <td className="px-3 py-2 text-muted-foreground">{approvalSignerLabel(detail?.workflowId === workflow.workflowId ? detail.approvals : [])}</td>
                          <td className="px-3 py-2 text-muted-foreground">{approvalEvidenceSummary(detail?.workflowId === workflow.workflowId ? detail : null)}</td>
                          <td className="px-3 py-2 font-mono text-muted-foreground">{formatApprovalDate(workflow.updatedAtUtc)}</td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>
            ) : null}
          </CardContent>
        </Card>

        <Card className="panel-surface" role="region" aria-label="Selected approval detail">
          <CardHeader>
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <CardTitle className="text-base">Selected item detail</CardTitle>
                <CardDescription>{selectedWorkflow ? `${selectedWorkflow.periodId} / ${selectedWorkflow.fundAccountId}` : "Select an approval queue row."}</CardDescription>
              </div>
              {selectedApproval ? <Badge variant={approvalStatusTone(selectedApproval.status)}>{selectedApproval.status}</Badge> : null}
            </div>
          </CardHeader>
          <CardContent className="space-y-4 text-sm">
            {detailLoading ? <p role="status" className="text-muted-foreground">Loading selected approval detail...</p> : null}
            {detailError ? <div role="alert" className="rounded-md border border-danger/30 bg-danger/10 px-3 py-2 text-danger">{detailError}</div> : null}
            {actionError ? <div role="alert" className="rounded-md border border-danger/30 bg-danger/10 px-3 py-2 text-danger">{actionError}</div> : null}
            {selectedWorkflow ? (
              <>
                <div className="grid gap-2 sm:grid-cols-2">
                  <ApprovalValue label="Workflow" value={selectedWorkflow.workflowId} />
                  <ApprovalValue label="Approval ID" value={selectedApproval?.approvalId ?? requestedApprovalId ?? "No approval row"} />
                  <ApprovalValue label="Required signers" value={approvalSignerLabel(detail?.approvals ?? [])} />
                  <ApprovalValue label="Missing evidence" value={missingApprovalEvidenceLabel(detail)} />
                </div>
                <div className="rounded-md border border-border/70 bg-secondary/20 px-3 py-3">
                  <div className="text-xs font-semibold uppercase text-muted-foreground">Why blocked</div>
                  <p className="mt-2 leading-6 text-muted-foreground">{approvalBlockedReason(detail)}</p>
                </div>
                <div className="rounded-md border border-border/70 bg-secondary/20 px-3 py-3">
                  <div className="text-xs font-semibold uppercase text-muted-foreground">Next action</div>
                  <p className="mt-2 leading-6 text-muted-foreground">{approvalNextAction(detail, selectedWorkflow)}</p>
                </div>
                <div className="flex flex-wrap gap-2">
                  <Button size="sm" disabled={approveDisabledReason !== null} disabledReason={approveDisabledReason} busy={action === "approve"} busyLabel="Approving" onClick={() => void runDecision("approve")}>Approve</Button>
                  <Button size="sm" variant="outline" disabled={rejectDisabledReason !== null} disabledReason={rejectDisabledReason} busy={action === "reject"} busyLabel="Rejecting" onClick={() => void runDecision("reject")}>Reject</Button>
                  <Button size="sm" variant="ghost" disabled={requestChangesDisabledReason !== null} disabledReason={requestChangesDisabledReason} busy={action === "request-changes"} busyLabel="Requesting changes" onClick={() => void runDecision("request-changes")}>Request changes</Button>
                </div>
              </>
            ) : (
              <p className="text-muted-foreground">No approval workflow is selected.</p>
            )}
          </CardContent>
        </Card>
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
    return <div role="alert" className="rounded-md border border-danger/30 bg-danger/10 px-3 py-2 text-sm text-danger">{error}</div>;
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
      <div className="mt-2 flex flex-wrap gap-2 text-[11px] text-muted-foreground">
        <span>Actor: {entry.actor || "unknown"}</span>
        <span>Hash: {entry.currentHash || "pending"}</span>
        <span>Evidence: {entry.references.length}</span>
      </div>
    </div>
  );
}

function ApprovalValue({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-md border border-border/70 bg-background/50 px-3 py-2" aria-label={`${label}: ${value}`}>
      <div className="text-[11px] uppercase text-muted-foreground">{label}</div>
      <div className="mt-1 font-mono text-sm text-foreground">{value}</div>
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

function approvalQueueStatusLabel(workflow: OperationsContinuityWorkflowSummary): string {
  const approvalGate = workflow.gates.find((gate) => gate.gateKey === "Approval") ?? null;
  if (workflow.status === "Blocked" || approvalGate?.status === "Blocked") {
    return "Blocked";
  }

  if (workflow.status === "Closed" || workflow.status === "ReadyForClose" || approvalGate?.status === "Passed") {
    return "Approved";
  }

  return "Pending";
}

function approvalQueueStatusTone(workflow: OperationsContinuityWorkflowSummary): "success" | "warning" | "danger" | "outline" {
  const label = approvalQueueStatusLabel(workflow);
  if (label === "Approved") return "success";
  if (label === "Blocked") return "danger";
  return "warning";
}

function approvalStatusTone(status: OperationsApprovalState): "success" | "warning" | "danger" | "outline" {
  if (status === "Approved") return "success";
  if (status === "Rejected") return "danger";
  if (status === "Pending") return "outline";
  return "warning";
}

function approvalSignerLabel(approvals: OperationsApproval[]): string {
  const signers = approvals
    .flatMap((approval) => [approval.reviewer, approval.operator])
    .map((value) => value?.trim())
    .filter((value): value is string => Boolean(value));
  const unique = [...new Set(signers)];
  return unique.length > 0 ? unique.join(", ") : "Reviewer pending";
}

function approvalEvidenceSummary(workflow: OperationsContinuityWorkflow | null): string {
  if (!workflow) {
    return "Detail pending";
  }

  const count = workflow.evidenceLinks.length
    + workflow.reportPackReadiness.evidenceLinks.length
    + workflow.approvals.reduce((total, approval) => total + approval.evidenceLinks.length, 0);
  return count === 0 ? "No evidence links" : `${count} evidence link${count === 1 ? "" : "s"}`;
}

function missingApprovalEvidenceLabel(workflow: OperationsContinuityWorkflow | null): string {
  if (!workflow) {
    return "Detail pending";
  }

  const missing: string[] = [];
  if (!resolveApprovalReportPackId(workflow)) {
    missing.push("report pack");
  }

  if (!workflow.reportPackReadiness.isReady) {
    missing.push("report readiness");
  }

  const blockerEvidenceMissing = approvalBlockers(workflow).some((blocker) => blocker.evidenceLinks.length === 0);
  if (blockerEvidenceMissing) {
    missing.push("blocker evidence");
  }

  return missing.length === 0 ? "None" : missing.join(", ");
}

function approvalBlockedReason(workflow: OperationsContinuityWorkflow | null): string {
  if (!workflow) {
    return "Approval detail is still loading.";
  }

  const blockers = approvalBlockers(workflow);
  if (blockers.length > 0) {
    return blockers.map((blocker) => blocker.message).join(" ");
  }

  if (!workflow.reportPackReadiness.isReady) {
    return workflow.reportPackReadiness.blockingReason ?? "Report pack readiness is still blocked.";
  }

  return "No approval blockers are surfaced for the selected workflow.";
}

function approvalNextAction(workflow: OperationsContinuityWorkflow | null, summary: OperationsContinuityWorkflowSummary): string {
  const source = workflow ?? summary;
  const approvalAction = source.nextActions.find((action) => action.gate === "Approval") ?? source.nextActions[0] ?? null;
  if (approvalAction) {
    return approvalAction.label;
  }

  if (approvalQueueStatusLabel(summary) === "Approved") {
    return "Approval is complete; continue to evidence production or close package publication.";
  }

  return "Review blockers, evidence, and required signers before taking an approval action.";
}

function approvalBlockers(workflow: OperationsContinuityWorkflow): OperationsWorkflowBlocker[] {
  return [
    ...workflow.blockers,
    ...workflow.gates.flatMap((gate) => gate.gateKey === "Approval" ? gate.blockers : [])
  ];
}

function buildApprovalActionDisabledReason(
  workflow: OperationsContinuityWorkflow | OperationsContinuityWorkflowSummary | null,
  approval: OperationsApproval | null,
  action: string | null
): string | null {
  if (action) {
    return "Wait for the current approval action to finish.";
  }

  if (!workflow) {
    return "Select an approval before approving.";
  }

  if (approval?.status === "Approved") {
    return "This approval is already approved.";
  }

  if ("reportPackReadiness" in workflow && !resolveApprovalReportPackId(workflow)) {
    return "Approval requires a report pack id from the selected workflow.";
  }

  return null;
}

function resolveApprovalReportPackId(workflow: OperationsContinuityWorkflow | OperationsContinuityWorkflowSummary): string | null {
  if ("closePackage" in workflow && workflow.closePackage?.reportPackId) {
    return workflow.closePackage.reportPackId;
  }

  if ("reportPackReadiness" in workflow && workflow.reportPackReadiness.reportPackId) {
    return workflow.reportPackReadiness.reportPackId;
  }

  return null;
}

function formatApprovalDate(value: string | null | undefined): string {
  if (!value) {
    return "Not recorded";
  }

  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString("en-US", { timeZone: "UTC" });
}

function splitApprovalWords(value: string): string {
  return value.replace(/([a-z])([A-Z])/g, "$1 $2");
}

function formatApprovalError(err: unknown, fallback: string): string {
  return err instanceof Error ? err.message || fallback : fallback;
}
