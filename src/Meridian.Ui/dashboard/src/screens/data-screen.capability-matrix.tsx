import { TableWorkbench, type TableWorkbenchColumn } from "@/components/meridian/table-workbench";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { PanelReload } from "@/components/ui/panel-reload";
import { StatusBanner } from "@/components/ui/status-banner";
import { StatusBadge } from "@/design-system/status";
import { FixedIncomeCapabilityReadiness } from "./data-screen.fixed-income-capabilities";
import {
  CAPABILITY_LEGEND,
  type CapabilityFreshnessModel,
  type CapabilityMatrixRowModel,
  type CapabilityMatrixViewModel
} from "./data-screen.capability-matrix.view-model";

const FRESHNESS_SEVERITY: Record<CapabilityFreshnessModel["state"], string> = { current: "ready", stale: "stale", "no-evidence": "unknown" };

function declaredSupport(value?: boolean) {
  return value === undefined ? "Unavailable" : value ? "Yes" : "No";
}

function declaredDepth(row: CapabilityMatrixRowModel) {
  const declaration = row.capabilities;
  if (!declaration?.supportsMarketDepth) return declaredSupport(declaration?.supportsMarketDepth);
  return declaration.maxDepthLevels == null ? "Unlimited" : declaration.maxDepthLevels.toString();
}

const declaredCapabilityColumns: TableWorkbenchColumn<CapabilityMatrixRowModel>[] = [
  { id: "market-depth", label: "Depth levels", render: declaredDepth, exportValue: declaredDepth },
  ...([
    ["supportsAdjustedPrices", "Adjusted bars"],
    ["supportsHistoricalTrades", "Historical trades"],
    ["supportsHistoricalQuotes", "Historical quotes"],
    ["supportsIntraday", "Intraday"]
  ] as const).map(([id, label]) => ({
    id, label,
    render: (row: CapabilityMatrixRowModel) => declaredSupport(row.capabilities?.[id]),
    exportValue: (row: CapabilityMatrixRowModel) => declaredSupport(row.capabilities?.[id])
  }))
];

export function CapabilityMatrixRegion({ panel, freshness }: { panel: CapabilityMatrixViewModel; freshness: (providerId: string) => CapabilityFreshnessModel }) {
  return (
    <section aria-labelledby="capability-matrix-title" className="workspace-region capability-matrix-region">
      <Card>
        <CardHeader className="flex flex-row flex-wrap items-start justify-between gap-3">
          <div>
            <CardTitle id="capability-matrix-title">Provider capability matrix</CardTitle>
            <CardDescription>
              Declared provider coverage per instrument type, with how fresh each provider's newest evidence is.
              {panel.model ? ` ${panel.model.summary}` : null}
            </CardDescription>
          </div>
          <PanelReload reload={panel.refresh} failed={Boolean(panel.error)} busy={panel.loading} what="provider capability matrix" />
        </CardHeader>
        <CardContent>
          {panel.error ? (
            <StatusBanner tone="danger" title="Capability matrix unavailable" detail={panel.error} />
          ) : !panel.model ? (
            <p className="text-sm text-muted-foreground" role="status">Loading capability matrix…</p>
          ) : (
            <div className="grid gap-4">
              <FixedIncomeCapabilityReadiness rows={panel.model.rows} />
              <div className="overflow-x-auto">
                <TableWorkbench workbenchId="data-provider-capabilities" sourceKey={panel.model.rows.map((row) => row.providerId).join("|")}
                  sourceDescription="Loaded declared provider capability matrix; discovery failures are listed below."
                  completePopulation={panel.model.failures.length === 0}
                  rows={panel.model.rows} getRowId={(row) => row.providerId} getSearchText={(row) => `${row.providerId} ${row.cells.map((cell) => cell.description).join(" ")}`}
                  columns={[{ id: "provider", label: "Provider", rowHeader: true, render: (row) => <span className="font-mono font-semibold">{row.providerId}</span>, exportValue: (row) => row.providerId },
                    { id: "freshness", label: "Freshness", render: (row: CapabilityMatrixRowModel) => { const fresh = freshness(row.providerId); return (
                      <span className="inline-flex flex-wrap items-center gap-1.5" data-freshness={fresh.state}>
                        <StatusBadge status={FRESHNESS_SEVERITY[fresh.state]} label={fresh.label} />
                        {fresh.ageText ? <span className="text-xs tabular-nums text-muted-foreground">{fresh.ageText}</span> : null}
                      </span>); },
                      exportValue: (row: CapabilityMatrixRowModel) => { const fresh = freshness(row.providerId); return fresh.ageText ? `${fresh.label} (${fresh.ageText})` : fresh.label; } },
                    ...panel.model.columns.map((column) => ({ id: column, label: column,
                      render: (row: CapabilityMatrixRowModel) => { const cell = row.cells.find((item) => item.instrumentType === column); return cell ? <span aria-label={cell.description} title={cell.description}>{cell.marks}</span> : "Unavailable"; },
                      exportValue: (row: CapabilityMatrixRowModel) => row.cells.find((cell) => cell.instrumentType === column)?.description ?? "Unavailable" })),
                    ...declaredCapabilityColumns]}
                  ariaLabel="Provider capability by instrument type" emptyText="No declared capabilities." maxVisibleRows={null} />
              </div>
              <p className="text-xs text-muted-foreground">
                {CAPABILITY_LEGEND.map((mark) => `${mark.code} = ${mark.label}`).join(" · ")}
              </p>
              <p className="text-xs text-muted-foreground">Depth, adjusted bars, historical trades and quotes, and intraday coverage are adapter declarations.</p>
              {panel.model.failures.length > 0 && (
                <div>
                  <h3 className="text-sm font-semibold">Discovery failures</h3>
                  <ul className="mt-2 grid gap-1.5">
                    {panel.model.failures.map((failure, index) => (
                      <li key={`${failure.stage}-${failure.subject}-${index}`} className="text-sm text-muted-foreground">
                        <Badge variant="danger">{failure.stage}</Badge>{" "}
                        <span className="font-mono">{failure.subject}</span> — {failure.errorType}: {failure.errorMessage}
                      </li>
                    ))}
                  </ul>
                </div>
              )}
            </div>
          )}
        </CardContent>
      </Card>
    </section>
  );
}
