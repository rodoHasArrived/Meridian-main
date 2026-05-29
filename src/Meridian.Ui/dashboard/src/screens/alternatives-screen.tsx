import { useState } from "react";
import { AlertTriangle, Archive, CalendarClock, FileText, History, Landmark, WalletCards } from "lucide-react";
import { DenseDataTable, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import {
  Sheet,
  SheetBody,
  SheetCloseButton,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle
} from "@/components/ui/sheet";
import {
  buildAlternativesScreenViewModel,
  type AlternativesSummaryCard,
  type AlternativesTone,
  type AlternativesWarning,
  type CapitalActivityItem,
  type DocumentEvidenceItem,
  type PrivateAssetRecord,
  type ValuationHistoryPoint
} from "@/screens/alternatives-screen.view-model";

const toneBadgeVariant: Record<AlternativesTone, "default" | "success" | "warning" | "danger"> = {
  default: "default",
  success: "success",
  warning: "warning",
  danger: "danger"
};

const assetColumns: DenseDataTableColumn<PrivateAssetRecord>[] = [
  {
    id: "name",
    label: "Private asset",
    render: (row) => (
      <div>
        <div className="font-medium text-foreground">{row.name}</div>
        <div className="text-xs text-muted-foreground">{row.strategy} · {row.entity}</div>
      </div>
    )
  },
  {
    id: "nav",
    label: "NAV",
    align: "right",
    render: (row) => <span className="font-mono text-foreground">{row.nav}</span>
  },
  {
    id: "commitment",
    label: "Commitment",
    align: "right",
    render: (row) => <span className="font-mono text-muted-foreground">{row.commitment}</span>
  },
  {
    id: "unfunded",
    label: "Unfunded",
    align: "right",
    render: (row) => <span className="font-mono text-warning">{row.unfunded}</span>
  },
  {
    id: "source",
    label: "Source",
    render: (row) => <Badge variant={row.source === "import" ? "warning" : "outline"}>{row.sourceLabel}</Badge>
  },
  {
    id: "warnings",
    label: "Operator warnings",
    render: (row) => <WarningPills warnings={row.warnings} />
  }
];

export function AlternativesScreen() {
  const [selectedAssetId, setSelectedAssetId] = useState<string | null>(null);
  const [detailOpen, setDetailOpen] = useState(false);
  const vm = buildAlternativesScreenViewModel(selectedAssetId);

  const selectAsset = (asset: PrivateAssetRecord) => {
    setSelectedAssetId(asset.id);
    setDetailOpen(true);
  };

  return (
    <section className="space-y-6" aria-label={vm.route.ariaLabel}>
      <header className="space-y-4">
        <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
          <div className="min-w-0">
            <div className="eyebrow-label">{vm.route.workspaceLabel} / {vm.route.label}</div>
            <h1 className="text-2xl font-semibold tracking-tight text-foreground">{vm.route.title}</h1>
            <p className="mt-2 max-w-4xl text-sm leading-6 text-muted-foreground">{vm.route.description}</p>
            <p className="mt-3 rounded-md border border-warning/40 bg-warning/10 px-3 py-2 text-sm text-warning">
              {vm.route.dataSourcePolicy}
            </p>
          </div>
          <div className="flex flex-wrap gap-2 lg:justify-end">
            {vm.statusChips.map((chip) => (
              <span
                key={chip.label}
                className="rounded-md border border-border bg-secondary/40 px-3 py-2 text-xs text-muted-foreground"
                aria-label={`${chip.label}: ${chip.value}`}
              >
                {chip.label}<b className="ml-2 font-mono text-foreground">{chip.value}</b>
              </span>
            ))}
          </div>
        </div>
      </header>

      <section className="grid gap-4 md:grid-cols-2 xl:grid-cols-4" aria-label="Alternatives commitment and warning summary">
        <h2 className="sr-only">Alternatives commitment and warning summary</h2>
        {vm.summaryCards.map((card) => <SummaryCard key={card.id} card={card} />)}
      </section>

      <Card className="panel-surface border-border/80">
        <CardHeader>
          <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
            <div>
              <div className="eyebrow-label">Private asset list</div>
              <CardTitle className="mt-2 flex items-center gap-2 text-base">
                <WalletCards className="h-4 w-4 text-primary" aria-hidden="true" />
                Fixture/import-backed private asset records
              </CardTitle>
              <CardDescription>
                Select an asset to open the detail drawer for commitments, valuation history, documents, capital activity, and stale data warnings.
              </CardDescription>
            </div>
            <div className="flex flex-wrap gap-2" aria-label="Explicit operator warning labels">
              {vm.explicitWarnings.map((warning) => <Badge key={warning} variant="warning">{warning}</Badge>)}
            </div>
          </div>
        </CardHeader>
        <CardContent>
          <DenseDataTable
            columns={assetColumns}
            rows={vm.privateAssets}
            getRowId={(row) => row.id}
            getRowAriaLabel={(row) => row.ariaLabel}
            getRowClassName={(row) => row.rowClassName}
            getRowSelectAriaLabel={(row) => row.selectAriaLabel}
            onRowSelect={selectAsset}
            selectedRowId={vm.selectedAsset.id}
            emptyText={vm.listEmptyText}
            ariaLabel="Private asset list"
            tableId="alternatives-private-assets-table"
            caption="Private alternatives fixture and statement-import records"
          />
        </CardContent>
      </Card>

      <Card className="panel-surface border-border/80" aria-label="Stale data warnings">
        <CardHeader>
          <CardTitle className="flex items-center gap-2 text-base">
            <AlertTriangle className="h-4 w-4 text-warning" aria-hidden="true" />
            Stale data warnings
          </CardTitle>
          <CardDescription>Warnings remain explicit before integration automation can resolve source freshness.</CardDescription>
        </CardHeader>
        <CardContent>
          <WarningList warnings={vm.privateAssets.flatMap((asset) => asset.warnings).filter((warning) => warning.label === "stale NAV" || warning.label === "missing source document")} />
        </CardContent>
      </Card>

      <AssetDetailSheet
        open={detailOpen}
        onOpenChange={setDetailOpen}
        title={vm.detailDrawerTitle}
        description={vm.detailDrawerDescription}
        asset={vm.selectedAsset}
        detail={vm.selectedDetail}
      />
    </section>
  );
}

function SummaryCard({ card }: { card: AlternativesSummaryCard }) {
  return (
    <Card className="panel-surface border-border/80" aria-label={card.ariaLabel}>
      <CardHeader className="space-y-3">
        <div className="flex items-start justify-between gap-3">
          <Landmark className="mt-1 h-4 w-4 text-primary" aria-hidden="true" />
          <Badge variant={toneBadgeVariant[card.tone]}>{card.tone === "default" ? "Tracked" : card.tone}</Badge>
        </div>
        <div>
          <CardTitle className="text-sm font-semibold text-foreground">{card.label}</CardTitle>
          <div className="mt-2 font-mono text-2xl font-semibold text-foreground">{card.value}</div>
        </div>
      </CardHeader>
      <CardContent>
        <p className="text-sm leading-6 text-muted-foreground">{card.detail}</p>
      </CardContent>
    </Card>
  );
}

function AssetDetailSheet({
  open,
  onOpenChange,
  title,
  description,
  asset,
  detail
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  title: string;
  description: string;
  asset: PrivateAssetRecord;
  detail: ReturnType<typeof buildAlternativesScreenViewModel>["selectedDetail"];
}) {
  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent id="alternatives-asset-detail-drawer" aria-labelledby="alternatives-asset-detail-title" aria-describedby="alternatives-asset-detail-description">
        <SheetHeader>
          <SheetTitle id="alternatives-asset-detail-title">
            <Archive className="h-4 w-4 text-primary" aria-hidden="true" />
            {title}
          </SheetTitle>
          <SheetDescription id="alternatives-asset-detail-description">{description}</SheetDescription>
          <SheetCloseButton onClick={() => onOpenChange(false)} label="Close private asset detail drawer" />
        </SheetHeader>
        <SheetBody className="space-y-4">
          <section className="rounded-xl border border-border/70 bg-secondary/20 p-4" aria-label={`${asset.name} overview`}>
            <div className="eyebrow-label">{detail.subtitle}</div>
            <h2 className="mt-2 text-lg font-semibold text-foreground">{detail.title}</h2>
            <p className="mt-2 text-sm leading-6 text-muted-foreground">{detail.overview}</p>
            <div className="mt-3 flex flex-wrap gap-2">
              <Badge variant={asset.source === "import" ? "warning" : "outline"}>{asset.sourceLabel}</Badge>
              {asset.warnings.map((warning) => <Badge key={warning.id} variant={toneBadgeVariant[warning.tone]}>{warning.label}</Badge>)}
            </div>
          </section>

          <DetailSection title="Commitment summary" icon={<WalletCards className="h-4 w-4 text-primary" aria-hidden="true" />}>
            <dl className="grid gap-2 sm:grid-cols-3">
              {detail.commitmentSummary.map((item) => (
                <div key={item.id} className="rounded-md border border-border/60 bg-background/35 px-3 py-2" aria-label={item.ariaLabel}>
                  <dt className="text-xs text-muted-foreground">{item.label}</dt>
                  <dd className="mt-1 font-mono text-sm text-foreground">{item.value}</dd>
                  <dd className="mt-1 text-xs text-muted-foreground">{item.detail}</dd>
                </div>
              ))}
            </dl>
          </DetailSection>

          <DetailSection title="Valuation history" icon={<History className="h-4 w-4 text-primary" aria-hidden="true" />}>
            <TimelineList items={detail.valuationHistory} render={(item: ValuationHistoryPoint) => (
              <>
                <div className="flex items-center justify-between gap-3">
                  <span className="font-medium text-foreground">{item.asOf}</span>
                  <span className="font-mono text-foreground">{item.value}</span>
                </div>
                <p className="text-xs text-muted-foreground">{item.basis} · {item.source}</p>
                {item.warningLabel ? <Badge variant="warning">{item.warningLabel}</Badge> : null}
              </>
            )} />
          </DetailSection>

          <DetailSection title="Document/evidence panel" icon={<FileText className="h-4 w-4 text-primary" aria-hidden="true" />}>
            <TimelineList items={detail.documents} render={(item: DocumentEvidenceItem) => (
              <>
                <div className="flex items-center justify-between gap-3">
                  <span className="font-medium text-foreground">{item.label}</span>
                  <span className="font-mono text-xs text-muted-foreground">{item.status}</span>
                </div>
                <p className="text-xs text-muted-foreground">{item.detail}</p>
                {item.warningLabel ? <Badge variant="warning">{item.warningLabel}</Badge> : null}
              </>
            )} />
          </DetailSection>

          <DetailSection title="Capital activity timeline" icon={<CalendarClock className="h-4 w-4 text-primary" aria-hidden="true" />}>
            <TimelineList items={detail.capitalActivity} render={(item: CapitalActivityItem) => (
              <>
                <div className="flex items-center justify-between gap-3">
                  <span className="font-medium text-foreground">{item.date} · {item.type}</span>
                  <span className="font-mono text-foreground">{item.amount}</span>
                </div>
                <p className="text-xs text-muted-foreground">{item.status} — {item.detail}</p>
                {item.warningLabel ? <Badge variant="warning">{item.warningLabel}</Badge> : null}
              </>
            )} />
          </DetailSection>

          <DetailSection title="Stale data warnings" icon={<AlertTriangle className="h-4 w-4 text-warning" aria-hidden="true" />}>
            {detail.staleDataWarnings.length > 0 ? <WarningList warnings={detail.staleDataWarnings} /> : <p className="text-sm text-muted-foreground">No stale NAV or missing source document warning for this asset.</p>}
          </DetailSection>
        </SheetBody>
      </SheetContent>
    </Sheet>
  );
}

function DetailSection({ title, icon, children }: { title: string; icon: React.ReactNode; children: React.ReactNode }) {
  return (
    <section className="rounded-xl border border-border/70 bg-background/30 p-4" aria-label={title}>
      <h3 className="flex items-center gap-2 text-sm font-semibold text-foreground">{icon}{title}</h3>
      <div className="mt-3">{children}</div>
    </section>
  );
}

function TimelineList<T extends { id: string }>({ items, render }: { items: T[]; render: (item: T) => React.ReactNode }) {
  return (
    <ol className="space-y-2">
      {items.map((item) => (
        <li key={item.id} className="rounded-md border border-border/60 bg-secondary/20 px-3 py-2">
          {render(item)}
        </li>
      ))}
    </ol>
  );
}

function WarningPills({ warnings }: { warnings: AlternativesWarning[] }) {
  return (
    <div className="flex flex-wrap gap-1.5">
      {warnings.map((warning) => <Badge key={warning.id} variant={toneBadgeVariant[warning.tone]}>{warning.label}</Badge>)}
    </div>
  );
}

function WarningList({ warnings }: { warnings: AlternativesWarning[] }) {
  if (warnings.length === 0) {
    return <p className="text-sm text-muted-foreground">No stale data warnings are currently attached.</p>;
  }

  return (
    <ul className="grid gap-2" aria-label="Explicit stale data warning list">
      {warnings.map((warning) => (
        <li key={warning.id} className="rounded-md border border-warning/40 bg-warning/10 px-3 py-2">
          <div className="flex flex-wrap items-start justify-between gap-2">
            <Badge variant={toneBadgeVariant[warning.tone]}>{warning.label}</Badge>
            <span className="text-xs text-warning">operator warning</span>
          </div>
          <p className="mt-2 text-sm leading-6 text-muted-foreground">{warning.detail}</p>
        </li>
      ))}
    </ul>
  );
}
