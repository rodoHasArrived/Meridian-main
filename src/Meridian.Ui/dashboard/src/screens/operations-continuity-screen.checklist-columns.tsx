import { Link } from "react-router-dom";
import type { DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { statusBadgeVariant } from "@/design-system/status-styles";
import type { OperationsContinuityChecklistRow } from "@/screens/operations-continuity-screen.view-model";

interface ChecklistColumnOptions {
  pendingTaskId: string | null;
  onAcknowledge: (row: OperationsContinuityChecklistRow) => void;
}

export function buildChecklistColumns({
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
      render: (row) => <Badge variant={statusBadgeVariant(row.statusTone)}>{row.statusLabel}</Badge>
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
