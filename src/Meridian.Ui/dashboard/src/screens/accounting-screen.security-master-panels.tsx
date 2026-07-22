import { Briefcase, Network, ShieldCheck, Table2, TrendingUp } from "lucide-react";
import { Link } from "react-router-dom";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { DenseDataTable, EntitySummary, ToolbarStrip, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { cn } from "@/lib/utils";
import type {
  CorporateActionsViewState,
  CorporateActionRowViewModel,
  InstrumentPassportProviderConfidenceRowViewModel,
  InstrumentPassportViewState,
  ReferenceDataEndpointRowViewModel,
  ReferenceDataWorkbenchViewState,
  SecurityOpenLotReadModelViewState,
  SecurityOpenLotRowViewModel,
  SecuritySchedulesViewState,
  SecurityScheduleRowViewModel,
  TradingParametersViewState
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
  { id: "amount", label: "Amount", align: "right", render: (row) => <span className="font-mono tabular-nums text-foreground">{row.amountLabel}</span> }
];

const securityScheduleColumns: DenseDataTableColumn<SecurityScheduleRowViewModel>[] = [
  {
    id: "eventType",
    label: "Event",
    render: (row) => (
      <span className="block min-w-0">
        <span className="block font-semibold text-foreground">{row.eventTypeLabel}</span>
        <span className="mt-1 block break-all font-mono text-[11px] text-muted-foreground">{row.eventId}</span>
      </span>
    )
  },
  { id: "paymentDate", label: "Payment date", render: (row) => <span className="font-mono text-muted-foreground">{row.paymentDateLabel}</span> },
  { id: "expected", label: "Expected", align: "right", render: (row) => <span className="font-mono tabular-nums text-foreground">{row.expectedAmountLabel}</span> },
  {
    id: "actual",
    label: "Actual",
    align: "right",
    render: (row) => (
      <span className={cn("font-mono tabular-nums", row.actualAmount === null ? "text-muted-foreground" : "text-foreground")}>
        {row.actualAmountLabel}
      </span>
    )
  },
  {
    id: "variance",
    label: "Variance",
    align: "right",
    render: (row) => (
      <span className={cn("font-mono tabular-nums", row.postingStatus === "Variance" ? "text-danger" : "text-muted-foreground")}>
        {row.varianceLabel}
      </span>
    )
  },
  { id: "factor", label: "Factor", align: "right", render: (row) => <span className="font-mono tabular-nums text-muted-foreground">{row.factorLabel}</span> },
  { id: "status", label: "Status", render: (row) => <Badge variant={row.postingStatusTone}>{row.postingStatusLabel}</Badge> }
];

const securityOpenLotColumns: DenseDataTableColumn<SecurityOpenLotRowViewModel>[] = [
  {
    id: "lot",
    label: "Lot",
    render: (row) => (
      <span className="block min-w-0">
        <span className="block font-semibold text-foreground">{row.lotId}</span>
        <span className="mt-1 block break-all font-mono text-[11px] text-muted-foreground">{row.runId}</span>
      </span>
    )
  },
  { id: "scope", label: "Scope", render: (row) => <span className="text-muted-foreground">{row.scopeLabel}</span> },
  { id: "tradeDate", label: "Trade", render: (row) => <span className="font-mono text-muted-foreground">{row.tradeDateLabel}</span> },
  { id: "quantity", label: "Quantity", align: "right", render: (row) => <span className="font-mono tabular-nums text-foreground">{row.quantityLabel}</span> },
  { id: "face", label: "Face", align: "right", render: (row) => <span className="font-mono tabular-nums text-muted-foreground">{row.faceLabel}</span> },
  { id: "factor", label: "Factor adj.", align: "right", render: (row) => <span className="font-mono tabular-nums text-muted-foreground">{row.factorAdjustedLabel}</span> },
  { id: "cost", label: "Cost", align: "right", render: (row) => <span className="font-mono tabular-nums text-foreground">{row.costBasisLabel}</span> },
  {
    id: "pnl",
    label: "Unrealized",
    align: "right",
    render: (row) => (
      <span className={cn("font-mono tabular-nums", row.unrealizedPnl !== null && row.unrealizedPnl < 0 ? "text-danger" : "text-muted-foreground")}>
        {row.unrealizedPnlLabel}
      </span>
    )
  },
  { id: "status", label: "Status", render: (row) => <Badge variant={row.statusTone}>{row.statusLabel}</Badge> }
];

const referenceDataEndpointColumns: DenseDataTableColumn<ReferenceDataEndpointRowViewModel>[] = [
  {
    id: "endpoint",
    label: "Data source",
    render: (row) => (
      <span className="block min-w-0">
        <span className="block font-semibold text-foreground">{row.label}</span>
        <span className="mt-1 block text-xs text-muted-foreground">{row.displaySummary}</span>
      </span>
    )
  },
  { id: "family", label: "Family", render: (row) => <span className="text-muted-foreground">{row.familyLabel}</span> },
  { id: "method", label: "Access", render: (row) => <span className="text-xs text-foreground">{row.accessLabel}</span> },
  { id: "payload", label: "Records", align: "right", render: (row) => <span className="font-mono text-xs tabular-nums text-muted-foreground">{row.countLabel}</span> },
  { id: "latency", label: "Latency", align: "right", render: (row) => <span className="font-mono text-xs tabular-nums text-muted-foreground">{row.latencyLabel}</span> },
  { id: "status", label: "Status", render: (row) => <Badge variant={row.statusBadgeVariant} dot>{row.statusLabel}</Badge> }
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
            <div
              id={view.detailPanelId}
              data-selected-source="Selected from corporate actions"
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
            </div>
          </div>
        )}
      </CardContent>
    </Card>
  );
}

export function ReferenceDataWorkbenchPanel({
  view,
  onSelect
}: {
  view: ReferenceDataWorkbenchViewState;
  onSelect: (rowId: string) => void;
}) {
  return (
    <Card className="panel-surface">
      <CardHeader>
        <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
          <div className="min-w-0">
            <CardTitle className="flex items-center gap-2 text-base">
              <Network className="h-4 w-4 text-primary" aria-hidden="true" />
              {view.title}
            </CardTitle>
            <CardDescription className="mt-2">{view.description}</CardDescription>
          </div>
          <div className="min-w-0 lg:max-w-[32rem]">
            <ToolbarStrip
              ariaLabel="Reference data source coverage metrics"
              items={view.metrics.map((metric) => ({
                id: metric.id,
                label: metric.label,
                value: metric.value,
                active: metric.tone === "success" || metric.tone === "warning" || metric.tone === "danger"
              }))}
            />
          </div>
        </div>
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
          <div className="grid gap-4 2xl:grid-cols-[minmax(0,1.4fr)_minmax(22rem,0.6fr)]">
            <DenseDataTable
              columns={referenceDataEndpointColumns}
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
            <div id={view.detailPanelId} data-selected-source="Selected from reference endpoints" className="row-detail-panel h-fit min-w-0">
              {view.selectedDetail ? (
                <div className="space-y-4">
                  <EntitySummary
                    eyebrow={view.selectedDetail.eyebrow}
                    title={view.selectedDetail.title}
                    subtitle={view.selectedDetail.subtitle}
                    description={view.selectedDetail.description}
                    ariaLabel={view.selectedDetail.ariaLabel}
                    status={<Badge variant={view.selectedDetail.statusBadgeVariant} dot>{view.selectedDetail.statusLabel}</Badge>}
                    fields={view.selectedDetail.fields.map((field) => ({ label: field.label, value: field.value }))}
                  />
                  {view.selectedDetail.responsePreview ? (
                    <pre className="max-h-72 overflow-auto rounded-md border border-border bg-muted/40 p-3 font-mono text-[11px] leading-5 text-muted-foreground">
                      {view.selectedDetail.responsePreview}
                    </pre>
                  ) : null}
                  {view.selectedDetail.errorSummary ? (
                    <div role="alert" className="rounded-lg border border-danger/30 bg-danger/10 px-4 py-3 text-sm text-danger">
                      <div>{view.selectedDetail.errorSummary}</div>
                      {view.selectedDetail.errorDetails.length > 0 ? (
                        <ul className="mt-2 list-disc space-y-1 pl-5 text-xs leading-5">
                          {view.selectedDetail.errorDetails.map((detail) => (
                            <li key={detail}>{detail}</li>
                          ))}
                        </ul>
                      ) : null}
                    </div>
                  ) : null}
                </div>
              ) : (
                <div role="region" aria-label={view.detailEmptyAriaLabel}>
                  <div className="eyebrow-label">Reference data detail</div>
                  <h3 className="mt-2 text-sm font-semibold text-foreground">{view.detailEmptyTitle}</h3>
                  <p className="mt-2 text-sm leading-6 text-muted-foreground">{view.detailEmptyText}</p>
                </div>
              )}
            </div>
          </div>
        )}
      </CardContent>
    </Card>
  );
}

export function SecuritySchedulesPanel({
  view,
  onSelect
}: {
  view: SecuritySchedulesViewState;
  onSelect: (rowId: string) => void;
}) {
  return (
    <Card className="panel-surface">
      <CardHeader>
        <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
          <div className="min-w-0">
            <CardTitle className="flex items-center gap-2 text-base">
              <Table2 className="h-4 w-4 text-primary" aria-hidden="true" />
              {view.title}
            </CardTitle>
            <CardDescription className="mt-2">{view.description}</CardDescription>
          </div>
          <div className="min-w-0 lg:max-w-[28rem]">
            <ToolbarStrip ariaLabel={view.toolbarAriaLabel} items={view.toolbarItems} />
          </div>
        </div>
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
          <div className="grid gap-4 2xl:grid-cols-[minmax(0,1.45fr)_minmax(20rem,0.55fr)]">
          <DenseDataTable
            columns={securityScheduleColumns}
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
          <div id={view.detailPanelId} data-selected-source="Selected from schedule events" className="row-detail-panel h-fit min-w-0">
            {view.selectedDetail ? (
              <EntitySummary
                eyebrow={view.selectedDetail.eyebrow}
                title={view.selectedDetail.title}
                subtitle={view.selectedDetail.subtitle}
                description={view.selectedDetail.description}
                ariaLabel={view.selectedDetail.ariaLabel}
                status={<Badge variant={view.selectedDetail.statusTone}>{view.selectedDetail.statusLabel}</Badge>}
                fields={view.selectedDetail.fields.map((field) => ({ label: field.label, value: field.value }))}
              />
            ) : (
              <div role="region" aria-label={view.detailEmptyAriaLabel}>
                <div className="eyebrow-label">Schedule event detail</div>
                <h3 className="mt-2 text-sm font-semibold text-foreground">{view.detailEmptyTitle}</h3>
                <p className="mt-2 text-sm leading-6 text-muted-foreground">{view.detailEmptyText}</p>
              </div>
            )}
          </div>
          </div>
        )}
      </CardContent>
    </Card>
  );
}

export function SecurityOpenLotReadModelPanel({
  view,
  onSelect
}: {
  view: SecurityOpenLotReadModelViewState;
  onSelect: (rowId: string) => void;
}) {
  return (
    <Card className="panel-surface">
      <CardHeader>
        <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
          <div className="min-w-0">
            <CardTitle className="flex items-center gap-2 text-base">
              <Briefcase className="h-4 w-4 text-primary" aria-hidden="true" />
              {view.title}
            </CardTitle>
            <CardDescription className="mt-2">
              {view.description}
              {view.asOfLabel !== "—" ? <> As of {view.asOfLabel}.</> : null}
            </CardDescription>
          </div>
          <div className="min-w-0 lg:max-w-[28rem]">
            <ToolbarStrip ariaLabel={view.toolbarAriaLabel} items={view.toolbarItems} />
          </div>
        </div>
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
          <>
            <p className="text-sm leading-6 text-muted-foreground">{view.summary}</p>
            <div className="grid gap-4 2xl:grid-cols-[minmax(0,1.45fr)_minmax(20rem,0.55fr)]">
              <DenseDataTable
                columns={securityOpenLotColumns}
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
              <div id={view.detailPanelId} data-selected-source="Selected from open lots" className="row-detail-panel h-fit min-w-0">
                {view.selectedDetail ? (
                  <EntitySummary
                    eyebrow={view.selectedDetail.eyebrow}
                    title={view.selectedDetail.title}
                    subtitle={view.selectedDetail.subtitle}
                    description={view.selectedDetail.description}
                    ariaLabel={view.selectedDetail.ariaLabel}
                    status={<Badge variant={view.selectedDetail.statusTone}>{view.selectedDetail.statusLabel}</Badge>}
                    fields={view.selectedDetail.fields.map((field) => ({ label: field.label, value: field.value }))}
                  />
                ) : (
                  <div role="region" aria-label={view.detailEmptyAriaLabel}>
                    <div className="eyebrow-label">Open lot detail</div>
                    <h3 className="mt-2 text-sm font-semibold text-foreground">{view.detailEmptyTitle}</h3>
                    <p className="mt-2 text-sm leading-6 text-muted-foreground">{view.detailEmptyText}</p>
                  </div>
                )}
              </div>
            </div>
          </>
        )}
      </CardContent>
    </Card>
  );
}

const instrumentPassportProviderColumns: DenseDataTableColumn<InstrumentPassportProviderConfidenceRowViewModel>[] = [
  {
    id: "provider",
    label: "Provider",
    render: (row) => <span className="font-medium text-foreground">{row.providerLabel}</span>
  },
  {
    id: "symbol",
    label: "Symbol",
    render: (row) => <span className="font-mono text-xs text-muted-foreground">{row.symbolLabel}</span>
  },
  {
    id: "confidence",
    label: "Confidence",
    align: "right",
    render: (row) => <span className="font-mono text-xs tabular-nums text-foreground">{row.confidenceLabel}</span>
  },
  {
    id: "freshness",
    label: "Freshness",
    render: (row) => <span className="font-mono text-xs text-muted-foreground">{row.freshnessLabel}</span>
  },
  {
    id: "status",
    label: "Status",
    render: (row) => <Badge variant={row.statusTone} dot>{row.statusLabel}</Badge>
  }
];

export function InstrumentPassportPanel({ view }: { view: InstrumentPassportViewState }) {
  return (
    <Card className="panel-surface">
      <CardHeader>
        <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
          <div className="min-w-0">
            <CardTitle className="flex items-center gap-2 text-base">
              <ShieldCheck className="h-4 w-4 text-primary" aria-hidden="true" />
              {view.title}
            </CardTitle>
            <CardDescription className="mt-2">{view.description}</CardDescription>
          </div>
          <Badge variant={view.statusBadgeVariant} dot className="w-fit shrink-0">
            {view.statusLabel}
          </Badge>
        </div>
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
          <>
          <div className="grid gap-4 2xl:grid-cols-[minmax(20rem,0.65fr)_minmax(0,1.35fr)]">
            <EntitySummary
              eyebrow="Passport summary"
              title={view.securityId}
              subtitle="Identifiers, trust, pricing, and usage"
              description="Instrument passport evidence for the selected Security Master record."
              ariaLabel={`Instrument passport summary for ${view.securityId}`}
              status={<Badge variant={view.statusBadgeVariant} dot>{view.statusLabel}</Badge>}
              fields={view.fields.map((field) => ({ label: field.label, value: field.value }))}
            />
            <DenseDataTable
              columns={instrumentPassportProviderColumns}
              rows={view.providerRows}
              getRowId={(row) => row.rowId}
              getRowAriaLabel={(row) => row.ariaLabel}
              emptyText={view.providerEmptyText}
              ariaLabel={view.providerTableLabel}
              caption={view.providerTableCaption}
            />
          </div>
          <section className="space-y-3" aria-label={view.operationsWorkbenchTitle}>
            <div className="flex flex-col gap-2 sm:flex-row sm:items-start sm:justify-between">
              <div className="min-w-0">
                <h3 className="text-sm font-semibold text-foreground">{view.operationsWorkbenchTitle}</h3>
                <p className="mt-1 text-sm leading-6 text-muted-foreground">{view.operationsWorkbenchSummary}</p>
              </div>
              <Badge variant={view.operationsWorkbenchStatusBadgeVariant} dot className="w-fit shrink-0">
                {view.operationsWorkbenchStatusLabel}
              </Badge>
            </div>
            {view.operationsReadiness.length > 0 ? (
              <div className="grid gap-2 md:grid-cols-2 xl:grid-cols-5" role="list" aria-label="Operations readiness">
                {view.operationsReadiness.map((item) => (
                  <div key={item.readinessId} className="rounded-md border border-border/70 bg-secondary/20 px-3 py-2" role="listitem">
                    <div className="flex items-start justify-between gap-2">
                      <span className="text-xs font-semibold text-foreground">{item.label}</span>
                      <Badge variant={item.statusBadgeVariant}>{item.statusLabel}</Badge>
                    </div>
                    <p className="mt-2 text-xs leading-5 text-muted-foreground">{item.summary}</p>
                    <div className="mt-2 flex flex-wrap gap-2 font-mono text-[11px] text-muted-foreground">
                      <span>{item.evidenceLabel}</span>
                      <span>{item.blockerLabel}</span>
                    </div>
                    {item.isReady ? null : (
                      <p className="mt-2 text-xs leading-5 text-warning">{item.nextAction}</p>
                    )}
                    {item.route ? (
                      <Button asChild variant="ghost" size="sm" className="mt-2 h-7 px-2 text-xs">
                        <Link to={item.route} aria-label={`Follow next action for ${item.label}`}>
                          Follow
                        </Link>
                      </Button>
                    ) : null}
                  </div>
                ))}
              </div>
            ) : null}
            {view.operationsPanels.length > 0 ? (
              <div className="grid gap-3 xl:grid-cols-2" role="list" aria-label="Security Master operations workbench panels">
                {view.operationsPanels.map((panel) => (
                  <section key={panel.panelId} className="rounded-md border border-border/70 bg-background/35 p-3" role="listitem" aria-label={panel.title}>
                    <div className="flex items-start justify-between gap-3">
                      <div className="min-w-0">
                        <h4 className="text-sm font-semibold text-foreground">{panel.title}</h4>
                        <p className="mt-1 text-xs leading-5 text-muted-foreground">{panel.summary}</p>
                      </div>
                      <Badge variant={panel.statusBadgeVariant}>{panel.statusLabel}</Badge>
                    </div>
                    <div className="mt-3 space-y-2">
                      {panel.items.map((item) => (
                        <div key={item.itemId} className="grid gap-2 rounded-md border border-border/50 bg-secondary/15 px-3 py-2 lg:grid-cols-[minmax(0,0.35fr)_minmax(0,0.65fr)]">
                          <div className="min-w-0">
                            <div className="flex flex-wrap items-center gap-2">
                              <span className="text-xs font-semibold text-foreground">{item.label}</span>
                              <Badge variant={item.statusBadgeVariant}>{item.statusLabel}</Badge>
                            </div>
                            <div className="mt-1 break-words font-mono text-[11px] text-muted-foreground">{item.value}</div>
                          </div>
                          <div className="min-w-0">
                            <p className="text-xs leading-5 text-muted-foreground">{item.detail}</p>
                            <div className="mt-1 flex flex-wrap gap-2 font-mono text-[11px] text-muted-foreground">
                              <span>{item.evidenceLabel}</span>
                              <span>{item.blockerLabel}</span>
                            </div>
                            {item.route ? (
                              <Button asChild variant="ghost" size="sm" className="mt-2 h-7 px-2 text-xs">
                                <Link to={item.route} aria-label={`Follow handoff action ${item.label}`}>
                                  Follow
                                </Link>
                              </Button>
                            ) : null}
                          </div>
                        </div>
                      ))}
                    </div>
                  </section>
                ))}
              </div>
            ) : null}
          </section>
          </>
        )}
      </CardContent>
    </Card>
  );
}
export function TradingParametersPanel({ view }: { view: TradingParametersViewState }) {
  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-base">
          <TrendingUp className="h-4 w-4 text-primary" />
          Trading parameters
        </CardTitle>
        <CardDescription>
          Lot size, tick size, margin, and circuit-breaker constraints
          {view.securityId ? <> for <span className="font-mono">{view.securityId}</span></> : null}
          {view.asOfLabel !== "—" ? <> as of {view.asOfLabel}</> : null}.
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-2">
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
        {!view.loadingText && !view.errorText && view.fields.length === 0 && (
          <p className="text-sm text-muted-foreground">No trading parameters available for this security.</p>
        )}
        {view.fields.length > 0 && (
          <dl className="grid gap-2">
            {view.fields.map((field) => (
              <div key={field.label} className="grid min-w-0 grid-cols-[minmax(0,0.6fr)_minmax(0,1fr)] items-start gap-3 rounded-md border border-border/60 bg-secondary/25 px-3 py-2">
                <dt className="min-w-0 text-xs text-muted-foreground">{field.label}</dt>
                <dd className={cn(
                  "min-w-0 break-words text-right font-mono text-xs",
                  field.tone === "warning" ? "text-warning" : "text-foreground"
                )}>
                  {field.value}
                </dd>
              </div>
            ))}
          </dl>
        )}
      </CardContent>
    </Card>
  );
}
