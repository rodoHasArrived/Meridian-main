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
  type CellActionApi,
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

export function DataQualityRegion({
  panel,
  actionApi
}: {
  panel: DataQualityPanelViewModel;
  actionApi?: Partial<CellActionApi>;
}) {
  const toast = useToast();
  const cellActions = useCellActions({
    toast,
    onOpenCapabilityMatrix: (symbol) => revealCapabilityMatrix(toast, symbol),
    onAfterMutation: () => void panel.refresh(),
    ...(actionApi ? { api: actionApi } : {})
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
                    title={card.detail}
                  >
                    <div className="text-xs uppercase tracking-wide">{card.label}</div>
                    <div className="mt-1 font-mono text-lg font-semibold">{card.value}</div>
                    <div className="mt-1 text-xs opacity-80">{card.detail}</div>
                  </div>
                ))}
              </div>
              {panel.model.isPartial ? (
                <StatusBanner
                  tone="warning"
                  title="Partial quality evidence"
                  detail="Missing or stale source signals are shown explicitly and cannot produce a Green posture."
                />
              ) : null}
              {panel.model.symbols.length > 0 && (
                <div>
                  <h3 className="text-sm font-semibold">Collected symbols</h3>
                  <div className="mt-2 overflow-x-auto rounded-md border">
                    <div className="min-w-[760px] divide-y" role="list" aria-label="Composite quality by symbol">
                      {panel.model.symbols.map((row) => (
                        <details key={row.symbol} className="group text-sm" role="listitem">
                          <summary className="grid cursor-pointer list-none grid-cols-[7rem_7rem_1fr_auto] items-center gap-3 px-3 py-2 hover:bg-muted/40 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40">
                            <span className="font-mono font-semibold">{row.symbol}</span>
                            <span className="flex items-center gap-2">
                              <Badge variant={qualityToneBadgeVariant[row.tone]}>{row.status}</Badge>
                              <span className="font-mono">{row.scoreLabel}</span>
                            </span>
                            <span className="text-muted-foreground">
                              stored {row.completenessLabel} · streaming {row.freshnessLabel} · adapter {row.adapterLabel}
                            </span>
                            <span className="text-xs text-muted-foreground">{row.coverageLabel}</span>
                          </summary>
                          <div className="grid gap-3 border-t bg-muted/20 px-4 py-3 lg:grid-cols-2">
                            <div>
                              <h4 className="font-semibold">Expected-session evidence</h4>
                              <p className="mt-1 text-muted-foreground">{row.expectedEventsLabel}</p>
                              <ul className="mt-2 grid gap-1" aria-label={`${row.symbol} quality components`}>
                                {row.components.map((component) => (
                                  <li key={component.kind} className="flex items-start justify-between gap-3">
                                    <span>
                                      <span className="font-medium">{component.label}</span>{" "}
                                      <span className="text-muted-foreground">({component.availability})</span>
                                      <span className="block text-xs text-muted-foreground">{component.detail}</span>
                                    </span>
                                    <span className="font-mono">{component.score === null ? "—" : component.score.toFixed(1)}</span>
                                  </li>
                                ))}
                              </ul>
                            </div>
                            <div>
                              <h4 className="font-semibold">Provider freshness</h4>
                              {row.providerFreshness.length === 0 ? (
                                <p className="mt-1 text-muted-foreground">No provider-specific observation is available.</p>
                              ) : (
                                <ul className="mt-1 grid gap-1" aria-label={`${row.symbol} provider freshness`}>
                                  {row.providerFreshness.map((provider) => (
                                    <li key={provider.provider} className="flex justify-between gap-3">
                                      <span className="font-mono">{provider.provider}</span>
                                      <span className="text-muted-foreground">
                                        {provider.status} · {provider.lastEventAt ? new Date(provider.lastEventAt).toLocaleString() : "unmeasured"}
                                      </span>
                                    </li>
                                  ))}
                                </ul>
                              )}
                              <p className="mt-2 text-muted-foreground">
                                {row.gapCount} open gap{row.gapCount === 1 ? "" : "s"} · {row.anomalyCount} anomal
                                {row.anomalyCount === 1 ? "y" : "ies"}
                              </p>
                            </div>
                            {row.openGaps.length > 0 ? (
                              <div className="lg:col-span-2">
                                <h4 className="font-semibold">Open gaps</h4>
                                <ul className="mt-1 grid gap-1.5" aria-label={`${row.symbol} open gaps`}>
                                  {row.openGaps.map((gap) => {
                                    const disabledReason = !gap.canBackfill
                                      ? gap.disabledReason ?? "Server policy has disabled remediation for this gap."
                                      : null;
                                    const disabledReasonId = `quality-gap-${gap.gapId.replace(/[^a-zA-Z0-9_-]/g, "-")}-disabled-reason`;
                                    const context = {
                                      kind: "quality-gap" as const,
                                      symbol: gap.symbol,
                                      gapId: gap.gapId,
                                      dashboardVersion: panel.model?.dashboardVersion ?? "",
                                      provider: gap.provider,
                                      from: gap.from,
                                      to: gap.to,
                                      canBackfill: gap.canBackfill,
                                      disabledReason: gap.disabledReason
                                    };
                                    return (
                                      <li
                                        key={gap.gapId}
                                        className="group flex flex-wrap items-center gap-2 rounded-sm border bg-background px-2 py-1.5"
                                        onContextMenu={(event) => cellActions.openFor(event, context)}
                                      >
                                        <Badge variant="warning">{gap.severity}</Badge>
                                        <span className="font-mono">{gap.eventType}</span>
                                        <span className="text-xs text-muted-foreground">
                                          Gap <span className="font-mono text-foreground">{gap.gapId}</span>
                                          {" · "}{gap.provider ?? "Default provider"}
                                        </span>
                                        <span className="min-w-0 flex-1 text-muted-foreground">
                                          {new Date(gap.from).toLocaleString()} – {new Date(gap.to).toLocaleString()} ·{" "}
                                          {gap.estimatedMissingEvents.toLocaleString()} estimated missing
                                        </span>
                                        <Button
                                          type="button"
                                          variant="outline"
                                          size="sm"
                                          disabled={!gap.canBackfill || cellActions.running}
                                          title={disabledReason ?? "Backfill this exact provider and date range"}
                                          aria-label={disabledReason
                                            ? `Backfill gap ${gap.gapId} unavailable: ${disabledReason}`
                                            : `Backfill gap ${gap.gapId} for ${gap.symbol}`}
                                          aria-describedby={disabledReason ? disabledReasonId : undefined}
                                          onClick={() => cellActions.run(context, "backfill")}
                                        >
                                          {cellActions.running ? "Working…" : "Backfill gap"}
                                        </Button>
                                        <CellActionTrigger
                                          label={`Actions for the ${gap.symbol} quality gap`}
                                          onOpen={(event) => cellActions.openFor(event, context)}
                                        />
                                        {disabledReason ? (
                                          <span
                                            id={disabledReasonId}
                                            className="basis-full text-xs text-muted-foreground"
                                          >
                                            Remediation unavailable: {disabledReason}
                                          </span>
                                        ) : null}
                                      </li>
                                    );
                                  })}
                                </ul>
                              </div>
                            ) : null}
                            {row.issues.length > 0 ? (
                              <div className="lg:col-span-2">
                                <h4 className="font-semibold">Current issues</h4>
                                <ul className="mt-1 list-disc pl-5 text-muted-foreground">
                                  {row.issues.map((issue) => <li key={issue}>{issue}</li>)}
                                </ul>
                              </div>
                            ) : null}
                          </div>
                        </details>
                      ))}
                    </div>
                  </div>
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
                        anomalyId: anomaly.id,
                        anomalyType: `Type ${anomaly.type}`
                      };
                      return (
                        <li
                          key={anomaly.id}
                          className="group flex items-center gap-2 rounded-[2px] text-sm text-muted-foreground transition-colors hover:bg-muted/40"
                          onContextMenu={(event) => cellActions.openFor(event, context)}
                        >
                          <span className="min-w-0 flex-1 truncate">
                            <span className="font-mono font-semibold text-foreground">{anomaly.symbol}</span>{" "}
                            Type {anomaly.type}: {anomaly.description}
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
