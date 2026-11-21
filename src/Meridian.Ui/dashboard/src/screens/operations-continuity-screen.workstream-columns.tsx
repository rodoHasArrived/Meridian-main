import { Link } from "react-router-dom";
import { Badge } from "@/components/ui/badge";
import { TechnicalDetails } from "@/components/ui/technical-details";
import type { DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { statusBadgeVariant } from "@/design-system/status-styles";
import type {
  OperationsContinuityCloseCockpitLaneRow,
  OperationsContinuityReconciliationLaneRow,
  OperationsTechnicalField
} from "@/screens/operations-continuity-screen.view-model";

export function OperationsSystemDetails({ fields, label = "System details" }: { fields?: OperationsTechnicalField[]; label?: string }) {
  if (!fields?.length) return null;
  return (
    <TechnicalDetails label={label} className="mt-2" contentClassName="py-2">
      <dl className="space-y-2 text-xs leading-5">
        {fields.map((field) => <div key={field.label}><dt className="font-medium">{field.label}</dt><dd className="break-words font-mono text-muted-foreground">{field.value}</dd></div>)}
      </dl>
    </TechnicalDetails>
  );
}

export const reconciliationLaneColumns: DenseDataTableColumn<OperationsContinuityReconciliationLaneRow>[] = [
  {
    id: "lane",
    label: "Workstream",
    render: (row) => (
      <div className="block min-w-0">
        <span className="block font-semibold text-foreground">{row.label}</span>
        <span className="mt-1 block text-xs leading-5 text-muted-foreground">{row.summary}</span>
        <OperationsSystemDetails fields={row.technicalDetails} />
      </div>
    )
  },
  {
    id: "status",
    label: "Status",
    render: (row) => (
      <span className="block text-xs leading-5">
        <Badge variant={statusBadgeVariant(row.statusTone)}>{row.statusLabel}</Badge>
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
          <Link to={row.evidenceHref} aria-label={`Open retained evidence for reconciliation workstream ${row.label}`}>
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
          <Link className="link-subtle mt-1 inline-flex" to={row.routeHref} aria-label={`Open reconciliation workstream: ${row.label}`}>
            {row.routeLabel}
          </Link>
        ) : (
          <span className="mt-1 block text-muted-foreground">{row.routeLabel}</span>
        )}
      </span>
    )
  }
];

export const closeCockpitLaneColumns: DenseDataTableColumn<OperationsContinuityCloseCockpitLaneRow>[] = [
  {
    id: "lane",
    label: "Workstream",
    render: (row) => (
      <div className="block min-w-0">
        <span className="block font-semibold text-foreground">{row.label}</span>
        <span className="mt-1 block text-xs leading-5 text-muted-foreground">{row.summary}</span>
        <OperationsSystemDetails fields={row.technicalDetails} />
      </div>
    )
  },
  {
    id: "status",
    label: "Status",
    render: (row) => <Badge variant={statusBadgeVariant(row.statusTone)}>{row.statusLabel}</Badge>
  },
  {
    id: "evidence",
    label: "Evidence",
    render: (row) => (
      <span className="block text-xs leading-5">
        <span className="block text-muted-foreground">{row.evidenceLabel}</span>
        {row.routeHref ? (
          <Link to={row.routeHref} aria-label={`Open private-capital close cockpit workstream: ${row.label}`}>
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

