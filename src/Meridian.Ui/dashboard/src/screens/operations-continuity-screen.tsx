import { useCallback, useMemo, useState } from "react";
import { AlertTriangle, ArrowRight, CalendarDays, Gauge, GitBranch, ListChecks, Lock, RefreshCcw, Workflow } from "lucide-react";
import { Link } from "react-router-dom";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import {
  acknowledgeOperationsContinuityChecklistTask,
  approveOperationsContinuityWorkflow,
  assignOperationsContinuityBreakCase,
  closeOperationsContinuityWorkflow,
  reopenOperationsContinuityWorkflow,
  rejectOperationsContinuityWorkflow,
  resolveOperationsContinuityBreakCase,
  submitOperationsContinuityApproval
} from "@/lib/api";
import { DenseDataTable, EntitySummary, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { cn } from "@/lib/utils";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { readinessToneToBadgeVariant, readinessToneToPanelClass } from "@/lib/shared-tone-mappings";
import {
  useOperationsContinuityScreenViewModel,
  type FinancialOperationsCommandSpineRow,
  type FinancialOperationsOperatorQueueRow,
  type OperationsAccountingRecordEvidenceRow,
  type OperationsContinuityCloseCalendarRow,
  type OperationsContinuityCloseCockpitApprovalRow,
  type OperationsContinuityCloseCockpitLaneRow,
  type OperationsContinuityCloseCockpitWorkflowRow,
  type OperationsContinuityBreakCaseRow,
  type OperationsContinuityBlockerRow,
  type OperationsContinuityChecklistRow,
  type OperationsContinuityDashboardMetricRow,
  type OperationsContinuityEvidencePackageRow,
  type OperationsContinuityGateRow,
  type OperationsContinuityNavSupportPackageRow,
  type OperationsContinuityReconciliationLaneRow,
  type OperationsReviewedAutomationArtifactRow,
  type OperationsContinuityTimelineRow,
  type OperationsContinuityWorkflowApprovalRow,
  type OperationsContinuityWorkflowRow
} from "@/screens/operations-continuity-screen.view-model";

const workflowColumns: DenseDataTableColumn<OperationsContinuityWorkflowRow>[] = [
  {
    id: "workflow",
    label: "Workflow",
    render: (row) => (
      <span className="block min-w-0">
        <span className="block font-semibold text-foreground">{row.title}</span>
        <span className="mt-1 block break-words font-mono text-[11px] text-muted-foreground">{row.subtitle}</span>
      </span>
    )
  },
  {
    id: "status",
    label: "Status",
    render: (row) => <Badge variant={readinessToneToBadgeVariant(row.statusTone)}>{row.statusLabel}</Badge>
  },
  {
    id: "gates",
    label: "Gates",
    render: (row) => (
      <span className="block text-xs leading-5">
        <span className="block font-medium text-foreground">{row.gatesLabel}</span>
        <span className="block text-muted-foreground">{row.blockersLabel}</span>
      </span>
    )
  },
  {
    id: "updated",
    label: "Updated",
    render: (row) => <span className="font-mono text-xs text-muted-foreground">{row.updatedLabel}</span>
  }
];

const gateColumns: DenseDataTableColumn<OperationsContinuityGateRow>[] = [
  {
    id: "gate",
    label: "Gate",
    render: (row) => (
      <span className="block min-w-0">
        <span className="block font-semibold text-foreground">{row.label}</span>
        <span className="mt-1 block text-xs leading-5 text-muted-foreground">{row.detail}</span>
      </span>
    )
  },
  {
    id: "status",
    label: "Status",
    render: (row) => <Badge variant={readinessToneToBadgeVariant(row.statusTone)}>{row.statusLabel}</Badge>
  },
  {
    id: "required",
    label: "Scope",
    render: (row) => <span className="text-xs text-foreground/80">{row.requiredLabel}</span>
  },
  {
    id: "completed",
    label: "Completion",
    render: (row) => (
      <span className="block text-xs leading-5">
        <span className="block text-foreground">{row.blockerCountLabel}</span>
        <span className="block text-muted-foreground">{row.completedLabel}</span>
      </span>
    )
  }
];

const blockerColumns: DenseDataTableColumn<OperationsContinuityBlockerRow>[] = [
  {
    id: "code",
    label: "Blocker",
    render: (row) => (
      <span className="block min-w-0">
        <span className="block font-mono text-xs font-semibold text-foreground">{row.code}</span>
        <span className="mt-1 block text-xs leading-5 text-muted-foreground">{row.message}</span>
      </span>
    )
  },
  {
    id: "gate",
    label: "Gate",
    render: (row) => <span className="text-xs text-foreground/80">{row.gateLabel}</span>
  },
  {
    id: "severity",
    label: "Severity",
    render: (row) => <Badge variant={readinessToneToBadgeVariant(row.severityTone)}>{row.severityLabel}</Badge>
  },
  {
    id: "evidence",
    label: "Evidence",
    render: (row) => <span className="text-xs text-muted-foreground">{row.evidenceLabel}</span>
  }
];

const operationalDashboardColumns: DenseDataTableColumn<OperationsContinuityDashboardMetricRow>[] = [
  {
    id: "metric",
    label: "Metric",
    render: (row) => (
      <span className="block min-w-0">
        <span className="block font-semibold text-foreground">{row.label}</span>
        <span className="mt-1 block text-xs leading-5 text-muted-foreground">{row.detail}</span>
      </span>
    )
  },
  {
    id: "status",
    label: "Status",
    render: (row) => (
      <span className="block text-xs leading-5">
        <Badge variant={readinessToneToBadgeVariant(row.statusTone)}>{row.statusLabel}</Badge>
        <span className="mt-1 block font-mono tabular-nums text-foreground">{row.valueLabel}</span>
      </span>
    )
  },
  {
    id: "evidence",
    label: "Evidence",
    render: (row) => (
      <span className="block text-xs leading-5">
        <span className="block text-foreground">{row.evidenceLabel}</span>
        <span className="block text-muted-foreground">{row.requiredActionsLabel}</span>
      </span>
    )
  },
  {
    id: "route",
    label: "Route",
    render: (row) => row.routeHref ? (
      <Link className="text-xs" to={row.routeHref} aria-label={`Open operational dashboard metric ${row.label}`}>
        {row.routeLabel}
      </Link>
    ) : (
      <span className="text-xs text-muted-foreground">{row.routeLabel}</span>
    )
  }
];

const closeCalendarColumns: DenseDataTableColumn<OperationsContinuityCloseCalendarRow>[] = [
  {
    id: "scope",
    label: "Scope",
    render: (row) => (
      <span className="block min-w-0">
        <span className="block font-semibold text-foreground">{row.periodLabel}</span>
        <span className="mt-1 block text-xs leading-5 text-muted-foreground">{row.taskLabel}</span>
      </span>
    )
  },
  {
    id: "status",
    label: "Status",
    render: (row) => (
      <span className="block text-xs leading-5">
        <Badge variant={readinessToneToBadgeVariant(row.statusTone)}>{row.statusLabel}</Badge>
        <span className="mt-1 block text-muted-foreground">{row.dueLabel}</span>
      </span>
    )
  },
  {
    id: "readiness",
    label: "Readiness",
    render: (row) => (
      <span className="block text-xs leading-5">
        <Badge variant={readinessToneToBadgeVariant(row.readinessTone)}>{row.readinessLabel}</Badge>
        <span className="mt-1 block text-muted-foreground">{row.ownerLabel}</span>
      </span>
    )
  },
  {
    id: "controls",
    label: "Controls",
    render: (row) => (
      <span className="block text-xs leading-5 text-muted-foreground">
        <span className="block text-foreground">{row.blockerLabel}</span>
        <span className="block">{row.checklistLabel}</span>
        <span className="block">{row.approvalLabel}</span>
      </span>
    )
  },
  {
    id: "route",
    label: "Route",
    render: (row) => row.routeHref ? (
      <Link className="text-xs" to={row.routeHref} aria-label={`Open close calendar workflow ${row.periodLabel}`}>
        {row.routeLabel}
      </Link>
    ) : (
      <span className="text-xs text-muted-foreground">{row.routeLabel}</span>
    )
  }
];

interface FinancialOperationsCommandSpineColumnOptions {
  pendingStageId: string | null;
  onSubmitApproval: (row: FinancialOperationsCommandSpineRow) => void;
  onCloseWorkflow: (row: FinancialOperationsCommandSpineRow) => void;
}

function buildFinancialOperationsCommandSpineColumns({
  pendingStageId,
  onSubmitApproval,
  onCloseWorkflow
}: FinancialOperationsCommandSpineColumnOptions): DenseDataTableColumn<FinancialOperationsCommandSpineRow>[] {
  return [
    {
      id: "stage",
      label: "Stage",
      render: (row) => (
        <span className="block min-w-0">
          <span className="block font-semibold text-foreground">{row.stageLabel}</span>
          <span className="mt-1 block text-xs leading-5 text-muted-foreground">{row.detail}</span>
        </span>
      )
    },
    {
      id: "command",
      label: "Command",
      render: (row) => (
        <span className="block text-xs leading-5">
          <Badge variant={readinessToneToBadgeVariant(row.statusTone)}>{row.statusLabel}</Badge>
          <span className="mt-1 block text-foreground">{row.commandLabel}</span>
          <span className="block font-mono text-muted-foreground">{row.postureLabel}</span>
        </span>
      )
    },
    {
      id: "guard",
      label: "Guard / evidence",
      render: (row) => (
        <span className="block text-xs leading-5">
          <span className="block text-foreground">{row.guardLabel}</span>
          <span className="block text-muted-foreground">{row.evidenceLabel}</span>
        </span>
      )
    },
    {
      id: "route",
      label: "Route / action",
      render: (row) => {
        const busy = pendingStageId === row.id;
        const activeCommandDisabledReason = pendingStageId && !busy
          ? "Wait for the active Financial Operations command to finish."
          : null;
        const approvalDisabledReason = activeCommandDisabledReason ?? row.submitApprovalDisabledReason;
        const closeDisabledReason = activeCommandDisabledReason ?? row.closeWorkflowDisabledReason;

        return (
          <span className="block text-xs leading-5">
            {row.routeHref ? (
              <Link className="link-subtle inline-flex" to={row.routeHref} aria-label={`Open Financial Operations command stage ${row.stageLabel}`}>
                {row.routeLabel}
              </Link>
            ) : (
              <span className="text-muted-foreground">{row.routeLabel}</span>
            )}
            {row.id === "approve-results" ? (
              <Button
                type="button"
                size="sm"
                variant="outline"
                className="mt-2"
                busy={busy}
                busyLabel="Submitting"
                disabled={approvalDisabledReason !== null}
                disabledReason={approvalDisabledReason}
                aria-label={row.submitApprovalAriaLabel}
                onClick={() => onSubmitApproval(row)}
              >
                {row.submitApprovalLabel}
              </Button>
            ) : null}
            {row.id === "produce-evidence" ? (
              <Button
                type="button"
                size="sm"
                variant="outline"
                className="mt-2"
                busy={busy}
                busyLabel="Publishing"
                disabled={closeDisabledReason !== null}
                disabledReason={closeDisabledReason}
                aria-label={row.closeWorkflowAriaLabel}
                onClick={() => onCloseWorkflow(row)}
              >
                {row.closeWorkflowLabel}
              </Button>
            ) : null}
          </span>
        );
      }
    }
  ];
}

const financialOperationsQueueColumns: DenseDataTableColumn<FinancialOperationsOperatorQueueRow>[] = [
  {
    id: "work-item",
    label: "Work item",
    render: (row) => (
      <span className="block min-w-0">
        <span className="eyebrow-label">{row.kindLabel}</span>
        <span className="mt-1 block font-semibold text-foreground">{row.title}</span>
        <span className="mt-1 block text-xs leading-5 text-muted-foreground">{row.detail}</span>
      </span>
    )
  },
  {
    id: "status",
    label: "Status",
    render: (row) => <Badge variant={readinessToneToBadgeVariant(row.statusTone)}>{row.statusLabel}</Badge>
  },
  {
    id: "owner",
    label: "Owner / due",
    render: (row) => (
      <span className="block text-xs leading-5">
        <span className="block font-mono text-foreground/80">{row.ownerLabel}</span>
        <span className="block text-muted-foreground">{row.dueLabel}</span>
      </span>
    )
  },
  {
    id: "evidence",
    label: "Evidence / action",
    render: (row) => (
      <span className="block text-xs leading-5">
        <span className="block text-foreground">{row.evidenceLabel}</span>
        <span className="block text-muted-foreground">{row.actionLabel}</span>
        {row.routeHref ? (
          <Link className="link-subtle mt-1 inline-flex" to={row.routeHref} aria-label={`Open Financial Operations queue item: ${row.title}`}>
            {row.routeLabel}
          </Link>
        ) : (
          <span className="mt-1 block text-muted-foreground">{row.routeLabel}</span>
        )}
      </span>
    )
  }
];

function buildWorkflowApprovalHistoryColumns({
  pendingDecision,
  onRunDecision
}: WorkflowApprovalHistoryColumnOptions): DenseDataTableColumn<OperationsContinuityWorkflowApprovalRow>[] {
  return [
    {
      id: "approval",
      label: "Approval",
      render: (row) => (
        <span className="block min-w-0">
          <span className="block font-mono text-xs font-semibold text-foreground">{row.id}</span>
          <span className="mt-1 block text-xs leading-5 text-muted-foreground">{row.rationale}</span>
        </span>
      )
    },
    {
      id: "status",
      label: "Status",
      render: (row) => (
        <span className="block text-xs leading-5">
          <Badge variant={readinessToneToBadgeVariant(row.statusTone)}>{row.statusLabel}</Badge>
          <span className="mt-1 block text-muted-foreground">{row.decidedLabel}</span>
        </span>
      )
    },
    {
      id: "actor",
      label: "Actor / submitted",
      render: (row) => (
        <span className="block text-xs leading-5">
          <span className="block text-foreground">{row.actorLabel}</span>
          <span className="block text-muted-foreground">{row.submittedLabel}</span>
        </span>
      )
    },
    {
      id: "evidence",
      label: "Evidence",
      render: (row) => (
        <span className="block text-xs leading-5">
          <span className="block text-foreground">{row.evidenceLabel}</span>
          {row.evidenceHref ? (
            <Link className="link-subtle mt-1 inline-flex" to={row.evidenceHref} aria-label={`Open approval evidence for workflow approval ${row.id}`}>
              {row.evidenceRouteLabel}
            </Link>
          ) : (
            <span className="mt-1 block text-muted-foreground">{row.evidenceRouteLabel}</span>
          )}
        </span>
      )
    },
    {
      id: "decision",
      label: "Decision",
      render: (row) => {
        const approveBusy = pendingDecision?.approvalId === row.id && pendingDecision.kind === "approve";
        const rejectBusy = pendingDecision?.approvalId === row.id && pendingDecision.kind === "reject";
        const approveDisabledReason = pendingDecision && !approveBusy
          ? "Wait for the active approval decision command to finish."
          : row.approveWorkflowDisabledReason;
        const rejectDisabledReason = pendingDecision && !rejectBusy
          ? "Wait for the active approval decision command to finish."
          : row.rejectWorkflowDisabledReason;

        return (
          <span className="block text-xs leading-5">
            <span className="block text-muted-foreground">{row.decisionGuardLabel}</span>
            <span className="mt-2 flex flex-wrap gap-2">
              <Button
                type="button"
                size="sm"
                variant="outline"
                busy={approveBusy}
                busyLabel="Approving"
                disabled={approveDisabledReason !== null}
                disabledReason={approveDisabledReason}
                aria-label={row.approveWorkflowAriaLabel}
                onClick={() => onRunDecision(row, "approve")}
              >
                {row.approveWorkflowLabel}
              </Button>
              <Button
                type="button"
                size="sm"
                variant="outline"
                busy={rejectBusy}
                busyLabel="Rejecting"
                disabled={rejectDisabledReason !== null}
                disabledReason={rejectDisabledReason}
                aria-label={row.rejectWorkflowAriaLabel}
                onClick={() => onRunDecision(row, "reject")}
              >
                {row.rejectWorkflowLabel}
              </Button>
            </span>
          </span>
        );
      }
    }
  ];
}

const reviewedAutomationArtifactColumns: DenseDataTableColumn<OperationsReviewedAutomationArtifactRow>[] = [
  {
    id: "artifact",
    label: "Output",
    render: (row) => (
      <span className="block min-w-0">
        <span className="eyebrow-label">{row.kindLabel}</span>
        <span className="mt-1 block font-semibold text-foreground">{row.title}</span>
        <span className="mt-1 block text-xs leading-5 text-muted-foreground">{row.sourceSummary}</span>
      </span>
    )
  },
  {
    id: "status",
    label: "Review",
    render: (row) => (
      <span className="block text-xs leading-5">
        <Badge variant={readinessToneToBadgeVariant(row.statusTone)}>{row.statusLabel}</Badge>
        <span className="mt-1 block text-muted-foreground">{row.reviewLabel}</span>
        <span className="block font-mono text-muted-foreground">{row.confidenceLabel}</span>
      </span>
    )
  },
  {
    id: "evidence",
    label: "Evidence",
    render: (row) => (
      <span className="block text-xs leading-5">
        <span className="block text-foreground">{row.evidenceLabel}</span>
        <span className="block text-muted-foreground">{row.checklistLabel}</span>
      </span>
    )
  },
  {
    id: "action",
    label: "Action / block",
    render: (row) => (
      <span className="block text-xs leading-5">
        <span className="block text-foreground">{row.suggestedActionLabel}</span>
        <span className="block text-muted-foreground">{row.blockedActionLabel}</span>
      </span>
    )
  }
];

const evidencePackageColumns: DenseDataTableColumn<OperationsContinuityEvidencePackageRow>[] = [
  {
    id: "package",
    label: "Package",
    render: (row) => (
      <span className="block min-w-0">
        <span className="block font-semibold text-foreground">{row.label}</span>
        <span className="mt-1 block text-xs leading-5 text-muted-foreground">{row.summary}</span>
      </span>
    )
  },
  {
    id: "status",
    label: "Status",
    render: (row) => (
      <span className="block text-xs leading-5">
        <Badge variant={readinessToneToBadgeVariant(row.statusTone)}>{row.statusLabel}</Badge>
        <span className="mt-1 block text-muted-foreground">{row.readinessLabel}</span>
      </span>
    )
  },
  {
    id: "evidence",
    label: "Evidence",
    render: (row) => (
      <span className="block text-xs leading-5">
        <span className="block text-foreground">{row.evidenceLabel}</span>
        {row.evidenceHref ? (
          <Link className="block" to={row.evidenceHref} aria-label={`Open retained evidence for package ${row.label}`}>
            {row.evidenceRouteLabel}
          </Link>
        ) : (
          <span className="block text-muted-foreground">{row.evidenceRouteLabel}</span>
        )}
        <span className="block text-muted-foreground">{row.requiredActionsLabel}</span>
      </span>
    )
  },
  {
    id: "route",
    label: "Route",
    render: (row) => row.routeHref ? (
      <Link className="text-xs" to={row.routeHref} aria-label={`Open evidence package ${row.label}`}>
        {row.routeLabel}
      </Link>
    ) : (
      <span className="text-xs text-muted-foreground">{row.routeLabel}</span>
    )
  }
];

type BreakCommandKind = "assign" | "resolve";
type ApprovalDecisionKind = "approve" | "reject";

interface ReopenWorkflowFormState {
  incidentId: string;
  justification: string;
  approvalReference: string;
  impactSummary: string;
}

interface BreakCaseColumnOptions {
  pendingBreakCommand: { breakId: string; kind: BreakCommandKind } | null;
  onRunCommand: (row: OperationsContinuityBreakCaseRow, kind: BreakCommandKind) => void;
}

interface WorkflowApprovalHistoryColumnOptions {
  pendingDecision: { approvalId: string; kind: ApprovalDecisionKind } | null;
  onRunDecision: (row: OperationsContinuityWorkflowApprovalRow, kind: ApprovalDecisionKind) => void;
}

function buildBreakCaseColumns({
  pendingBreakCommand,
  onRunCommand
}: BreakCaseColumnOptions): DenseDataTableColumn<OperationsContinuityBreakCaseRow>[] {
  return [
    {
      id: "case",
      label: "Break",
      render: (row) => (
        <span className="block min-w-0">
          <span className="block break-words font-mono text-xs font-semibold text-foreground">{row.caseLabel}</span>
          <span className="mt-1 block text-xs leading-5 text-muted-foreground">{row.categoryLabel}</span>
        </span>
      )
    },
    {
      id: "status",
      label: "Status",
      render: (row) => (
        <span className="block text-xs leading-5">
          <Badge variant={readinessToneToBadgeVariant(row.statusTone)}>{row.statusLabel}</Badge>
          <span className="mt-1 block text-muted-foreground">{row.severityLabel}</span>
          <span className="block text-muted-foreground">{row.materialityLabel}</span>
        </span>
      )
    },
    {
      id: "owner",
      label: "Owner",
      render: (row) => (
        <span className="block text-xs leading-5">
          <span className="block font-mono text-foreground/80">{row.ownerLabel}</span>
          <span className="block text-muted-foreground">{row.dueLabel}</span>
          <span className="block text-muted-foreground">{row.slaLabel}</span>
        </span>
      )
    },
    {
      id: "escalation",
      label: "Escalation",
      render: (row) => (
        <span className="block text-xs leading-5">
          <span className="block text-foreground">{row.escalationLabel}</span>
          <span className="block text-muted-foreground">{row.evidenceLabel}</span>
          {row.evidenceHref ? (
            <Link className="block" to={row.evidenceHref} aria-label={`Open retained evidence for break ${row.caseLabel}`}>
              {row.evidenceRouteLabel}
            </Link>
          ) : (
            <span className="block text-muted-foreground">{row.evidenceRouteLabel}</span>
          )}
        </span>
      )
    },
    {
      id: "variance",
      label: "Variance",
      render: (row) => (
        <span className="block text-xs leading-5">
          <span className="block text-foreground">{row.varianceLabel}</span>
          <span className="block text-muted-foreground">{row.sourceLabel}</span>
          <span className="block text-muted-foreground">{row.rootCauseLabel}</span>
        </span>
      )
    },
    {
      id: "action",
      label: "Action",
      render: (row) => {
        const assignBusy = pendingBreakCommand?.breakId === row.id && pendingBreakCommand.kind === "assign";
        const resolveBusy = pendingBreakCommand?.breakId === row.id && pendingBreakCommand.kind === "resolve";
        const assignDisabledReason = pendingBreakCommand && !assignBusy
          ? "Wait for the active break command to finish."
          : row.assignDisabledReason;
        const resolveDisabledReason = pendingBreakCommand && !resolveBusy
          ? "Wait for the active break command to finish."
          : row.resolveDisabledReason;

        return (
          <span className="block text-xs leading-5">
            <span className="block text-foreground">{row.actionLabel}</span>
            <span className="block text-muted-foreground">{row.approvalLabel}</span>
            <span className="block text-muted-foreground">{row.blockedOutputsLabel}</span>
            <span className="block text-muted-foreground">{row.commandPostureLabel}</span>
            <span className="block text-muted-foreground">{row.commandGuardLabel}</span>
            <span className="mt-2 flex flex-wrap gap-2">
              <Button
                type="button"
                size="sm"
                variant="outline"
                busy={assignBusy}
                busyLabel="Assigning"
                disabled={assignDisabledReason !== null}
                disabledReason={assignDisabledReason}
                aria-label={row.assignAriaLabel}
                onClick={() => onRunCommand(row, "assign")}
              >
                {row.assignLabel}
              </Button>
              <Button
                type="button"
                size="sm"
                variant="outline"
                busy={resolveBusy}
                busyLabel="Resolving"
                disabled={resolveDisabledReason !== null}
                disabledReason={resolveDisabledReason}
                aria-label={row.resolveAriaLabel}
                onClick={() => onRunCommand(row, "resolve")}
              >
                {row.resolveLabel}
              </Button>
            </span>
            {row.caseworkHref ? (
              <Link className="link-subtle mt-1 inline-flex" to={row.caseworkHref} aria-label={`Open exception casework for break ${row.caseLabel}`}>
                {row.caseworkRouteLabel}
              </Link>
            ) : (
              <span className="mt-1 block text-muted-foreground">{row.caseworkRouteLabel}</span>
            )}
            <span className="block font-mono text-muted-foreground">{row.symbolLabel}</span>
          </span>
        );
      }
    }
  ];
}

interface ChecklistColumnOptions {
  pendingTaskId: string | null;
  onAcknowledge: (row: OperationsContinuityChecklistRow) => void;
}

function buildChecklistColumns({
  pendingTaskId,
  onAcknowledge
}: ChecklistColumnOptions): DenseDataTableColumn<OperationsContinuityChecklistRow>[] {
  return [
    {
      id: "task",
      label: "Task",
      render: (row) => (
        <span className="block min-w-0">
          <span className="block font-semibold text-foreground">{row.label}</span>
          <span className="mt-1 block text-xs leading-5 text-muted-foreground">{row.requiredEvidence}</span>
        </span>
      )
    },
    {
      id: "status",
      label: "Status",
      render: (row) => <Badge variant={readinessToneToBadgeVariant(row.statusTone)}>{row.statusLabel}</Badge>
    },
    {
      id: "owner",
      label: "Owner",
      render: (row) => (
        <span className="block text-xs leading-5">
          <span className="block font-mono text-foreground/80">{row.ownerLabel}</span>
          <span className="block text-muted-foreground">{row.approvalLabel}</span>
        </span>
      )
    },
    {
      id: "due",
      label: "Due",
      render: (row) => (
        <span className="block text-xs leading-5">
          <span className="block text-foreground">{row.dueLabel}</span>
          <span className="block text-muted-foreground">{row.expiresLabel}</span>
        </span>
      )
    },
    {
      id: "evidence",
      label: "Evidence",
      render: (row) => (
        <span className="block text-xs leading-5">
          <span className="block font-mono text-muted-foreground">{row.evidenceLabel}</span>
          {row.remediationHref ? (
            <Link to={row.remediationHref} aria-label={`Open remediation for ${row.label}`}>
              {row.remediationLabel}
            </Link>
          ) : (
            <span className="block text-muted-foreground">{row.remediationLabel}</span>
          )}
        </span>
      )
    },
    {
      id: "acknowledgement",
      label: "Acknowledgement",
      render: (row) => {
        const busy = pendingTaskId === row.id;
        const disabledReason = pendingTaskId && !busy
          ? "Wait for the active checklist acknowledgement command to finish."
          : row.acknowledgeDisabledReason;

        return (
          <span className="block text-xs leading-5">
            <span className="block text-muted-foreground">{row.acknowledgementLabel}</span>
            <span className="mt-1 block font-medium text-foreground">{row.acknowledgementCommandPostureLabel}</span>
            <span className="block font-mono text-muted-foreground">{row.acknowledgementGuardLabel}</span>
            <Button
              type="button"
              size="sm"
              variant="outline"
              className="mt-2"
              busy={busy}
              busyLabel="Acknowledging"
              disabled={disabledReason !== null}
              disabledReason={disabledReason}
              aria-label={row.acknowledgeAriaLabel}
              onClick={() => onAcknowledge(row)}
            >
              {row.acknowledgeLabel}
            </Button>
          </span>
        );
      }
    }
  ];
}

const reconciliationLaneColumns: DenseDataTableColumn<OperationsContinuityReconciliationLaneRow>[] = [
  {
    id: "lane",
    label: "Lane",
    render: (row) => (
      <span className="block min-w-0">
        <span className="block font-semibold text-foreground">{row.label}</span>
        <span className="mt-1 block text-xs leading-5 text-muted-foreground">{row.summary}</span>
      </span>
    )
  },
  {
    id: "status",
    label: "Status",
    render: (row) => (
      <span className="block text-xs leading-5">
        <Badge variant={readinessToneToBadgeVariant(row.statusTone)}>{row.statusLabel}</Badge>
        <span className="mt-1 block text-muted-foreground">{row.breakCountLabel}</span>
      </span>
    )
  },
  {
    id: "evidence",
    label: "Evidence",
    render: (row) => (
      <span className="block text-xs leading-5">
        <span className="block text-muted-foreground">{row.evidenceLabel}</span>
        {row.evidenceHref ? (
          <Link to={row.evidenceHref} aria-label={`Open retained evidence for reconciliation lane ${row.label}`}>
            {row.evidenceRouteLabel}
          </Link>
        ) : (
          <span className="block text-muted-foreground">{row.evidenceRouteLabel}</span>
        )}
      </span>
    )
  },
  {
    id: "actions",
    label: "Actions",
    render: (row) => (
      <span className="block text-xs leading-5">
        <span className="block text-foreground">{row.requiredActionsLabel}</span>
        {row.routeHref ? (
          <Link className="link-subtle mt-1 inline-flex" to={row.routeHref} aria-label={`Open reconciliation lane: ${row.label}`}>
            {row.routeLabel}
          </Link>
        ) : (
          <span className="mt-1 block text-muted-foreground">{row.routeLabel}</span>
        )}
      </span>
    )
  }
];

const closeCockpitLaneColumns: DenseDataTableColumn<OperationsContinuityCloseCockpitLaneRow>[] = [
  {
    id: "lane",
    label: "Lane",
    render: (row) => (
      <span className="block min-w-0">
        <span className="block font-semibold text-foreground">{row.label}</span>
        <span className="mt-1 block text-xs leading-5 text-muted-foreground">{row.summary}</span>
      </span>
    )
  },
  {
    id: "status",
    label: "Status",
    render: (row) => <Badge variant={readinessToneToBadgeVariant(row.statusTone)}>{row.statusLabel}</Badge>
  },
  {
    id: "evidence",
    label: "Evidence",
    render: (row) => (
      <span className="block text-xs leading-5">
        <span className="block text-muted-foreground">{row.evidenceLabel}</span>
        {row.routeHref ? (
          <Link to={row.routeHref} aria-label={`Open private-capital close cockpit lane: ${row.label}`}>
            {row.routeLabel}
          </Link>
        ) : (
          <span className="block text-muted-foreground">{row.routeLabel}</span>
        )}
      </span>
    )
  },
  {
    id: "actions",
    label: "Required actions",
    render: (row) => <span className="text-xs leading-5 text-muted-foreground">{row.requiredActionsLabel}</span>
  }
];

const closeCockpitWorkflowColumns: DenseDataTableColumn<OperationsContinuityCloseCockpitWorkflowRow>[] = [
  {
    id: "workflow",
    label: "Workflow",
    render: (row) => (
      <span className="block min-w-0">
        {row.workflowHref ? (
          <Link to={row.workflowHref} aria-label={`Open private-capital close workflow ${row.periodLabel}`}>
            {row.periodLabel}
          </Link>
        ) : (
          <span className="block font-semibold text-foreground">{row.periodLabel}</span>
        )}
        <span className="mt-1 block font-mono text-[11px] text-muted-foreground">{row.id}</span>
      </span>
    )
  },
  {
    id: "readiness",
    label: "Readiness",
    render: (row) => (
      <span className="block text-xs leading-5">
        <Badge variant={readinessToneToBadgeVariant(row.readinessTone)}>{row.readinessLabel}</Badge>
        <span className="mt-1 block text-muted-foreground">{row.statusLabel}</span>
      </span>
    )
  },
  {
    id: "controls",
    label: "Controls",
    render: (row) => (
      <span className="block text-xs leading-5">
        <span className="block text-foreground">{row.blockersLabel}</span>
        <span className="block text-muted-foreground">{row.checklistLabel}</span>
      </span>
    )
  },
  {
    id: "package",
    label: "Close package",
    render: (row) => row.packageHref ? (
      <Link to={row.packageHref} aria-label={`Open private-capital close package for ${row.periodLabel}`}>
        {row.packageLabel}
      </Link>
    ) : (
      <span className="text-xs text-muted-foreground">{row.packageLabel}</span>
    )
  }
];

const closeCockpitApprovalColumns: DenseDataTableColumn<OperationsContinuityCloseCockpitApprovalRow>[] = [
  {
    id: "approval",
    label: "Approval",
    render: (row) => (
      <span className="block min-w-0">
        <span className="block font-semibold text-foreground">{row.workflowLabel}</span>
        <span className="mt-1 block font-mono text-[11px] text-muted-foreground">{row.id}</span>
      </span>
    )
  },
  {
    id: "status",
    label: "Status",
    render: (row) => (
      <span className="block text-xs leading-5">
        <Badge variant={readinessToneToBadgeVariant(row.statusTone)}>{row.statusLabel}</Badge>
        <span className="mt-1 block text-muted-foreground">{row.decidedLabel}</span>
      </span>
    )
  },
  {
    id: "actor",
    label: "Actor",
    render: (row) => (
      <span className="block text-xs leading-5">
        <span className="block text-foreground">{row.actorLabel}</span>
        <span className="block text-muted-foreground">{row.rationale}</span>
      </span>
    )
  },
  {
    id: "evidence",
    label: "Evidence",
    render: (row) => (
      <span className="block text-xs leading-5">
        <span className="block text-muted-foreground">{row.evidenceLabel}</span>
        {row.evidenceHref ? (
          <Link to={row.evidenceHref} aria-label={`Open private-capital close approval evidence ${row.id}`}>
            {row.evidenceRouteLabel}
          </Link>
        ) : (
          <span className="block text-muted-foreground">{row.evidenceRouteLabel}</span>
        )}
        {row.workflowHref ? (
          <Link className="block" to={row.workflowHref} aria-label={`Open private-capital close approval workflow ${row.workflowLabel}`}>
            {row.routeLabel}
          </Link>
        ) : (
          <span className="block text-muted-foreground">{row.routeLabel}</span>
        )}
      </span>
    )
  }
];

const navSupportPackageColumns: DenseDataTableColumn<OperationsContinuityNavSupportPackageRow>[] = [
  {
    id: "package",
    label: "Package",
    render: (row) => (
      <span className="block min-w-0">
        <span className="block font-semibold text-foreground">{row.label}</span>
        <span className="mt-1 block text-xs leading-5 text-muted-foreground">{row.summary}</span>
      </span>
    )
  },
  {
    id: "status",
    label: "Status",
    render: (row) => (
      <span className="block text-xs leading-5">
        <Badge variant={readinessToneToBadgeVariant(row.statusTone)}>{row.statusLabel}</Badge>
        <span className="mt-1 block text-muted-foreground">{row.componentSummaryLabel}</span>
      </span>
    )
  },
  {
    id: "shadow-nav",
    label: "Shadow NAV",
    render: (row) => (
      <span className="block text-xs leading-5">
        <span className="block text-foreground">{row.shadowNavLabel}</span>
        <span className="block text-muted-foreground">{row.evidenceLabel}</span>
      </span>
    )
  },
  {
    id: "actions",
    label: "Actions",
    render: (row) => (
      <span className="block text-xs leading-5">
        <span className="block text-muted-foreground">{row.requiredActionsLabel}</span>
        {row.routeHref ? (
          <Link to={row.routeHref} aria-label={`Open NAV support package ${row.label}`}>
            {row.routeLabel}
          </Link>
        ) : (
          <span className="block text-muted-foreground">{row.routeLabel}</span>
        )}
      </span>
    )
  }
];

const accountingRecordColumns: DenseDataTableColumn<OperationsAccountingRecordEvidenceRow>[] = [
  {
    id: "category",
    label: "Evidence category",
    render: (row) => (
      <span className="block min-w-0">
        <span className="block font-semibold text-foreground">{row.label}</span>
        <span className="mt-1 block text-xs leading-5 text-muted-foreground">{row.detail}</span>
      </span>
    )
  },
  {
    id: "status",
    label: "Status",
    render: (row) => <Badge variant={readinessToneToBadgeVariant(row.statusTone)}>{row.statusLabel}</Badge>
  },
  {
    id: "evidence",
    label: "Evidence",
    render: (row) => (
      <span className="block min-w-0 text-xs leading-5 text-muted-foreground">
        <span className="block">{row.evidenceLabel}</span>
        <span className="mt-1 block">Requires {row.requiredEvidenceLabel}</span>
      </span>
    )
  },
  {
    id: "source",
    label: "Source",
    render: (row) => row.routeHref ? (
      <Link to={row.routeHref} aria-label={`Open accounting-record evidence source: ${row.label}`}>
        {row.routeLabel}
      </Link>
    ) : (
      <span className="text-xs text-muted-foreground">{row.routeLabel}</span>
    )
  }
];

const timelineColumns: DenseDataTableColumn<OperationsContinuityTimelineRow>[] = [
  {
    id: "event",
    label: "Event",
    render: (row) => (
      <span className="block min-w-0">
        <span className="block font-semibold text-foreground">{row.title}</span>
        <span className="mt-1 block text-xs leading-5 text-muted-foreground">{row.detail}</span>
      </span>
    )
  },
  {
    id: "actor",
    label: "Actor",
    render: (row) => <span className="font-mono text-xs text-foreground/80">{row.actorLabel}</span>
  },
  {
    id: "state",
    label: "State",
    render: (row) => (
      <span className="block text-xs leading-5">
        <span className="block text-foreground">{row.stateLabel}</span>
        <span className="block font-mono text-muted-foreground">{row.hashLabel}</span>
      </span>
    )
  },
  {
    id: "time",
    label: "Time",
    render: (row) => <span className="font-mono text-xs text-muted-foreground">{row.timestampLabel}</span>
  }
];

export function OperationsContinuityScreen() {
  const vm = useOperationsContinuityScreenViewModel();
  const [breakCommand, setBreakCommand] = useState<{
    pending: { breakId: string; kind: BreakCommandKind } | null;
    message: string | null;
    error: string | null;
  }>({ pending: null, message: null, error: null });
  const [acknowledgementCommand, setAcknowledgementCommand] = useState<{
    pendingTaskId: string | null;
    message: string | null;
    error: string | null;
  }>({ pendingTaskId: null, message: null, error: null });
  const [approvalSubmitCommand, setApprovalSubmitCommand] = useState<{
    pendingStageId: string | null;
    message: string | null;
    error: string | null;
  }>({ pendingStageId: null, message: null, error: null });
  const [approvalDecisionCommand, setApprovalDecisionCommand] = useState<{
    pending: { approvalId: string; kind: ApprovalDecisionKind } | null;
    message: string | null;
    error: string | null;
  }>({ pending: null, message: null, error: null });
  const [closeWorkflowCommand, setCloseWorkflowCommand] = useState<{
    pendingStageId: string | null;
    message: string | null;
    error: string | null;
  }>({ pendingStageId: null, message: null, error: null });
  const [reopenWorkflowCommand, setReopenWorkflowCommand] = useState<{
    pending: boolean;
    message: string | null;
    error: string | null;
  }>({ pending: false, message: null, error: null });
  const [reopenWorkflowForm, setReopenWorkflowForm] = useState<ReopenWorkflowFormState>({
    incidentId: "",
    justification: "",
    approvalReference: "",
    impactSummary: ""
  });

  const runBreakCommand = useCallback(async (row: OperationsContinuityBreakCaseRow, kind: BreakCommandKind) => {
    const disabledReason = kind === "assign" ? row.assignDisabledReason : row.resolveDisabledReason;
    if (!row.workflowId || row.expectedWorkflowVersion === null || disabledReason !== null) {
      setBreakCommand({
        pending: null,
        message: null,
        error: disabledReason ?? "Break command is unavailable."
      });
      return;
    }

    setBreakCommand({ pending: { breakId: row.id, kind }, message: null, error: null });

    try {
      const result = kind === "assign"
        ? await assignOperationsContinuityBreakCase(row.workflowId, row.id, {
            expectedVersion: row.expectedWorkflowVersion,
            actor: "browser-operator",
            owner: row.assignmentOwner,
            rationale: `Assigned break ${row.caseLabel} from Operations Continuity exception management.`,
            escalationLevel: row.assignmentEscalationLevel,
            escalationReason: row.assignmentEscalationReason,
            dueDate: row.assignmentDueDate,
            correlationId: `browser-break-assign:${row.id}`,
            actionOrigin: "HumanOperator"
          })
        : await resolveOperationsContinuityBreakCase(row.workflowId, row.id, {
            expectedVersion: row.expectedWorkflowVersion,
            actor: "browser-operator",
            resolutionStatus: row.resolutionStatus,
            rationale: `Resolved break ${row.caseLabel} after retained evidence review.`,
            correlationId: `browser-break-resolve:${row.id}`,
            evidenceLinks: row.resolutionEvidenceLinks,
            actionOrigin: "HumanOperator"
          });

      setBreakCommand({
        pending: null,
        message: result.message?.trim() || (kind === "assign"
          ? "Break assignment retained through shared operations command."
          : "Break resolution retained through shared operations command."),
        error: null
      });
      await vm.refresh();
    } catch (err) {
      setBreakCommand({
        pending: null,
        message: null,
        error: formatBreakCommandError(err)
      });
    }
  }, [vm.refresh]);

  const acknowledgeChecklistTask = useCallback(async (row: OperationsContinuityChecklistRow) => {
    if (!row.workflowId || row.expectedWorkflowVersion === null || !row.canAcknowledge) {
      setAcknowledgementCommand({
        pendingTaskId: null,
        message: null,
        error: row.acknowledgeDisabledReason ?? "Checklist acknowledgement command is unavailable."
      });
      return;
    }

    setAcknowledgementCommand({ pendingTaskId: row.id, message: null, error: null });

    try {
      const result = await acknowledgeOperationsContinuityChecklistTask(row.workflowId, row.id, {
        expectedVersion: row.expectedWorkflowVersion,
        actor: "browser-operator",
        rationale: `Acknowledged close checklist task ${row.label} after retained evidence review.`,
        correlationId: `browser-checklist:${row.id}`
      });

      setAcknowledgementCommand({
        pendingTaskId: null,
        message: result.message?.trim() || "Checklist acknowledgement retained through shared operations command.",
        error: null
      });
      await vm.refresh();
    } catch (err) {
      setAcknowledgementCommand({
        pendingTaskId: null,
        message: null,
        error: formatChecklistCommandError(err)
      });
    }
  }, [vm.refresh]);

  const checklistColumns = useMemo(
    () => buildChecklistColumns({
      pendingTaskId: acknowledgementCommand.pendingTaskId,
      onAcknowledge: (row) => { void acknowledgeChecklistTask(row); }
    }),
    [acknowledgementCommand.pendingTaskId, acknowledgeChecklistTask]
  );

  const breakCaseColumns = useMemo(
    () => buildBreakCaseColumns({
      pendingBreakCommand: breakCommand.pending,
      onRunCommand: (row, kind) => { void runBreakCommand(row, kind); }
    }),
    [breakCommand.pending, runBreakCommand]
  );

  const submitApprovalCommand = useCallback(async (row: FinancialOperationsCommandSpineRow) => {
    if (
      !row.workflowId ||
      row.expectedWorkflowVersion === null ||
      !row.submitApprovalReportPackId ||
      !row.submitApprovalReviewer ||
      row.submitApprovalDisabledReason !== null
    ) {
      setApprovalSubmitCommand({
        pendingStageId: null,
        message: null,
        error: row.submitApprovalDisabledReason ?? "Approval submission command is unavailable."
      });
      return;
    }

    setApprovalSubmitCommand({ pendingStageId: row.id, message: null, error: null });

    try {
      const result = await submitOperationsContinuityApproval(row.workflowId, {
        expectedVersion: row.expectedWorkflowVersion,
        actor: "browser-operator",
        reviewer: row.submitApprovalReviewer,
        rationale: `Submitted ${row.stageLabel} approval from Operations Continuity command spine.`,
        reportPackId: row.submitApprovalReportPackId,
        correlationId: `browser-approval-submit:${row.id}`,
        evidenceLinks: row.submitApprovalEvidenceLinks,
        checklistControlApprovals: row.submitApprovalChecklistControlApprovals,
        actionOrigin: "HumanOperator"
      });

      setApprovalSubmitCommand({
        pendingStageId: null,
        message: result.message?.trim() || "Workflow approval submitted through shared operations command.",
        error: null
      });
      await vm.refresh();
    } catch (err) {
      setApprovalSubmitCommand({
        pendingStageId: null,
        message: null,
        error: formatApprovalSubmitCommandError(err)
      });
    }
  }, [vm.refresh]);

  const runApprovalDecisionCommand = useCallback(async (row: OperationsContinuityWorkflowApprovalRow, kind: ApprovalDecisionKind) => {
    const disabledReason = kind === "approve" ? row.approveWorkflowDisabledReason : row.rejectWorkflowDisabledReason;
    if (
      !row.workflowId ||
      row.expectedWorkflowVersion === null ||
      !row.reviewer ||
      disabledReason !== null
    ) {
      setApprovalDecisionCommand({
        pending: null,
        message: null,
        error: disabledReason ?? "Approval decision command is unavailable."
      });
      return;
    }

    if (kind === "approve" && !row.approveWorkflowReportPackId) {
      setApprovalDecisionCommand({
        pending: null,
        message: null,
        error: "Ready report-pack metadata is required before approval decision."
      });
      return;
    }

    setApprovalDecisionCommand({ pending: { approvalId: row.id, kind }, message: null, error: null });

    try {
      const result = kind === "approve"
        ? await approveOperationsContinuityWorkflow(row.workflowId, {
            expectedVersion: row.expectedWorkflowVersion,
            actor: "browser-operator",
            reviewer: row.reviewer,
            rationale: `Approved workflow approval ${row.id} from Operations Continuity approval history.`,
            reportPackId: row.approveWorkflowReportPackId!, // guarded above: approve returns early when absent
            correlationId: `browser-approval-decision:approve:${row.id}`,
            evidenceLinks: row.approveWorkflowEvidenceLinks,
            checklistControlApprovals: row.approveWorkflowChecklistControlApprovals,
            actionOrigin: "HumanOperator"
          })
        : await rejectOperationsContinuityWorkflow(row.workflowId, {
            expectedVersion: row.expectedWorkflowVersion,
            actor: "browser-operator",
            reviewer: row.reviewer,
            rationale: `Rejected workflow approval ${row.id} from Operations Continuity approval history.`,
            reasonCode: row.rejectWorkflowReasonCode,
            correlationId: `browser-approval-decision:reject:${row.id}`,
            evidenceLinks: row.rejectWorkflowEvidenceLinks,
            actionOrigin: "HumanOperator"
          });

      setApprovalDecisionCommand({
        pending: null,
        message: result.message?.trim() || (kind === "approve"
          ? "Workflow approval decision retained through shared operations command."
          : "Workflow approval rejection retained through shared operations command."),
        error: null
      });
      await vm.refresh();
    } catch (err) {
      setApprovalDecisionCommand({
        pending: null,
        message: null,
        error: formatApprovalDecisionCommandError(err)
      });
    }
  }, [vm.refresh]);

  const closeWorkflowCommandHandler = useCallback(async (row: FinancialOperationsCommandSpineRow) => {
    if (
      !row.workflowId ||
      row.expectedWorkflowVersion === null ||
      !row.closeWorkflowReportPackId ||
      row.closeWorkflowDisabledReason !== null
    ) {
      setCloseWorkflowCommand({
        pendingStageId: null,
        message: null,
        error: row.closeWorkflowDisabledReason ?? "Close package publication command is unavailable."
      });
      return;
    }

    setCloseWorkflowCommand({ pendingStageId: row.id, message: null, error: null });

    try {
      const result = await closeOperationsContinuityWorkflow(row.workflowId, {
        expectedVersion: row.expectedWorkflowVersion,
        actor: "browser-operator",
        rationale: `Published ${row.stageLabel} close package from Operations Continuity command spine.`,
        reportPackId: row.closeWorkflowReportPackId,
        correlationId: `browser-close-package:${row.id}`,
        evidenceLinks: row.closeWorkflowEvidenceLinks,
        checklistControlApprovals: row.closeWorkflowChecklistControlApprovals,
        actionOrigin: "HumanOperator"
      });

      setCloseWorkflowCommand({
        pendingStageId: null,
        message: result.message?.trim() || "Close package publication retained through shared operations command.",
        error: null
      });
      await vm.refresh();
    } catch (err) {
      setCloseWorkflowCommand({
        pendingStageId: null,
        message: null,
        error: formatCloseWorkflowCommandError(err)
      });
    }
  }, [vm.refresh]);

  const reopenWorkflowCommandHandler = useCallback(async () => {
    const formDisabledReason = buildReopenWorkflowFormDisabledReason(reopenWorkflowForm);
    const disabledReason = vm.closeGovernance.reopenWorkflowDisabledReason ?? formDisabledReason;
    if (
      !vm.closeGovernance.workflowId ||
      vm.closeGovernance.expectedWorkflowVersion === null ||
      disabledReason !== null
    ) {
      setReopenWorkflowCommand({
        pending: false,
        message: null,
        error: disabledReason ?? "Governed reopen command is unavailable."
      });
      return;
    }

    const incidentId = reopenWorkflowForm.incidentId.trim();
    const approvalReference = reopenWorkflowForm.approvalReference.trim();
    setReopenWorkflowCommand({ pending: true, message: null, error: null });

    try {
      const result = await reopenOperationsContinuityWorkflow(vm.closeGovernance.workflowId, {
        expectedVersion: vm.closeGovernance.expectedWorkflowVersion,
        actor: "browser-admin",
        rationale: `Governed reopen requested from Operations Continuity close governance for ${vm.closeGovernance.lockLabel}.`,
        incidentId,
        isGovernedAdmin: true,
        justification: reopenWorkflowForm.justification.trim(),
        approvalReference,
        impactSummary: reopenWorkflowForm.impactSummary.trim(),
        correlationId: `browser-governed-reopen:${incidentId}`,
        evidenceLinks: vm.closeGovernance.reopenWorkflowEvidenceLinks,
        actionOrigin: "HumanOperator"
      });

      setReopenWorkflowCommand({
        pending: false,
        message: result.message?.trim() || "Governed reopen retained through shared operations command.",
        error: null
      });
      await vm.refresh();
    } catch (err) {
      setReopenWorkflowCommand({
        pending: false,
        message: null,
        error: formatReopenWorkflowCommandError(err)
      });
    }
  }, [reopenWorkflowForm, vm.closeGovernance, vm.refresh]);

  const reopenWorkflowDisabledReason = reopenWorkflowCommand.pending
    ? "Wait for the active governed reopen command to finish."
    : vm.closeGovernance.reopenWorkflowDisabledReason ?? buildReopenWorkflowFormDisabledReason(reopenWorkflowForm);

  const financialOperationsCommandSpineColumns = useMemo(
    () => buildFinancialOperationsCommandSpineColumns({
      pendingStageId: approvalSubmitCommand.pendingStageId ?? closeWorkflowCommand.pendingStageId,
      onSubmitApproval: (row) => { void submitApprovalCommand(row); },
      onCloseWorkflow: (row) => { void closeWorkflowCommandHandler(row); }
    }),
    [
      approvalSubmitCommand.pendingStageId,
      closeWorkflowCommand.pendingStageId,
      submitApprovalCommand,
      closeWorkflowCommandHandler
    ]
  );

  const workflowApprovalHistoryDecisionColumns = useMemo(
    () => buildWorkflowApprovalHistoryColumns({
      pendingDecision: approvalDecisionCommand.pending,
      onRunDecision: (row, kind) => { void runApprovalDecisionCommand(row, kind); }
    }),
    [approvalDecisionCommand.pending, runApprovalDecisionCommand]
  );

  return (
    <div className="space-y-6">
      <span className="sr-only" aria-live="polite">{vm.statusAnnouncement}</span>

      <section
        role="region"
        aria-label="Operations continuity control strip"
        className="panel-surface-strong flex flex-wrap items-start justify-between gap-4 px-4 py-4"
      >
        <div className="min-w-0">
          <div className="eyebrow-label">Accounting operations</div>
          <h2 className="mt-2 font-display text-[1.375rem] font-semibold leading-tight text-foreground">
            {vm.title}
          </h2>
          <p className="mt-1 max-w-3xl text-sm leading-6 text-muted-foreground">{vm.subtitle}</p>
          {vm.errorText ? (
            <p role="alert" className="mt-3 inline-flex items-center gap-2 rounded-md border border-danger/40 bg-danger/10 px-3 py-2 text-sm text-danger">
              <AlertTriangle className="h-4 w-4" aria-hidden="true" />
              {vm.errorText}
            </p>
          ) : null}
        </div>
        <div className="flex flex-wrap items-center justify-end gap-2">
          <Badge variant="outline">{vm.workflowsSummaryLabel}</Badge>
          <Button
            type="button"
            variant="outline"
            size="sm"
            onClick={() => void vm.refresh()}
            busy={vm.reloadBusy}
            busyLabel={vm.reloadLabel}
            disabled={vm.reloadDisabled}
            disabledReason={vm.reloadDisabledReason}
            aria-label={vm.reloadAriaLabel}
          >
            <RefreshCcw className="h-4 w-4" aria-hidden="true" />
            {vm.reloadLabel}
          </Button>
        </div>
      </section>

      <section className="grid gap-4 xl:grid-cols-[1fr_0.75fr]">
        <Card className="panel-surface">
          <CardHeader>
            <CardTitle className="flex items-center gap-2">
              <Workflow className="h-5 w-5 text-primary" aria-hidden="true" />
              Workflows
            </CardTitle>
            <CardDescription>Select a workflow to inspect close-lane gates, blockers, next action, and timeline.</CardDescription>
          </CardHeader>
          <CardContent>
            <DenseDataTable
              columns={workflowColumns}
              rows={vm.workflows}
              getRowId={(row) => row.id}
              getRowAriaLabel={(row) => row.ariaLabel}
              getRowSelectAriaLabel={(row) => `Open ${row.ariaLabel}`}
              getRowAriaControls={(row) => row.detailPanelId}
              getRowAriaExpanded={(row) => row.expanded}
              getRowClassName={(row) => row.rowClassName}
              onRowSelect={(row) => vm.selectWorkflow(row.id)}
              selectedRowId={vm.selectedWorkflowId}
              emptyText={vm.workflowsEmptyText}
              ariaLabel={vm.workflowsTableLabel}
              caption={vm.workflowsTableCaption}
            />
          </CardContent>
        </Card>

        <Card className={cn("border", readinessToneToPanelClass(vm.nextAction.statusTone))}>
          <CardHeader>
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <CardTitle className="flex items-center gap-2">
                  <ArrowRight className="h-5 w-5 text-primary" aria-hidden="true" />
                  Next action
                </CardTitle>
                <CardDescription>{vm.nextAction.detail}</CardDescription>
              </div>
              <Badge variant={readinessToneToBadgeVariant(vm.nextAction.statusTone)}>
                {vm.nextAction.disabled ? "Unavailable" : "Ready"}
              </Badge>
            </div>
          </CardHeader>
          <CardContent className="space-y-4">
            <div>
              <p className="font-semibold text-foreground">{vm.nextAction.title}</p>
              {vm.nextAction.disabledReason ? (
                <p className="mt-2 text-sm leading-6 text-muted-foreground">{vm.nextAction.disabledReason}</p>
              ) : null}
            </div>
            {vm.nextAction.href ? (
              <Button asChild disabled={vm.nextAction.disabled} disabledReason={vm.nextAction.disabledReason}>
                <Link to={vm.nextAction.href} aria-label={vm.nextAction.ariaLabel}>
                  {vm.nextAction.label}
                  <ArrowRight className="h-4 w-4" aria-hidden="true" />
                </Link>
              </Button>
            ) : (
              <Button type="button" disabled disabledReason={vm.nextAction.disabledReason}>
                {vm.nextAction.label}
              </Button>
            )}
          </CardContent>
        </Card>
      </section>

      {vm.selectedDetail ? (
        <section className="grid gap-4 lg:grid-cols-[0.8fr_1.2fr]">
          <div className={cn("rounded-lg border", readinessToneToPanelClass(vm.selectedDetail.statusTone))}>
            <EntitySummary
              id={vm.selectedDetail.id}
              eyebrow="Selected workflow"
              title={vm.selectedDetail.title}
              subtitle={vm.selectedDetail.subtitle}
              description={vm.selectedDetail.description}
              ariaLabel={vm.selectedDetail.ariaLabel}
              status={<Badge variant={readinessToneToBadgeVariant(vm.selectedDetail.statusTone)}>{vm.selectedDetail.statusLabel}</Badge>}
              fields={vm.selectedDetail.metadata}
            />
            <div className="px-4 pb-4">
              {vm.detailErrorText ? (
                <p role="alert" className="mt-3 inline-flex items-center gap-2 text-sm text-danger">
                  <AlertTriangle className="h-4 w-4" aria-hidden="true" />
                  {vm.detailErrorText}
                </p>
              ) : null}
            </div>
          </div>

          <Card className="panel-surface">
            <CardHeader>
              <CardTitle className="flex items-center gap-2">
                <ListChecks className="h-5 w-5 text-primary" aria-hidden="true" />
                {vm.gatesLabel}
              </CardTitle>
              <CardDescription>Server-derived checkpoints for the selected close workflow.</CardDescription>
            </CardHeader>
            <CardContent>
              <DenseDataTable
                columns={gateColumns}
                rows={vm.gates}
                getRowId={(row) => row.id}
                getRowAriaLabel={(row) => row.ariaLabel}
                emptyText={vm.gatesEmptyText}
                ariaLabel="Operations continuity gates"
              />
            </CardContent>
          </Card>
        </section>
      ) : null}

      <section className="grid gap-4">
        <Card className={cn("border", readinessToneToPanelClass(vm.dashboard.statusTone))}>
          <CardHeader>
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <CardTitle className="flex items-center gap-2">
                  <Gauge className="h-5 w-5 text-primary" aria-hidden="true" />
                  {vm.dashboard.title}
                </CardTitle>
                <CardDescription>{vm.dashboard.summaryLabel}</CardDescription>
              </div>
              <Badge variant={readinessToneToBadgeVariant(vm.dashboard.statusTone)}>
                {vm.dashboard.statusLabel}
              </Badge>
            </div>
            <div
              role="list"
              aria-label="Financial Operations dashboard summary"
              className="grid gap-2 pt-3 text-xs text-muted-foreground sm:grid-cols-2 xl:grid-cols-4"
            >
              <span role="listitem">{vm.dashboard.stageLabel}</span>
              <span role="listitem">{vm.dashboard.metricCountLabel}</span>
              <span role="listitem">{vm.dashboard.evidenceLabel}</span>
              <span role="listitem">{vm.dashboard.requiredActionsLabel}</span>
            </div>
          </CardHeader>
          <CardContent>
            <DenseDataTable
              columns={operationalDashboardColumns}
              rows={vm.dashboard.metrics}
              getRowId={(row) => row.id}
              getRowAriaLabel={(row) => row.ariaLabel}
              emptyText={vm.dashboard.metricsEmptyText}
              ariaLabel="Financial Operations operational dashboard"
            />
          </CardContent>
        </Card>
        <Card className={cn("border", readinessToneToPanelClass(vm.closeCalendar.statusTone))}>
          <CardHeader>
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <CardTitle className="flex items-center gap-2">
                  <CalendarDays className="h-5 w-5 text-primary" aria-hidden="true" />
                  {vm.closeCalendar.title}
                </CardTitle>
                <CardDescription>{vm.closeCalendar.summaryLabel}</CardDescription>
              </div>
              <Badge variant={readinessToneToBadgeVariant(vm.closeCalendar.statusTone)}>
                {vm.closeCalendar.statusLabel}
              </Badge>
            </div>
            <div
              role="list"
              aria-label="Operations close calendar summary"
              className="grid gap-2 pt-3 text-xs text-muted-foreground sm:grid-cols-2 xl:grid-cols-4"
            >
              <span role="listitem">{vm.closeCalendar.scopeLabel}</span>
              <span role="listitem">{vm.closeCalendar.itemCountLabel}</span>
              <span role="listitem">{vm.closeCalendar.nextDueLabel}</span>
              <span role="listitem">{vm.closeCalendar.blockerCountLabel}</span>
              <span role="listitem">{vm.closeCalendar.checklistCountLabel}</span>
              <span role="listitem">{vm.closeCalendar.approvalCountLabel}</span>
              <span role="listitem">{vm.closeCalendar.freshnessLabel}</span>
            </div>
            {vm.closeCalendar.errorText ? (
              <p role="alert" className="mt-3 inline-flex items-center gap-2 text-sm text-danger">
                <AlertTriangle className="h-4 w-4" aria-hidden="true" />
                {vm.closeCalendar.errorText}
              </p>
            ) : null}
          </CardHeader>
          <CardContent>
            <DenseDataTable
              columns={closeCalendarColumns}
              rows={vm.closeCalendar.rows}
              getRowId={(row) => row.id}
              getRowAriaLabel={(row) => row.ariaLabel}
              emptyText={vm.closeCalendar.emptyText}
              ariaLabel="Operations continuity close calendar"
            />
          </CardContent>
        </Card>
        <Card className={cn("border", readinessToneToPanelClass(vm.commandSpine.statusTone))}>
          <CardHeader>
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <CardTitle className="flex items-center gap-2">
                  <GitBranch className="h-5 w-5 text-primary" aria-hidden="true" />
                  {vm.commandSpine.title}
                </CardTitle>
                <CardDescription>{vm.commandSpine.summaryLabel}</CardDescription>
              </div>
              <Badge variant={readinessToneToBadgeVariant(vm.commandSpine.statusTone)}>
                {vm.commandSpine.statusLabel}
              </Badge>
            </div>
          </CardHeader>
          <CardContent className="space-y-3">
            {approvalSubmitCommand.error ? (
              <p role="alert" className="rounded-md border border-danger/30 bg-danger/10 px-3 py-2 text-sm text-danger">
                {approvalSubmitCommand.error}
              </p>
            ) : null}
            {approvalSubmitCommand.message ? (
              <p role="status" className="rounded-md border border-success/30 bg-success/10 px-3 py-2 text-sm text-success">
                {approvalSubmitCommand.message}
              </p>
            ) : null}
            {closeWorkflowCommand.error ? (
              <p role="alert" className="rounded-md border border-danger/30 bg-danger/10 px-3 py-2 text-sm text-danger">
                {closeWorkflowCommand.error}
              </p>
            ) : null}
            {closeWorkflowCommand.message ? (
              <p role="status" className="rounded-md border border-success/30 bg-success/10 px-3 py-2 text-sm text-success">
                {closeWorkflowCommand.message}
              </p>
            ) : null}
            <DenseDataTable
              columns={financialOperationsCommandSpineColumns}
              rows={vm.commandSpine.rows}
              getRowId={(row) => row.id}
              getRowAriaLabel={(row) => row.ariaLabel}
              emptyText={vm.commandSpine.emptyText}
              ariaLabel="Financial Operations command spine"
            />
          </CardContent>
        </Card>
        <Card className="panel-surface">
          <CardHeader>
            <CardTitle className="flex items-center gap-2">
              <ListChecks className="h-5 w-5 text-primary" aria-hidden="true" />
              {vm.workflowApprovalHistoryLabel}
            </CardTitle>
            <CardDescription>Source-owned approval submissions, reviewer decisions, and retained approval evidence for the selected workflow.</CardDescription>
          </CardHeader>
          <CardContent className="space-y-3">
            {approvalDecisionCommand.error ? (
              <p role="alert" className="rounded-md border border-danger/30 bg-danger/10 px-3 py-2 text-sm text-danger">
                {approvalDecisionCommand.error}
              </p>
            ) : null}
            {approvalDecisionCommand.message ? (
              <p role="status" className="rounded-md border border-success/30 bg-success/10 px-3 py-2 text-sm text-success">
                {approvalDecisionCommand.message}
              </p>
            ) : null}
            <DenseDataTable
              columns={workflowApprovalHistoryDecisionColumns}
              rows={vm.workflowApprovalHistory}
              getRowId={(row) => row.id}
              getRowAriaLabel={(row) => row.ariaLabel}
              emptyText={vm.workflowApprovalHistoryEmptyText}
              ariaLabel="Operations continuity workflow approval history"
            />
          </CardContent>
        </Card>
      </section>

      <section className="grid gap-4">
        <Card className={cn("border", readinessToneToPanelClass(vm.reviewedAutomation.statusTone))}>
          <CardHeader>
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <CardTitle className="flex items-center gap-2">
                  <Workflow className="h-5 w-5 text-primary" aria-hidden="true" />
                  {vm.reviewedAutomation.title}
                </CardTitle>
                <CardDescription>{vm.reviewedAutomation.summaryLabel}</CardDescription>
              </div>
              <Badge variant={readinessToneToBadgeVariant(vm.reviewedAutomation.statusTone)}>
                {vm.reviewedAutomation.statusLabel}
              </Badge>
            </div>
            <div
              role="list"
              aria-label="Reviewed automation summary"
              className="grid gap-2 pt-3 text-xs text-muted-foreground sm:grid-cols-2 xl:grid-cols-3"
            >
              <span role="listitem">{vm.reviewedAutomation.stageLabel}</span>
              <span role="listitem">{vm.reviewedAutomation.reviewLabel}</span>
              <span role="listitem">{vm.reviewedAutomation.evidenceLabel}</span>
              <span role="listitem">Allowed: {vm.reviewedAutomation.allowedUseCasesLabel}</span>
              <span role="listitem">Prohibited: {vm.reviewedAutomation.prohibitedActionsLabel}</span>
              <span role="listitem">{vm.reviewedAutomation.requiredActionsLabel}</span>
            </div>
          </CardHeader>
          <CardContent>
            <DenseDataTable
              columns={reviewedAutomationArtifactColumns}
              rows={vm.reviewedAutomation.artifacts}
              getRowId={(row) => row.id}
              getRowAriaLabel={(row) => row.ariaLabel}
              emptyText={vm.reviewedAutomation.artifactsEmptyText}
              ariaLabel="Reviewed automation output queue"
            />
          </CardContent>
        </Card>
      </section>

      <section className="grid gap-4">
        <Card className={cn("border", readinessToneToPanelClass(vm.financialOperationsQueue.statusTone))}>
          <CardHeader>
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <CardTitle className="flex items-center gap-2">
                  <ListChecks className="h-5 w-5 text-primary" aria-hidden="true" />
                  {vm.financialOperationsQueue.title}
                </CardTitle>
                <CardDescription>{vm.financialOperationsQueue.summaryLabel}</CardDescription>
              </div>
              <Badge variant={readinessToneToBadgeVariant(vm.financialOperationsQueue.statusTone)}>
                {vm.financialOperationsQueue.statusLabel}
              </Badge>
            </div>
          </CardHeader>
          <CardContent>
            <DenseDataTable
              columns={financialOperationsQueueColumns}
              rows={vm.financialOperationsQueue.items}
              getRowId={(row) => row.id}
              getRowAriaLabel={(row) => row.ariaLabel}
              emptyText={vm.financialOperationsQueue.emptyText}
              ariaLabel="Financial Operations operator queue"
            />
          </CardContent>
        </Card>
      </section>

      <section className="grid gap-4">
        <Card className="panel-surface">
          <CardHeader>
            <CardTitle className="flex items-center gap-2">
              <ListChecks className="h-5 w-5 text-primary" aria-hidden="true" />
              {vm.evidencePackagesLabel}
            </CardTitle>
            <CardDescription>Shared package posture for accounting records, report packs, close manifests, and audit support.</CardDescription>
          </CardHeader>
          <CardContent>
            <DenseDataTable
              columns={evidencePackageColumns}
              rows={vm.evidencePackages}
              getRowId={(row) => row.id}
              getRowAriaLabel={(row) => row.ariaLabel}
              emptyText={vm.evidencePackagesEmptyText}
              ariaLabel="Operations continuity evidence packages"
            />
          </CardContent>
        </Card>
      </section>

      <section className="grid gap-4">
        <Card className={cn("border", readinessToneToPanelClass(vm.closeCockpit.statusTone))}>
          <CardHeader>
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <CardTitle className="flex items-center gap-2">
                  <Gauge className="h-5 w-5 text-primary" aria-hidden="true" />
                  {vm.closeCockpit.title}
                </CardTitle>
                <CardDescription>{vm.closeCockpit.summaryLabel}</CardDescription>
              </div>
              <Badge variant={readinessToneToBadgeVariant(vm.closeCockpit.statusTone)}>
                {vm.closeCockpit.statusLabel}
              </Badge>
            </div>
            <div
              role="list"
              aria-label="Private-capital close cockpit summary"
              className="grid gap-2 pt-3 text-xs text-muted-foreground sm:grid-cols-2 xl:grid-cols-4"
            >
              <span role="listitem">{vm.closeCockpit.scopeLabel}</span>
              <span role="listitem">{vm.closeCockpit.readinessLabel}</span>
              <span role="listitem">{vm.closeCockpit.laneCountLabel}</span>
              <span role="listitem">{vm.closeCockpit.proofLaneSummaryLabel}</span>
              <span role="listitem">{vm.closeCockpit.workflowCountLabel}</span>
              <span role="listitem">{vm.closeCockpit.blockerCountLabel}</span>
              <span role="listitem">{vm.closeCockpit.fundEventCountLabel}</span>
              <span role="listitem">{vm.closeCockpit.reportOutputCountLabel}</span>
              <span role="listitem">{vm.closeCockpit.freshnessLabel}</span>
            </div>
            {vm.closeCockpit.errorText ? (
              <p role="alert" className="mt-3 inline-flex items-center gap-2 text-sm text-danger">
                <AlertTriangle className="h-4 w-4" aria-hidden="true" />
                {vm.closeCockpit.errorText}
              </p>
            ) : null}
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="flex flex-wrap items-center justify-between gap-3 rounded-md border border-border/70 bg-background/60 px-3 py-3">
              <div className="min-w-0 text-xs leading-5 text-muted-foreground">
                <p className="font-semibold text-foreground">{vm.closeCockpit.nextActionLabel}</p>
                {vm.closeCockpit.nextActionDisabledReason ? (
                  <p>{vm.closeCockpit.nextActionDisabledReason}</p>
                ) : null}
                <p className="mt-1">Live: {vm.closeCockpit.liveCapabilitiesLabel}</p>
                <p>Planned: {vm.closeCockpit.plannedCapabilitiesLabel}</p>
              </div>
              {vm.closeCockpit.nextActionHref ? (
                <Button asChild size="sm" disabled={vm.closeCockpit.nextActionDisabled} disabledReason={vm.closeCockpit.nextActionDisabledReason}>
                  <Link to={vm.closeCockpit.nextActionHref} aria-label={`Open private-capital close cockpit action: ${vm.closeCockpit.nextActionLabel}`}>
                    Open action
                    <ArrowRight className="h-4 w-4" aria-hidden="true" />
                  </Link>
                </Button>
              ) : (
                <Button type="button" size="sm" disabled disabledReason={vm.closeCockpit.nextActionDisabledReason}>
                  Open action
                </Button>
              )}
            </div>

            <div className="grid gap-4 xl:grid-cols-[1.05fr_0.95fr]">
              <div className="space-y-2">
                <h3 className="text-sm font-semibold text-foreground">Cockpit lanes</h3>
                <DenseDataTable
                  columns={closeCockpitLaneColumns}
                  rows={vm.closeCockpit.lanes}
                  getRowId={(row) => row.id}
                  getRowAriaLabel={(row) => row.ariaLabel}
                  emptyText={vm.closeCockpit.lanesEmptyText}
                  ariaLabel="Private-capital close cockpit lanes"
                />
              </div>
              <div className="space-y-2">
                <h3 className="text-sm font-semibold text-foreground">Scoped close workflows</h3>
                <DenseDataTable
                  columns={closeCockpitWorkflowColumns}
                  rows={vm.closeCockpit.workflows}
                  getRowId={(row) => row.id}
                  getRowAriaLabel={(row) => row.ariaLabel}
                  emptyText={vm.closeCockpit.workflowsEmptyText}
                  ariaLabel="Private-capital close cockpit workflows"
                />
              </div>
            </div>
            <div className="space-y-2">
              <h3 className="text-sm font-semibold text-foreground">Evidence packages</h3>
              <DenseDataTable
                columns={evidencePackageColumns}
                rows={vm.closeCockpit.evidencePackages}
                getRowId={(row) => row.id}
                getRowAriaLabel={(row) => row.ariaLabel}
                emptyText={vm.closeCockpit.evidencePackagesEmptyText}
                ariaLabel="Private-capital close cockpit evidence packages"
              />
            </div>
            <div className="space-y-2">
              <h3 className="text-sm font-semibold text-foreground">NAV support packages</h3>
              <DenseDataTable
                columns={navSupportPackageColumns}
                rows={vm.closeCockpit.navSupportPackages}
                getRowId={(row) => row.id}
                getRowAriaLabel={(row) => row.ariaLabel}
                emptyText={vm.closeCockpit.navSupportPackagesEmptyText}
                ariaLabel="Private-capital close cockpit NAV support packages"
              />
            </div>
            <div className="space-y-2">
              <h3 className="text-sm font-semibold text-foreground">Approval history</h3>
              <DenseDataTable
                columns={closeCockpitApprovalColumns}
                rows={vm.closeCockpit.approvalHistory}
                getRowId={(row) => row.id}
                getRowAriaLabel={(row) => row.ariaLabel}
                emptyText={vm.closeCockpit.approvalHistoryEmptyText}
                ariaLabel="Private-capital close cockpit approval history"
              />
            </div>
          </CardContent>
        </Card>
      </section>

      <section className="grid gap-4 xl:grid-cols-2">
        <Card className="panel-surface">
          <CardHeader>
            <CardTitle className="flex items-center gap-2">
              <AlertTriangle className="h-5 w-5 text-primary" aria-hidden="true" />
              {vm.blockersLabel}
            </CardTitle>
            <CardDescription>Open gate and workflow blockers that explain why the next action may be disabled or routed.</CardDescription>
          </CardHeader>
          <CardContent>
            <DenseDataTable
              columns={blockerColumns}
              rows={vm.blockers}
              getRowId={(row) => row.id}
              getRowAriaLabel={(row) => row.ariaLabel}
              emptyText={vm.blockersEmptyText}
              ariaLabel="Operations continuity blockers"
            />
          </CardContent>
        </Card>

        <Card className="panel-surface xl:col-span-2">
          <CardHeader>
            <CardTitle className="flex items-center gap-2">
              <ListChecks className="h-5 w-5 text-primary" aria-hidden="true" />
              {vm.reconciliationLanesLabel}
            </CardTitle>
            <CardDescription>Shared reconciliation coverage for cash, position, trade, income, MBS factor, bank, and GL support lanes.</CardDescription>
          </CardHeader>
          <CardContent>
            <DenseDataTable
              columns={reconciliationLaneColumns}
              rows={vm.reconciliationLanes}
              getRowId={(row) => row.id}
              getRowAriaLabel={(row) => row.ariaLabel}
              emptyText={vm.reconciliationLanesEmptyText}
              ariaLabel="Operations continuity reconciliation lane coverage"
            />
          </CardContent>
        </Card>

        <Card className="panel-surface">
          <CardHeader>
            <CardTitle className="flex items-center gap-2">
              <AlertTriangle className="h-5 w-5 text-primary" aria-hidden="true" />
              {vm.breakCasesLabel}
            </CardTitle>
            <CardDescription>Source-owned reconciliation breaks with retained owner, due-date, escalation, variance, and evidence posture.</CardDescription>
          </CardHeader>
          <CardContent className="space-y-3">
            {breakCommand.error ? (
              <p role="alert" className="rounded-md border border-danger/30 bg-danger/10 px-3 py-2 text-sm text-danger">
                {breakCommand.error}
              </p>
            ) : null}
            {breakCommand.message ? (
              <p role="status" className="rounded-md border border-success/30 bg-success/10 px-3 py-2 text-sm text-success">
                {breakCommand.message}
              </p>
            ) : null}
            <DenseDataTable
              columns={breakCaseColumns}
              rows={vm.breakCases}
              getRowId={(row) => row.id}
              getRowAriaLabel={(row) => row.ariaLabel}
              emptyText={vm.breakCasesEmptyText}
              ariaLabel="Operations continuity break assignment and escalation"
            />
          </CardContent>
        </Card>

        <Card className="panel-surface">
          <CardHeader>
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <CardTitle className="flex items-center gap-2">
                  <ListChecks className="h-5 w-5 text-primary" aria-hidden="true" />
                  {vm.checklistLabel}
                </CardTitle>
                <CardDescription>Shared close-control tasks required before approval and close transitions.</CardDescription>
              </div>
              <Badge variant={readinessToneToBadgeVariant(vm.checklistSummary.statusTone)}>
                {vm.checklistSummary.taskCountLabel}
              </Badge>
            </div>
            <div
              role="list"
              aria-label="Close checklist control summary"
              className="grid gap-2 pt-3 text-xs text-muted-foreground sm:grid-cols-2"
            >
              <span role="listitem">{vm.checklistSummary.readyCountLabel}</span>
              <span role="listitem">{vm.checklistSummary.blockedCountLabel}</span>
              <span role="listitem">{vm.checklistSummary.acknowledgementCountLabel}</span>
              <span role="listitem">{vm.checklistSummary.approvalCountLabel}</span>
              <span role="listitem">{vm.checklistSummary.evidenceCountLabel}</span>
              <span role="listitem">{vm.checklistSummary.dueSoonLabel}</span>
            </div>
          </CardHeader>
          <CardContent className="space-y-3">
            {acknowledgementCommand.error ? (
              <p role="alert" className="rounded-md border border-danger/30 bg-danger/10 px-3 py-2 text-sm text-danger">
                {acknowledgementCommand.error}
              </p>
            ) : null}
            {acknowledgementCommand.message ? (
              <p role="status" className="rounded-md border border-success/30 bg-success/10 px-3 py-2 text-sm text-success">
                {acknowledgementCommand.message}
              </p>
            ) : null}
            <DenseDataTable
              columns={checklistColumns}
              rows={vm.checklist}
              getRowId={(row) => row.id}
              getRowAriaLabel={(row) => row.ariaLabel}
              emptyText={vm.checklistEmptyText}
              ariaLabel="Operations continuity close checklist"
            />
          </CardContent>
        </Card>
      </section>

      <section className="grid gap-4">
        <Card className={cn("border", readinessToneToPanelClass(vm.accountingRecordSummary.statusTone))}>
          <CardHeader>
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <CardTitle className="flex items-center gap-2">
                  <ListChecks className="h-5 w-5 text-primary" aria-hidden="true" />
                  {vm.accountingRecordLabel}
                </CardTitle>
                <CardDescription>{vm.accountingRecordSummary.summaryLabel}</CardDescription>
              </div>
              <Badge variant={readinessToneToBadgeVariant(vm.accountingRecordSummary.statusTone)}>
                {vm.accountingRecordSummary.statusLabel}
              </Badge>
            </div>
            <div
              role="list"
              aria-label="Accounting record evidence summary"
              className="grid gap-2 pt-3 text-xs text-muted-foreground sm:grid-cols-2"
            >
              <span role="listitem">{vm.accountingRecordSummary.recordIdLabel}</span>
              <span role="listitem">{vm.accountingRecordSummary.evidenceLabel}</span>
              <span role="listitem">{vm.accountingRecordSummary.auditPackLabel}</span>
              <span role="listitem">{vm.accountingRecordSummary.auditPackTimingLabel}</span>
            </div>
          </CardHeader>
          <CardContent>
            <DenseDataTable
              columns={accountingRecordColumns}
              rows={vm.accountingRecordEvidence}
              getRowId={(row) => row.id}
              getRowAriaLabel={(row) => row.ariaLabel}
              emptyText={vm.accountingRecordEmptyText}
              ariaLabel="Operations continuity accounting record evidence"
            />
          </CardContent>
        </Card>

        <Card className={cn("border", readinessToneToPanelClass(vm.closeGovernance.statusTone))}>
          <CardHeader>
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <CardTitle className="flex items-center gap-2">
                  <Lock className="h-5 w-5 text-primary" aria-hidden="true" />
                  {vm.closeGovernance.title}
                </CardTitle>
                <CardDescription>{vm.closeGovernance.lockDetail}</CardDescription>
              </div>
              <Badge variant={readinessToneToBadgeVariant(vm.closeGovernance.statusTone)}>
                {vm.closeGovernance.statusLabel}
              </Badge>
            </div>
          </CardHeader>
          <CardContent className="space-y-4">
            <dl
              aria-label="Period close lock and reopen evidence"
              className="grid gap-3 text-xs leading-5 sm:grid-cols-2 lg:grid-cols-4"
            >
              <div>
                <dt className="eyebrow-label">Lock</dt>
                <dd className="text-foreground">{vm.closeGovernance.lockLabel}</dd>
              </div>
              <div>
                <dt className="eyebrow-label">Close audit</dt>
                <dd className="break-words font-mono text-foreground">{vm.closeGovernance.closeAuditLabel}</dd>
              </div>
              <div>
                <dt className="eyebrow-label">Close package</dt>
                <dd className="break-words font-mono text-foreground">{vm.closeGovernance.closePackageLabel}</dd>
              </div>
              <div>
                <dt className="eyebrow-label">Reopen audit</dt>
                <dd className="break-words text-foreground">{vm.closeGovernance.reopenAuditLabel}</dd>
              </div>
              <div>
                <dt className="eyebrow-label">Reopen incident</dt>
                <dd className="break-words font-mono text-foreground">{vm.closeGovernance.reopenIncidentLabel}</dd>
              </div>
              <div>
                <dt className="eyebrow-label">Reopen evidence</dt>
                <dd className="text-foreground">{vm.closeGovernance.reopenEvidenceLabel}</dd>
              </div>
              <div className="sm:col-span-2">
                <dt className="eyebrow-label">Reopen rationale</dt>
                <dd className="text-foreground">{vm.closeGovernance.reopenRationaleLabel}</dd>
              </div>
              <div className="sm:col-span-2 lg:col-span-4">
                <dt className="eyebrow-label">Command guard</dt>
                <dd className="text-foreground">{vm.closeGovernance.commandGuardLabel}</dd>
              </div>
            </dl>
            {reopenWorkflowCommand.error ? (
              <p role="alert" className="rounded-md border border-danger/30 bg-danger/10 px-3 py-2 text-sm text-danger">
                {reopenWorkflowCommand.error}
              </p>
            ) : null}
            {reopenWorkflowCommand.message ? (
              <p role="status" className="rounded-md border border-success/30 bg-success/10 px-3 py-2 text-sm text-success">
                {reopenWorkflowCommand.message}
              </p>
            ) : null}
            <form
              aria-label="Governed period reopen command"
              className="border-t border-border/60 pt-4"
              onSubmit={(event) => {
                event.preventDefault();
                void reopenWorkflowCommandHandler();
              }}
            >
              <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
                <div className="space-y-1.5">
                  <Label htmlFor="operations-reopen-incident-id">Incident id</Label>
                  <Input
                    id="operations-reopen-incident-id"
                    value={reopenWorkflowForm.incidentId}
                    onChange={(event) => setReopenWorkflowForm((current) => ({ ...current, incidentId: event.target.value }))}
                  />
                </div>
                <div className="space-y-1.5">
                  <Label htmlFor="operations-reopen-approval-reference">Approval reference</Label>
                  <Input
                    id="operations-reopen-approval-reference"
                    value={reopenWorkflowForm.approvalReference}
                    onChange={(event) => setReopenWorkflowForm((current) => ({ ...current, approvalReference: event.target.value }))}
                  />
                </div>
                <div className="space-y-1.5">
                  <Label htmlFor="operations-reopen-justification">Justification</Label>
                  <Input
                    id="operations-reopen-justification"
                    value={reopenWorkflowForm.justification}
                    onChange={(event) => setReopenWorkflowForm((current) => ({ ...current, justification: event.target.value }))}
                  />
                </div>
                <div className="space-y-1.5">
                  <Label htmlFor="operations-reopen-impact-summary">Impact summary</Label>
                  <Input
                    id="operations-reopen-impact-summary"
                    value={reopenWorkflowForm.impactSummary}
                    onChange={(event) => setReopenWorkflowForm((current) => ({ ...current, impactSummary: event.target.value }))}
                  />
                </div>
              </div>
              <div className="mt-3 flex justify-end">
                <Button
                  type="submit"
                  size="sm"
                  variant="outline"
                  busy={reopenWorkflowCommand.pending}
                  busyLabel="Reopening"
                  disabled={reopenWorkflowDisabledReason !== null}
                  disabledReason={reopenWorkflowDisabledReason}
                  aria-label={vm.closeGovernance.reopenWorkflowAriaLabel}
                >
                  {vm.closeGovernance.reopenWorkflowLabel}
                </Button>
              </div>
            </form>
          </CardContent>
        </Card>

        <Card className={cn("border", readinessToneToPanelClass(vm.closePackage.statusTone))}>
          <CardHeader>
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <CardTitle>{vm.closePackage.title}</CardTitle>
                <CardDescription>Governed close publication from the shared operations-continuity workflow.</CardDescription>
              </div>
              <Badge variant={readinessToneToBadgeVariant(vm.closePackage.statusTone)}>{vm.closePackage.statusLabel}</Badge>
            </div>
          </CardHeader>
          <CardContent>
            <dl
              aria-label="Close package publication summary"
              className="grid gap-3 text-xs leading-5 sm:grid-cols-2 lg:grid-cols-4"
            >
              <div>
                <dt className="eyebrow-label">Package</dt>
                <dd className="break-words font-mono text-foreground">{vm.closePackage.packageIdLabel}</dd>
              </div>
              <div>
                <dt className="eyebrow-label">Report pack</dt>
                <dd className="break-words text-foreground">{vm.closePackage.reportPackLabel}</dd>
              </div>
              <div>
                <dt className="eyebrow-label">Manifest</dt>
                <dd className="break-words font-mono text-foreground">
                  {vm.closePackage.manifestHref ? (
                    <Link to={vm.closePackage.manifestHref} aria-label="Open retained close package manifest">
                      {vm.closePackage.manifestLabel}
                    </Link>
                  ) : vm.closePackage.manifestLabel}
                </dd>
              </div>
              <div>
                <dt className="eyebrow-label">Evidence hash</dt>
                <dd className="break-all font-mono text-foreground">{vm.closePackage.evidenceHashLabel}</dd>
              </div>
              <div>
                <dt className="eyebrow-label">Publication</dt>
                <dd className="text-foreground">{vm.closePackage.publishedLabel}</dd>
              </div>
              <div>
                <dt className="eyebrow-label">Sign-off</dt>
                <dd className="text-foreground">{vm.closePackage.signerLabel}</dd>
              </div>
              <div>
                <dt className="eyebrow-label">Evidence</dt>
                <dd className="text-foreground">{vm.closePackage.evidenceLabel}</dd>
              </div>
              <div>
                <dt className="eyebrow-label">Approvals</dt>
                <dd className="text-foreground">{vm.closePackage.approvalLabel}</dd>
              </div>
              <div className="sm:col-span-2 lg:col-span-4">
                <dt className="eyebrow-label">Rationale</dt>
                <dd className="text-foreground">{vm.closePackage.rationaleLabel}</dd>
              </div>
            </dl>
          </CardContent>
        </Card>
      </section>

      <section className="grid gap-4">
        <Card className="panel-surface">
          <CardHeader>
            <CardTitle className="flex items-center gap-2">
              <GitBranch className="h-5 w-5 text-primary" aria-hidden="true" />
              {vm.timelineLabel}
            </CardTitle>
            <CardDescription>Append-only workflow events from the shared audit timeline.</CardDescription>
          </CardHeader>
          <CardContent>
            <DenseDataTable
              columns={timelineColumns}
              rows={vm.timeline}
              getRowId={(row) => row.id}
              getRowAriaLabel={(row) => row.ariaLabel}
              emptyText={vm.timelineEmptyText}
              ariaLabel="Operations continuity timeline"
            />
          </CardContent>
        </Card>
      </section>
    </div>
  );
}

function formatChecklistCommandError(err: unknown): string {
  if (err instanceof Error && err.message.trim().length > 0) {
    return err.message;
  }

  return "Checklist acknowledgement command failed.";
}

function formatBreakCommandError(err: unknown): string {
  if (err instanceof Error && err.message.trim().length > 0) {
    return err.message;
  }

  return "Break command failed.";
}

function formatApprovalSubmitCommandError(err: unknown): string {
  if (err instanceof Error && err.message.trim().length > 0) {
    return err.message;
  }

  return "Approval submission command failed.";
}

function formatApprovalDecisionCommandError(err: unknown): string {
  if (err instanceof Error && err.message.trim().length > 0) {
    return err.message;
  }

  return "Approval decision command failed.";
}

function formatCloseWorkflowCommandError(err: unknown): string {
  if (err instanceof Error && err.message.trim().length > 0) {
    return err.message;
  }

  return "Close package publication command failed.";
}

function buildReopenWorkflowFormDisabledReason(form: ReopenWorkflowFormState): string | null {
  if (!form.incidentId.trim()) {
    return "Incident id is required before governed reopen.";
  }

  if (!form.approvalReference.trim()) {
    return "Approval reference is required before governed reopen.";
  }

  if (!form.justification.trim()) {
    return "Justification is required before governed reopen.";
  }

  if (!form.impactSummary.trim()) {
    return "Impact summary is required before governed reopen.";
  }

  return null;
}

function formatReopenWorkflowCommandError(err: unknown): string {
  if (err instanceof Error && err.message.trim().length > 0) {
    return err.message;
  }

  return "Governed reopen command failed.";
}
