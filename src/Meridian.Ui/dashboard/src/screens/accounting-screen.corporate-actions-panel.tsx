import { Table2 } from "lucide-react";
import { DenseRowDetailPanel } from "@/components/meridian/dense-row-detail-accessibility";
import { DenseDataTable, EntitySummary, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import type {
  CorporateActionRowViewModel,
  CorporateActionsViewState,
} from "@/screens/accounting-screen.view-model";

const corporateActionColumns: DenseDataTableColumn<CorporateActionRowViewModel>[] = [
  {
    id: "eventType",
    label: "Event type",
    render: (row) => (
      <span className="block min-w-0">
        <span className="block font-semibold text-foreground">{row.eventTypeLabel}</span>
        <span className="mt-1 block break-all font-mono text-[11px] text-muted-foreground">{row.corpActId}</span>
      </span>
    )
  },
  { id: "exDate", label: "Ex-date", render: (row) => <span className="font-mono text-muted-foreground">{row.exDateLabel}</span> },
  { id: "payDate", label: "Pay date", render: (row) => <span className="font-mono text-muted-foreground">{row.payDateLabel}</span> },
  { id: "lifecycle", label: "Lifecycle", render: (row) => <Badge variant={row.lifecycleState ? "outline" : "warning"}>{row.lifecycleState ?? "Not supplied"}</Badge> },
  { id: "amount", label: "Amount", align: "right", render: (row) => <span className="font-mono tabular-nums text-foreground">{row.amountLabel}</span> }
];

export function CorporateActionsPanel({
  view,
  onSelect
}: {
  view: CorporateActionsViewState;
  onSelect: (rowId: string) => void;
}) {
  return (
    <Card className="panel-surface">
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-base">
          <Table2 className="h-4 w-4 text-primary" />
          Corporate actions
        </CardTitle>
        <CardDescription>
          Dividends, splits, spin-offs, and other corporate events for <span className="font-mono">{view.securityId}</span>.
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-4">
        <span className="sr-only" aria-live="polite">{view.statusAnnouncement}</span>
        {view.loadingText && <p role="status" className="text-sm text-muted-foreground">{view.loadingText}</p>}
        {view.errorText && (
          <div role="alert" className="rounded-lg border border-danger/30 bg-danger/10 px-4 py-3 text-sm text-danger">
            <div>{view.errorText}</div>
            {view.errorDetails.length > 0 ? (
              <ul className="mt-2 list-disc space-y-1 pl-5 text-xs leading-5">
                {view.errorDetails.map((detail) => (
                  <li key={detail}>{detail}</li>
                ))}
              </ul>
            ) : null}
          </div>
        )}
        {!view.loadingText && !view.errorText && (
          <div className="grid gap-4 2xl:grid-cols-[minmax(0,1.35fr)_minmax(18rem,0.65fr)]">
            <DenseDataTable
              columns={corporateActionColumns}
              rows={view.rows}
              getRowId={(row) => row.rowId}
              getRowAriaLabel={(row) => row.ariaLabel}
              getRowSelectAriaLabel={(row) => row.selectAriaLabel}
              getRowAriaControls={(row) => row.detailPanelId}
              getRowAriaExpanded={(row) => row.isExpanded}
              onRowSelect={(row) => onSelect(row.rowId)}
              selectedRowId={view.selectedRowId}
              emptyText={view.emptyText}
              ariaLabel={view.tableLabel}
              caption={view.tableCaption}
            />
            <DenseRowDetailPanel
              id={view.detailPanelId}
              ariaLabel="Corporate action detail workspace"
              selectedSourceLabel="Selected from corporate actions"
              className="row-detail-panel h-fit min-w-0"
            >
              {view.selectedDetail ? (
                <EntitySummary
                  eyebrow={view.selectedDetail.eyebrow}
                  title={view.selectedDetail.title}
                  subtitle={view.selectedDetail.subtitle}
                  description={view.selectedDetail.description}
                  ariaLabel={view.selectedDetail.ariaLabel}
                  status={<Badge variant={view.selectedDetail.statusLabel === "Pay date scheduled" ? "success" : "warning"}>{view.selectedDetail.statusLabel}</Badge>}
                  fields={view.selectedDetail.fields.map((field) => ({ label: field.label, value: field.value }))}
                />
              ) : (
                <div role="region" aria-label={view.detailEmptyAriaLabel}>
                  <div className="eyebrow-label">Corporate action detail</div>
                  <h3 className="mt-2 text-sm font-semibold text-foreground">{view.detailEmptyTitle}</h3>
                  <p className="mt-2 text-sm leading-6 text-muted-foreground">{view.detailEmptyText}</p>
                </div>
              )}
            </DenseRowDetailPanel>
          </div>
        )}
      </CardContent>
    </Card>
  );
}
