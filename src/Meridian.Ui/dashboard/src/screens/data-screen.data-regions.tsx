import { RefreshCcw } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { StatusBanner } from "@/components/ui/status-banner";
import { useToast, type ToastApi } from "@/components/ui/toast";
import { cn } from "@/lib/utils";
import {
  CellActionConfirmDialog,
  CellActionTrigger,
  ContextMenu,
  useCellActions
} from "@/screens/data-screen.cell-actions";
import type { CoverageGapsViewModel } from "@/screens/data-screen.coverage-gaps.view-model";
import type { DataQualityPanelViewModel } from "@/screens/data-screen.data-quality.view-model";
import { qualityToneBadgeVariant, resultToneClass } from "@/screens/data-screen.tone-styles";

/** Reveal the provider capability matrix region in response to a contextual "compare" action. */
function revealCapabilityMatrix(toast: ToastApi, symbol?: string) {
  const target = typeof document !== "undefined" ? document.getElementById("capability-matrix-title") : null;
  if (!target) {
    toast.warning("Provider capability matrix is not on this view.");
    return;
  }
  target.scrollIntoView({ behavior: "smooth", block: "start" });
  toast.info(
    "Provider capability matrix",
    symbol ? `Comparing provider coverage relevant to ${symbol}.` : undefined
  );
}

export function CoverageGapsRegion({ panel }: { panel: CoverageGapsViewModel }) {
  const toast = useToast();
  const cellActions = useCellActions({
    toast,
    onMasterSymbol: (symbol) => void panel.requestDraft(symbol),
    onOpenCapabilityMatrix: (symbol) => revealCapabilityMatrix(toast, symbol),
    onAfterMutation: () => void panel.refresh()
  });
  return (
    <section aria-labelledby="coverage-gaps-title" className="workspace-region coverage-gaps-region">
      <Card>
        <CardHeader className="flex flex-row flex-wrap items-start justify-between gap-3">
          <div>
            <CardTitle id="coverage-gaps-title">Security master coverage</CardTitle>
            <CardDescription>
              Active symbols without a validated security-master record (rule RC001).
              {panel.model ? ` ${panel.model.summary} Last quality run: ${panel.model.runAtLabel}.` : null}
            </CardDescription>
          </div>
          <Button
            type="button"
            variant="outline"
            size="sm"
            onClick={() => void panel.refresh()}
            disabled={panel.loading}
            aria-label="Refresh security master coverage"
          >
            <RefreshCcw className="h-4 w-4" aria-hidden="true" />
            <span className="ml-1.5">{panel.loading ? "Refreshing…" : "Refresh"}</span>
          </Button>
        </CardHeader>
        <CardContent>
          {panel.error ? (
            <StatusBanner tone="danger" title="Coverage report unavailable" detail={panel.error} />
          ) : !panel.model ? (
            <p className="text-sm text-muted-foreground" role="status">Loading coverage report…</p>
          ) : panel.model.rows.length === 0 ? (
            <p className="text-sm text-muted-foreground" role="status">{panel.model.summary}</p>
          ) : (
            <ul className="grid gap-2" aria-label="Unmastered active symbols">
              {panel.model.rows.map((row) => {
                const draft = panel.drafts[row.symbol];
                const context = {
                  kind: "coverage-gap" as const,
                  symbol: row.symbol,
                  sourcesLabel: row.sourcesLabel
                };
                return (
                  <li
                    key={row.symbol}
                    className="group grid gap-1.5 rounded-[2px] text-sm transition-colors hover:bg-muted/40"
                    onContextMenu={(event) => cellActions.openFor(event, context)}
                  >
                    <div className="flex flex-wrap items-center gap-2">
                      <span className="font-mono font-semibold">{row.symbol}</span>
                      <span className="text-muted-foreground">active in {row.sourcesLabel}</span>
                      <Button
                        type="button"
                        variant="outline"
                        size="sm"
                        onClick={() => void panel.requestDraft(row.symbol)}
                        disabled={panel.draftingSymbol !== null}
                        aria-label={`Build a security-master draft for ${row.symbol}`}
                      >
                        {panel.draftingSymbol === row.symbol ? "Drafting…" : "Master this"}
                      </Button>
                      <CellActionTrigger
                        label={`Actions for ${row.symbol}`}
                        onOpen={(event) => cellActions.openFor(event, context)}
                        className="ml-auto"
                      />
                      {panel.draftErrors[row.symbol] ? (
                        <span className="text-sm text-destructive" role="alert">{panel.draftErrors[row.symbol]}</span>
                      ) : null}
                    </div>
                    {draft ? (
                      <div className="rounded-md border p-2 text-sm">
                        <div className="flex flex-wrap items-center gap-2">
                          <Badge variant={draft.resolved ? "success" : "warning"}>
                            {draft.resolved ? "Resolved" : "Manual completion required"}
                          </Badge>
                          <span className="font-semibold">{draft.displayName ?? draft.symbol}</span>
                          <span className="text-muted-foreground">
                            {draft.assetClass}
                            {draft.exchange ? ` · ${draft.exchange}` : ""}
                            {draft.currency ? ` · ${draft.currency}` : ""}
                            {" · "}{draft.provenance}
                          </span>
                        </div>
                        <ul className="mt-1.5 grid gap-0.5" aria-label={`Draft identifiers for ${row.symbol}`}>
                          {draft.identifiers.map((identifier) => (
                            <li key={`${identifier.kind}:${identifier.value}`} className="font-mono text-xs text-muted-foreground">
                              {identifier.kind}: {identifier.value}
                              {identifier.isPrimary ? " (primary)" : ""}
                            </li>
                          ))}
                        </ul>
                        {draft.notes ? (
                          <p className="mt-1.5 text-xs text-muted-foreground">{draft.notes}</p>
                        ) : null}
                      </div>
                    ) : null}
                  </li>
                );
              })}
            </ul>
          )}
        </CardContent>
      </Card>
      <ContextMenu
        open={cellActions.menu.open}
        position={cellActions.menu.position}
        items={cellActions.menu.items}
        onClose={cellActions.menu.close}
        label={cellActions.menu.label}
      />
      <CellActionConfirmDialog
        confirm={cellActions.confirm}
        running={cellActions.running}
        onResolve={cellActions.resolveConfirm}
      />
    </section>
  );
}

export function DataQualityRegion({ panel }: { panel: DataQualityPanelViewModel }) {
  const toast = useToast();
  const cellActions = useCellActions({
    toast,
    onOpenCapabilityMatrix: (symbol) => revealCapabilityMatrix(toast, symbol),
    onAfterMutation: () => void panel.refresh()
  });
  return (
    <section aria-labelledby="data-quality-title" className="workspace-region data-quality-region">
      <Card>
        <CardHeader className="flex flex-row flex-wrap items-start justify-between gap-3">
          <div>
            <CardTitle id="data-quality-title">Data quality</CardTitle>
            <CardDescription>
              Unified completeness, freshness, gap, and anomaly posture across tracked symbols.
              {panel.model ? ` ${panel.model.summary}` : null}
            </CardDescription>
          </div>
          <div className="flex items-center gap-2">
            {panel.model && (
              <Badge variant={qualityToneBadgeVariant[panel.model.overallTone]} dot={panel.model.overallTone === "success"}>
                {panel.model.overallLabel}
              </Badge>
            )}
            <Button
              type="button"
              variant="outline"
              size="sm"
              onClick={() => void panel.refresh()}
              disabled={panel.loading}
              aria-label="Refresh data quality dashboard"
            >
              <RefreshCcw className="h-4 w-4" aria-hidden="true" />
              <span className="ml-1.5">{panel.loading ? "Refreshing…" : "Refresh"}</span>
            </Button>
          </div>
        </CardHeader>
        <CardContent>
          {panel.error ? (
            <StatusBanner tone="danger" title="Data quality unavailable" detail={panel.error} />
          ) : !panel.model ? (
            <p className="text-sm text-muted-foreground" role="status">Loading data quality…</p>
          ) : (
            <div className="grid gap-4">
              <div className="grid gap-2 sm:grid-cols-2 lg:grid-cols-4" role="list" aria-label="Data quality scores">
                {panel.model.scoreCards.map((card) => (
                  <div
                    key={card.id}
                    role="listitem"
                    className={cn("rounded-md border p-3 text-sm", resultToneClass[card.tone])}
                  >
                    <div className="text-xs uppercase tracking-wide">{card.label}</div>
                    <div className="mt-1 font-mono text-lg font-semibold">{card.value}</div>
                  </div>
                ))}
              </div>
              {panel.model.attentionSymbols.length > 0 && (
                <div>
                  <h3 className="text-sm font-semibold">Symbols needing attention</h3>
                  <ul className="mt-2 grid gap-1.5">
                    {panel.model.attentionSymbols.map((row) => (
                      <li key={row.symbol} className="flex flex-wrap items-center gap-2 text-sm">
                        <Badge variant={qualityToneBadgeVariant[row.tone]}>{row.health}</Badge>
                        <span className="font-mono font-semibold">{row.symbol}</span>
                        <span className="text-muted-foreground">
                          completeness {row.completenessLabel} · freshness {row.freshnessLabel} ·{" "}
                          {row.gapCount} gap{row.gapCount === 1 ? "" : "s"} · {row.anomalyCount} anomal
                          {row.anomalyCount === 1 ? "y" : "ies"}
                        </span>
                      </li>
                    ))}
                  </ul>
                </div>
              )}
              {panel.model.unacknowledgedAnomalies.length > 0 && (
                <div>
                  <h3 className="text-sm font-semibold">Unacknowledged anomalies</h3>
                  <ul className="mt-2 grid gap-1.5">
                    {panel.model.unacknowledgedAnomalies.map((anomaly) => {
                      const context = {
                        kind: "anomaly" as const,
                        symbol: anomaly.symbol,
                        anomalyId: anomaly.anomalyId,
                        anomalyType: anomaly.anomalyType
                      };
                      return (
                        <li
                          key={anomaly.anomalyId}
                          className="group flex items-center gap-2 rounded-[2px] text-sm text-muted-foreground transition-colors hover:bg-muted/40"
                          onContextMenu={(event) => cellActions.openFor(event, context)}
                        >
                          <span className="min-w-0 flex-1 truncate">
                            <span className="font-mono font-semibold text-foreground">{anomaly.symbol}</span>{" "}
                            {anomaly.anomalyType}: {anomaly.message}
                          </span>
                          <CellActionTrigger
                            label={`Actions for the ${anomaly.symbol} anomaly`}
                            onOpen={(event) => cellActions.openFor(event, context)}
                          />
                        </li>
                      );
                    })}
                  </ul>
                </div>
              )}
            </div>
          )}
        </CardContent>
      </Card>
      <ContextMenu
        open={cellActions.menu.open}
        position={cellActions.menu.position}
        items={cellActions.menu.items}
        onClose={cellActions.menu.close}
        label={cellActions.menu.label}
      />
      <CellActionConfirmDialog
        confirm={cellActions.confirm}
        running={cellActions.running}
        onResolve={cellActions.resolveConfirm}
      />
    </section>
  );
}
