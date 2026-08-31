/**
 * Data quality watch for Data → Storage assurance.
 *
 * The Data screen already renders the quality dashboard, gap list, and anomaly
 * feed. What none of them answered is the operational question underneath:
 * which symbols are unhealthy, slow, error-prone, incomplete, or silent right
 * now. Nine rollup routes answer exactly that and had no caller.
 */

import { useCallback, useEffect, useMemo, useState } from "react";
import { Activity, RefreshCcw } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { StatusBanner } from "@/components/ui/status-banner";
import { DenseDataTable, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { cn } from "@/lib/utils";
import {
  getHighLatencyQualitySymbols,
  getLowCompletenessQualitySymbols,
  getQualityCompletenessSummary,
  getQualityHealth,
  getQualityLatencyStatistics,
  getStaleQualitySymbols,
  getTopErrorQualitySymbols,
  getUnacknowledgedQualityAnomalies,
  getUnhealthyQualitySymbols
} from "@/lib/api/data-quality-watch.api";
import {
  buildCompletenessOverview,
  buildHighLatencyRow,
  buildLatencyOverview,
  buildLowCompletenessRow,
  buildQualityHeadline,
  buildStaleSymbolsViewModel,
  buildTopErrorRow,
  buildUnacknowledgedAnomalyRow,
  buildUnhealthySymbolRow,
  type LowCompletenessRowViewModel,
  type QualityTone,
  type SymbolCountRowViewModel,
  type UnacknowledgedAnomalyRowViewModel,
  type UnhealthySymbolRowViewModel
} from "@/screens/data-quality-watch.view-model";
import type {
  QualityAnomaly,
  QualityCompletenessScore,
  QualityCompletenessSummary,
  QualityHealthSnapshot,
  QualityHighLatencySymbol,
  QualityLatencyStatistics,
  QualitySymbolHealth,
  QualityTopErrorSymbol
} from "@/types/data-quality-watch.types";

/** Matches the route defaults, so the label always states the threshold in force. */
const HIGH_LATENCY_THRESHOLD_MS = 100;
const TOP_ERROR_SYMBOL_COUNT = 10;
const UNACKNOWLEDGED_ANOMALY_COUNT = 25;

export function DataQualityWatch() {
  const [health, setHealth] = useState<QualityHealthSnapshot | null>(null);
  const [unhealthy, setUnhealthy] = useState<QualitySymbolHealth[] | null>(null);
  const [latency, setLatency] = useState<QualityLatencyStatistics | null>(null);
  const [slowSymbols, setSlowSymbols] = useState<QualityHighLatencySymbol[] | null>(null);
  const [errorSymbols, setErrorSymbols] = useState<QualityTopErrorSymbol[] | null>(null);
  const [completeness, setCompleteness] = useState<QualityCompletenessSummary | null>(null);
  const [lowCompleteness, setLowCompleteness] = useState<QualityCompletenessScore[] | null>(null);
  const [anomalies, setAnomalies] = useState<QualityAnomaly[] | null>(null);
  const [staleSymbols, setStaleSymbols] = useState<string[] | null>(null);
  const [loading, setLoading] = useState(true);
  const [errors, setErrors] = useState<string[]>([]);

  const refresh = useCallback(async () => {
    setLoading(true);
    const results = await Promise.allSettled([
      getQualityHealth(),
      getUnhealthyQualitySymbols(),
      getQualityLatencyStatistics(),
      getHighLatencyQualitySymbols(HIGH_LATENCY_THRESHOLD_MS),
      getTopErrorQualitySymbols(TOP_ERROR_SYMBOL_COUNT),
      getQualityCompletenessSummary(),
      getLowCompletenessQualitySymbols(),
      getUnacknowledgedQualityAnomalies(UNACKNOWLEDGED_ANOMALY_COUNT),
      getStaleQualitySymbols()
    ]);

    const [
      healthResult, unhealthyResult, latencyResult, slowResult, errorResult,
      completenessResult, lowCompletenessResult, anomalyResult, staleResult
    ] = results;

    setHealth(valueOrNull(healthResult));
    setUnhealthy(valueOrNull(unhealthyResult));
    setLatency(valueOrNull(latencyResult));
    setSlowSymbols(valueOrNull(slowResult));
    setErrorSymbols(valueOrNull(errorResult));
    setCompleteness(valueOrNull(completenessResult));
    setLowCompleteness(valueOrNull(lowCompletenessResult));
    setAnomalies(valueOrNull(anomalyResult));
    setStaleSymbols(valueOrNull(staleResult));

    // Nine independent rollups: one can be unavailable while the rest answer,
    // and naming which failed is the difference between a gap and an outage.
    const labels = [
      "Health", "Unhealthy symbols", "Latency statistics", "High latency", "Top error symbols",
      "Completeness summary", "Low completeness", "Unacknowledged anomalies", "Stale symbols"
    ];
    setErrors(results.flatMap((result, index) => (
      result.status === "rejected" ? [`${labels[index]}: ${errorMessage(result.reason)}`] : []
    )));
    setLoading(false);
  }, []);

  useEffect(() => { void refresh(); }, [refresh]);

  const headline = useMemo(() => buildQualityHeadline(health), [health]);
  const unhealthyRows = useMemo(() => (unhealthy ?? []).map(buildUnhealthySymbolRow), [unhealthy]);
  const latencyView = useMemo(() => buildLatencyOverview(latency), [latency]);
  const slowRows = useMemo(
    () => (slowSymbols ?? []).map((entry) => buildHighLatencyRow(entry, HIGH_LATENCY_THRESHOLD_MS)),
    [slowSymbols]
  );
  const errorRows = useMemo(() => (errorSymbols ?? []).map(buildTopErrorRow), [errorSymbols]);
  const completenessView = useMemo(() => buildCompletenessOverview(completeness), [completeness]);
  const lowCompletenessRows = useMemo(
    () => (lowCompleteness ?? []).map(buildLowCompletenessRow),
    [lowCompleteness]
  );
  const anomalyRows = useMemo(
    () => (anomalies ?? []).map(buildUnacknowledgedAnomalyRow),
    [anomalies]
  );
  const staleView = useMemo(() => buildStaleSymbolsViewModel(staleSymbols), [staleSymbols]);

  return (
    <Card className="panel-surface">
      <CardHeader>
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div>
            <div className="eyebrow-label">Data quality</div>
            <CardTitle className="flex items-center gap-2">
              <Activity className="h-5 w-5 text-primary" />
              Quality watch
            </CardTitle>
            <CardDescription>
              Which symbols are unhealthy, slow, error-prone, incomplete, or silent right now.
            </CardDescription>
          </div>
          <Button size="sm" variant="outline" onClick={() => void refresh()} disabled={loading}>
            <RefreshCcw className="mr-2 h-4 w-4" />
            Refresh
          </Button>
        </div>
      </CardHeader>
      <CardContent className="space-y-4">
        <dl className="flex flex-wrap items-baseline gap-x-6 gap-y-1.5 text-xs">
          <Stat label="Status" value={headline.statusLabel} tone={headline.statusTone} />
          <Stat label="Score" value={headline.scoreLabel} />
          <Stat label="Symbols with issues" value={headline.symbolsWithIssuesLabel} />
          <Stat label="Latency" value={latencyView.headlineLabel} />
          <Stat label="Coverage" value={completenessView.coverageLabel} />
          <Stat label="Stale" value={staleView.label} />
        </dl>
        <p className="text-xs text-muted-foreground">
          {headline.recentActivityLabel} · as of {headline.asOfLabel}
        </p>

        {errors.length > 0 ? (
          <StatusBanner
            role="alert"
            tone={health === null ? "danger" : "warning"}
            title={health === null ? "Quality watch unavailable" : "Quality watch loaded with gaps"}
            detail={(
              <ul className="mt-2 list-disc pl-5">
                {errors.map((error) => <li key={error}>{error}</li>)}
              </ul>
            )}
          />
        ) : null}
        {staleView.notice ? (
          <StatusBanner role="status" tone="warning" title="Symbols have stopped reporting" detail={staleView.notice} />
        ) : null}

        <Section title="Unhealthy symbols" count={unhealthyRows.length}>
          <DenseDataTable
            columns={unhealthyColumns}
            rows={unhealthyRows}
            getRowId={(row) => row.symbol}
            getRowAriaLabel={(row) => row.ariaLabel}
            emptyText={loading ? "Loading symbol health…" : "No symbol is reporting an unhealthy state."}
            ariaLabel="Unhealthy symbols"
            caption="Symbols the quality service scores below healthy, with the issues it attributes to each."
          />
        </Section>

        <div className="grid gap-4 lg:grid-cols-2">
          <Section
            title={`Slowest symbols (p99 over ${HIGH_LATENCY_THRESHOLD_MS}ms)`}
            count={slowRows.length}
            note={latencyView.spreadLabel}
          >
            <DenseDataTable
              columns={symbolCountColumns("p99 latency")}
              rows={slowRows}
              getRowId={(row) => row.symbol}
              emptyText={loading ? "Loading latency…" : `No symbol exceeds ${HIGH_LATENCY_THRESHOLD_MS}ms at p99.`}
              ariaLabel="Symbols above the latency threshold"
              caption={latencyView.extremesLabel}
            />
          </Section>

          <Section title="Noisiest symbols" count={errorRows.length} note={latencyView.sampleLabel}>
            <DenseDataTable
              columns={symbolCountColumns("Sequence errors")}
              rows={errorRows}
              getRowId={(row) => row.symbol}
              emptyText={loading ? "Loading sequence errors…" : "No symbol has recorded a sequence error."}
              ariaLabel="Symbols with the most sequence errors"
              caption={`Top ${TOP_ERROR_SYMBOL_COUNT} symbols by retained sequence-error count.`}
            />
          </Section>
        </div>

        <Section
          title="Lowest completeness"
          count={lowCompletenessRows.length}
          note={`Average ${completenessView.averageScoreLabel} across ${completenessView.trackedLabel} · grades ${completenessView.gradeLabel}`}
        >
          <DenseDataTable
            columns={completenessColumns}
            rows={lowCompletenessRows}
            getRowId={(row) => `${row.symbol}:${row.date}`}
            getRowAriaLabel={(row) => row.ariaLabel}
            emptyText={loading ? "Loading completeness…" : "Every tracked symbol is above the completeness threshold."}
            ariaLabel="Symbols below the completeness threshold"
            caption={`Score range across tracked symbol-dates: ${completenessView.spreadLabel}.`}
          />
        </Section>

        <Section title="Unacknowledged anomalies" count={anomalyRows.length}>
          <DenseDataTable
            columns={anomalyColumns}
            rows={anomalyRows}
            getRowId={(row) => row.id}
            getRowAriaLabel={(row) => row.ariaLabel}
            emptyText={loading ? "Loading anomalies…" : "Every detected anomaly has been acknowledged."}
            ariaLabel="Unacknowledged quality anomalies"
            caption={`The ${UNACKNOWLEDGED_ANOMALY_COUNT} most recent anomalies awaiting acknowledgement.`}
          />
        </Section>
      </CardContent>
    </Card>
  );
}

function Stat({ label, value, tone }: { label: string; value: string; tone?: QualityTone }) {
  return (
    <div className="flex min-w-0 items-baseline gap-2">
      <dt className="whitespace-nowrap uppercase tracking-[0.08em] text-muted-foreground">{label}</dt>
      <dd className={cn("whitespace-nowrap font-mono text-sm font-semibold", toneClass(tone ?? "default"))}>{value}</dd>
    </div>
  );
}

function Section({
  title,
  count,
  note,
  children
}: {
  title: string;
  count: number;
  note?: string;
  children: React.ReactNode;
}) {
  return (
    <div className="space-y-2">
      <div className="flex flex-wrap items-center gap-2 text-xs text-muted-foreground">
        <span className="font-medium text-foreground">{title}</span>
        <Badge variant="outline">{count}</Badge>
        {note ? <span>{note}</span> : null}
      </div>
      {children}
    </div>
  );
}

const unhealthyColumns: DenseDataTableColumn<UnhealthySymbolRowViewModel>[] = [
  {
    id: "symbol",
    label: "Symbol",
    render: (row) => <span className="font-mono text-foreground">{row.symbol}</span>
  },
  {
    id: "state",
    label: "State",
    render: (row) => <span className={cn("font-mono", toneClass(row.stateTone))}>{row.stateLabel}</span>
  },
  {
    id: "score",
    label: "Score",
    align: "right",
    render: (row) => <span className="font-mono tabular-nums text-foreground">{row.scoreLabel}</span>
  },
  {
    id: "silence",
    label: "Since last event",
    render: (row) => <span className="font-mono text-muted-foreground">{row.silenceLabel}</span>
  },
  {
    id: "issues",
    label: "Active issues",
    render: (row) => <span className="text-muted-foreground">{row.issues}</span>
  }
];

function symbolCountColumns(valueLabel: string): DenseDataTableColumn<SymbolCountRowViewModel>[] {
  return [
    {
      id: "symbol",
      label: "Symbol",
      render: (row) => <span className="font-mono text-foreground">{row.symbol}</span>
    },
    {
      id: "value",
      label: valueLabel,
      align: "right",
      render: (row) => <span className={cn("font-mono tabular-nums", toneClass(row.tone))}>{row.valueLabel}</span>
    }
  ];
}

const completenessColumns: DenseDataTableColumn<LowCompletenessRowViewModel>[] = [
  {
    id: "symbol",
    label: "Symbol",
    render: (row) => (
      <div className="space-y-1">
        <div className="font-mono text-foreground">{row.symbol}</div>
        <div className="font-mono text-xs text-muted-foreground">{row.date}</div>
      </div>
    )
  },
  {
    id: "score",
    label: "Completeness",
    align: "right",
    render: (row) => (
      <div className="space-y-1">
        <div className={cn("font-mono tabular-nums", toneClass(row.tone))}>{row.scoreLabel}</div>
        <div className="font-mono text-xs text-muted-foreground">grade {row.grade}</div>
      </div>
    )
  },
  {
    id: "missing",
    label: "Missing events",
    align: "right",
    render: (row) => <span className="font-mono tabular-nums text-muted-foreground">{row.missingLabel}</span>
  },
  {
    id: "coverage",
    label: "Time covered",
    align: "right",
    render: (row) => <span className="font-mono tabular-nums text-muted-foreground">{row.coverageLabel}</span>
  }
];

const anomalyColumns: DenseDataTableColumn<UnacknowledgedAnomalyRowViewModel>[] = [
  {
    id: "symbol",
    label: "Symbol",
    render: (row) => <span className="font-mono text-foreground">{row.symbol}</span>
  },
  {
    id: "type",
    label: "Anomaly",
    render: (row) => (
      <div className="space-y-1">
        <div className="text-foreground">{row.typeLabel}</div>
        <div className="text-xs text-muted-foreground">{row.description}</div>
      </div>
    )
  },
  {
    id: "severity",
    label: "Severity",
    render: (row) => <span className={cn("font-mono", toneClass(row.severityTone))}>{row.severityLabel}</span>
  },
  {
    id: "deviation",
    label: "Deviation",
    align: "right",
    render: (row) => <span className="font-mono tabular-nums text-muted-foreground">{row.deviationLabel}</span>
  },
  {
    id: "detected",
    label: "Detected",
    render: (row) => <span className="font-mono text-muted-foreground">{row.detectedAt}</span>
  }
];

function toneClass(tone: QualityTone): string {
  if (tone === "danger") {
    return "text-destructive";
  }

  if (tone === "warning") {
    return "text-warning";
  }

  return tone === "success" ? "text-success" : "text-foreground";
}

function valueOrNull<T>(result: PromiseSettledResult<T>): T | null {
  return result.status === "fulfilled" ? result.value : null;
}

function errorMessage(reason: unknown): string {
  return reason instanceof Error ? reason.message : "Request failed.";
}
