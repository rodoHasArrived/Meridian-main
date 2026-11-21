import { Link } from "react-router-dom";
import type { DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { Badge } from "@/components/ui/badge";
import { statusBadgeVariant } from "@/design-system/status-styles";
import type { OperationsContinuityCloseCalendarRow } from "@/screens/operations-continuity-screen.view-model";

export const closeCalendarColumns: DenseDataTableColumn<OperationsContinuityCloseCalendarRow>[] = [
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
        <Badge variant={statusBadgeVariant(row.statusTone)}>{row.statusLabel}</Badge>
        <span className="mt-1 block text-muted-foreground">{row.dueLabel}</span>
      </span>
    )
  },
  {
    id: "readiness",
    label: "Readiness",
    render: (row) => (
      <span className="block text-xs leading-5">
        <Badge variant={statusBadgeVariant(row.readinessTone)}>{row.readinessLabel}</Badge>
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
