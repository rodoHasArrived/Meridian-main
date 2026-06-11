import { AlertTriangle, ArrowRight, Gauge, GitBranch, ListChecks, Lock, RefreshCcw, Workflow } from "lucide-react";
import { Link } from "react-router-dom";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { DenseDataTable, EntitySummary, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { cn } from "@/lib/utils";
import {
  useOperationsContinuityScreenViewModel,
  type OperationsAccountingRecordEvidenceRow,
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
  type OperationsContinuityTimelineRow,
  type OperationsContinuityTone,
  type OperationsContinuityWorkflowRow
} from "@/screens/operations-continuity-screen.view-model";

const toneBadge: Record<OperationsContinuityTone, "success" | "warning" | "danger" | "outline"> = {
  ready: "success",
  review: "warning",
  blocked: "danger",
  neutral: "outline"
};

const tonePanel: Record<OperationsContinuityTone, string> = {
  ready: "border-success/35 bg-success/10",
  review: "border-warning/35 bg-warning/10",
  blocked: "border-danger/35 bg-danger/10",
  neutral: "border-border/70 bg-secondary/25"
};

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
    render: (row) => <Badge variant={toneBadge[row.statusTone]}>{row.statusLabel}</Badge>
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
    render: (row) => <Badge variant={toneBadge[row.statusTone]}>{row.statusLabel}</Badge>
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
    render: (row) => <Badge variant={toneBadge[row.severityTone]}>{row.severityLabel}</Badge>
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
        <Badge variant={toneBadge[row.statusTone]}>{row.statusLabel}</Badge>
        <span className="mt-1 block font-mono text-foreground">{row.valueLabel}</span>
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
        <Badge variant={toneBadge[row.statusTone]}>{row.statusLabel}</Badge>
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

const breakCaseColumns: DenseDataTableColumn<OperationsContinuityBreakCaseRow>[] = [
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
        <Badge variant={toneBadge[row.statusTone]}>{row.statusLabel}</Badge>
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
    render: (row) => (
      <span className="block text-xs leading-5">
        <span className="block text-foreground">{row.actionLabel}</span>
        <span className="block text-muted-foreground">{row.approvalLabel}</span>
        <span className="block text-muted-foreground">{row.blockedOutputsLabel}</span>
        <span className="block font-mono text-muted-foreground">{row.symbolLabel}</span>
      </span>
    )
  }
];

const checklistColumns: DenseDataTableColumn<OperationsContinuityChecklistRow>[] = [
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
    render: (row) => <Badge variant={toneBadge[row.statusTone]}>{row.statusLabel}</Badge>
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
    render: (row) => <span className="text-xs leading-5 text-muted-foreground">{row.acknowledgementLabel}</span>
  }
];

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
        <Badge variant={toneBadge[row.statusTone]}>{row.statusLabel}</Badge>
        <span className="mt-1 block text-muted-foreground">{row.breakCountLabel}</span>
      </span>
    )
  },
  {
    id: "evidence",
    label: "Evidence",
    render: (row) => <span className="text-xs text-muted-foreground">{row.evidenceLabel}</span>
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
    render: (row) => <Badge variant={toneBadge[row.statusTone]}>{row.statusLabel}</Badge>
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
        <Badge variant={toneBadge[row.readinessTone]}>{row.readinessLabel}</Badge>
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
        <Badge variant={toneBadge[row.statusTone]}>{row.statusLabel}</Badge>
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
        {row.workflowHref ? (
          <Link to={row.workflowHref} aria-label={`Open private-capital close approval workflow ${row.workflowLabel}`}>
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
        <Badge variant={toneBadge[row.statusTone]}>{row.statusLabel}</Badge>
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
    render: (row) => <Badge variant={toneBadge[row.statusTone]}>{row.statusLabel}</Badge>
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

        <Card className={cn("border", tonePanel[vm.nextAction.statusTone])}>
          <CardHeader>
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <CardTitle className="flex items-center gap-2">
                  <ArrowRight className="h-5 w-5 text-primary" aria-hidden="true" />
                  Next action
                </CardTitle>
                <CardDescription>{vm.nextAction.detail}</CardDescription>
              </div>
              <Badge variant={toneBadge[vm.nextAction.statusTone]}>
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
          <div className={cn("rounded-lg border", tonePanel[vm.selectedDetail.statusTone])}>
            <EntitySummary
              id={vm.selectedDetail.id}
              eyebrow="Selected workflow"
              title={vm.selectedDetail.title}
              subtitle={vm.selectedDetail.subtitle}
              description={vm.selectedDetail.description}
              ariaLabel={vm.selectedDetail.ariaLabel}
              status={<Badge variant={toneBadge[vm.selectedDetail.statusTone]}>{vm.selectedDetail.statusLabel}</Badge>}
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
        <Card className={cn("border", tonePanel[vm.dashboard.statusTone])}>
          <CardHeader>
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <CardTitle className="flex items-center gap-2">
                  <Gauge className="h-5 w-5 text-primary" aria-hidden="true" />
                  {vm.dashboard.title}
                </CardTitle>
                <CardDescription>{vm.dashboard.summaryLabel}</CardDescription>
              </div>
              <Badge variant={toneBadge[vm.dashboard.statusTone]}>
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
        <Card className={cn("border", tonePanel[vm.closeCockpit.statusTone])}>
          <CardHeader>
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <CardTitle className="flex items-center gap-2">
                  <Gauge className="h-5 w-5 text-primary" aria-hidden="true" />
                  {vm.closeCockpit.title}
                </CardTitle>
                <CardDescription>{vm.closeCockpit.summaryLabel}</CardDescription>
              </div>
              <Badge variant={toneBadge[vm.closeCockpit.statusTone]}>
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
          <CardContent>
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
              <Badge variant={toneBadge[vm.checklistSummary.statusTone]}>
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
          <CardContent>
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
        <Card className={cn("border", tonePanel[vm.accountingRecordSummary.statusTone])}>
          <CardHeader>
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <CardTitle className="flex items-center gap-2">
                  <ListChecks className="h-5 w-5 text-primary" aria-hidden="true" />
                  {vm.accountingRecordLabel}
                </CardTitle>
                <CardDescription>{vm.accountingRecordSummary.summaryLabel}</CardDescription>
              </div>
              <Badge variant={toneBadge[vm.accountingRecordSummary.statusTone]}>
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

        <Card className={cn("border", tonePanel[vm.closeGovernance.statusTone])}>
          <CardHeader>
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <CardTitle className="flex items-center gap-2">
                  <Lock className="h-5 w-5 text-primary" aria-hidden="true" />
                  {vm.closeGovernance.title}
                </CardTitle>
                <CardDescription>{vm.closeGovernance.lockDetail}</CardDescription>
              </div>
              <Badge variant={toneBadge[vm.closeGovernance.statusTone]}>
                {vm.closeGovernance.statusLabel}
              </Badge>
            </div>
          </CardHeader>
          <CardContent>
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
          </CardContent>
        </Card>

        <Card className={cn("border", tonePanel[vm.closePackage.statusTone])}>
          <CardHeader>
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <CardTitle>{vm.closePackage.title}</CardTitle>
                <CardDescription>Governed close publication from the shared operations-continuity workflow.</CardDescription>
              </div>
              <Badge variant={toneBadge[vm.closePackage.statusTone]}>{vm.closePackage.statusLabel}</Badge>
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
