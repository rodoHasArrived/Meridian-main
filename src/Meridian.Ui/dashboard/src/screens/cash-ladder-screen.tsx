import { useEffect, useRef, useState } from "react";
import { Link } from "react-router-dom";
import { ArrowLeft, Layers3 } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { MetricCard, EmptyState } from "@/components/data/concrete";
import { DenseDataTable, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { getPortfolioCashLadder } from "@/lib/api/portfolio-cash-ladder.api";
import { WORKSTATION_ROUTE_CATALOG } from "@/lib/workspace";
import { cn } from "@/lib/utils";
import {
  buildCashLadderChart,
  selectActiveScenario,
  selectBucketContributions,
  selectScenarioOptions,
  selectSummaryCards,
  CASH_LADDER_CHART_HEIGHT,
  CASH_LADDER_CHART_PADDING,
  CASH_LADDER_CHART_WIDTH,
  CASH_LADDER_SOURCE_ORDER,
  sourcePaletteIndex,
  type CashLadderDrillRow
} from "@/screens/cash-ladder-screen.view-model";
import type { PortfolioCashLadder } from "@/types/portfolio-cash-ladder.types";

const SOURCE_PALETTE = [
  "var(--chart-series-1, #16885F)",
  "var(--chart-series-2, #2F6F8F)",
  "var(--chart-series-3, #7C5CBF)",
  "var(--chart-series-4, #B08A2E)",
  "var(--chart-series-5, #BA3F55)",
  "var(--chart-series-6, #8A520E)",
  "var(--chart-series-7, #59636F)"
] as const;

const GRID = "var(--chart-grid, #CBD3DC)";
const AXIS = "var(--chart-axis, #59636F)";
const BORDER = "var(--chart-border, #99A5B2)";
const CUMULATIVE = "var(--chart-equity, #16885F)";
const THRESHOLD = "var(--chart-warning, #8A520E)";

const metricToneMap = {
  default: "neutral",
  success: "success",
  warning: "warning",
  danger: "danger"
} as const;

const drillColumns: DenseDataTableColumn<CashLadderDrillRow>[] = [
  {
    id: "instrument",
    label: "Instrument",
    render: (row) => (
      <div className="flex flex-col">
        <span className="font-semibold text-foreground">{row.instrument}</span>
        <span className="text-xs text-muted-foreground">{row.assetClass}</span>
      </div>
    )
  },
  {
    id: "flow",
    label: "Flow",
    render: (row) => (
      <div className="flex flex-col">
        <span className="text-foreground">{row.flowType}</span>
        <span className="text-xs text-muted-foreground">{row.sourceLane}</span>
      </div>
    )
  },
  {
    id: "due-date",
    label: "Due",
    render: (row) => <span className="font-mono text-foreground">{row.dueDate}</span>
  },
  {
    id: "amount",
    label: "Amount",
    align: "right",
    render: (row) => (
      <span className={cn("font-mono", row.amountTone === "success" ? "text-success" : "text-danger")}>
        {row.amountLabel}
      </span>
    )
  },
  {
    id: "currency",
    label: "Ccy",
    render: (row) => <span className="font-mono text-muted-foreground">{row.currency}</span>
  },
  {
    id: "projection-run",
    label: "Projection run",
    render: (row) => (
      <div className="flex flex-col">
        <span className="font-mono text-foreground">{row.projectionRunId}</span>
        <span className="text-xs text-muted-foreground">{row.projectionEngineVersion}</span>
      </div>
    )
  },
  {
    id: "terms-version",
    label: "Terms version",
    render: (row) => <span className="font-mono text-xs text-muted-foreground">{row.termsVersion}</span>
  },
  {
    id: "source",
    label: "Source",
    render: (row) => <span className="text-xs text-muted-foreground">{row.sourceDomain}</span>
  },
  {
    id: "scenario",
    label: "Scenario effect",
    render: (row) =>
      row.scenarioAdjustment ? (
        <span className="block max-w-64 truncate text-xs text-warning" title={row.scenarioAdjustment}>
          {row.scenarioAdjustment}
        </span>
      ) : (
        <span className="text-xs text-muted-foreground">—</span>
      )
  }
];

export interface CashLadderScreenProps {
  fundAccountId?: string;
}

export function CashLadderScreen({ fundAccountId }: CashLadderScreenProps = {}) {
  const [ladder, setLadder] = useState<PortfolioCashLadder | null>(null);
  const [scenarioId, setScenarioId] = useState("base");
  const [minimumCashInput, setMinimumCashInput] = useState("");
  const [appliedMinimumCash, setAppliedMinimumCash] = useState<number | undefined>(undefined);
  const [selectedBucket, setSelectedBucket] = useState<number | null>(null);
  const [status, setStatus] = useState<"loading" | "ready" | "error">("loading");
  const requestId = useRef(0);

  useEffect(() => {
    const currentRequest = ++requestId.current;
    setStatus("loading");
    getPortfolioCashLadder({ scenario: scenarioId, minimumCash: appliedMinimumCash, fundAccountId })
      .then((response) => {
        if (requestId.current === currentRequest) {
          setLadder(response);
          setStatus("ready");
        }
      })
      .catch(() => {
        if (requestId.current === currentRequest) {
          setStatus("error");
        }
      });
  }, [scenarioId, appliedMinimumCash, fundAccountId]);

  const chart = buildCashLadderChart(ladder);
  const scenarioOptions = selectScenarioOptions(ladder);
  const activeScenario = selectActiveScenario(ladder);
  const summaryCards = selectSummaryCards(ladder);
  const drillRows = selectBucketContributions(ladder, selectedBucket);
  const selectedBucketLabel = selectedBucket !== null && ladder && selectedBucket < ladder.buckets.length
    ? ladder.buckets[selectedBucket].label
    : null;

  function applyMinimumCash() {
    const parsed = Number(minimumCashInput);
    setAppliedMinimumCash(minimumCashInput.trim() !== "" && Number.isFinite(parsed) ? parsed : undefined);
  }

  return (
    <div className="space-y-4 p-4" data-testid="cash-ladder-screen">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex items-center gap-3">
          <Layers3 className="size-5 text-muted-foreground" aria-hidden />
          <div>
            <h1 className="text-lg font-semibold text-foreground">Cash Ladder</h1>
            <p className="text-sm text-muted-foreground">
              Projected portfolio inflows and outflows by source, joined with opening cash and capital activity.
            </p>
          </div>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          {ladder ? <Badge variant="outline">{ladder.engineVersion}</Badge> : null}
          <label className="flex items-center gap-2 text-sm text-muted-foreground">
            Scenario
            <select
              className="rounded border bg-background px-2 py-1 text-sm text-foreground"
              value={scenarioId}
              onChange={(event) => {
                setScenarioId(event.target.value);
                setSelectedBucket(null);
              }}
              aria-label="Stress scenario"
            >
              {scenarioOptions.map((option) => (
                <option key={option.scenarioId} value={option.scenarioId}>
                  {option.displayName}
                </option>
              ))}
            </select>
          </label>
          <label className="flex items-center gap-2 text-sm text-muted-foreground">
            Min balance
            <input
              type="number"
              inputMode="decimal"
              className="w-28 rounded border bg-background px-2 py-1 text-sm text-foreground"
              placeholder="0"
              value={minimumCashInput}
              onChange={(event) => setMinimumCashInput(event.target.value)}
              onBlur={applyMinimumCash}
              onKeyDown={(event) => {
                if (event.key === "Enter") {
                  applyMinimumCash();
                }
              }}
              aria-label="Minimum cash balance threshold"
            />
          </label>
          <Link
            to={WORKSTATION_ROUTE_CATALOG.portfolio}
            className="inline-flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground"
          >
            <ArrowLeft className="size-4" aria-hidden /> Portfolio
          </Link>
        </div>
      </div>

      {status === "error" ? (
        <Card>
          <CardContent className="py-8">
            <EmptyState
              icon="inbox"
              title="Cash ladder unavailable"
              detail="The cash ladder request failed. Verify the workstation API is reachable and retry."
            />
          </CardContent>
        </Card>
      ) : null}

      {status !== "error" ? (
        <>
          <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
            {summaryCards.map((card) => (
              <MetricCard
                key={card.id}
                label={card.label}
                value={card.value}
                delta={card.detail}
                tone={metricToneMap[card.tone]}
              />
            ))}
          </div>

          <Card>
            <CardHeader className="pb-2">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <div>
                  <CardTitle>Projected cash by bucket</CardTitle>
                  <CardDescription>
                    {ladder
                      ? `${ladder.horizonDays}-day horizon from ${ladder.asOfDate} · scenario: ${activeScenario?.displayName ?? ladder.scenarioId}`
                      : "Loading projection…"}
                  </CardDescription>
                </div>
                <div className="flex flex-wrap items-center gap-3 text-xs text-muted-foreground">
                  {CASH_LADDER_SOURCE_ORDER.map((source) => (
                    <span key={source} className="inline-flex items-center gap-1.5">
                      <span
                        className="inline-block size-2.5 rounded-sm"
                        style={{ backgroundColor: SOURCE_PALETTE[sourcePaletteIndex(source)] }}
                        aria-hidden
                      />
                      {source}
                    </span>
                  ))}
                  <span className="inline-flex items-center gap-1.5">
                    <span className="inline-block h-0.5 w-4" style={{ backgroundColor: CUMULATIVE }} aria-hidden />
                    Cumulative cash
                  </span>
                </div>
              </div>
            </CardHeader>
            <CardContent>
              {chart ? (
                <svg
                  viewBox={`0 0 ${CASH_LADDER_CHART_WIDTH} ${CASH_LADDER_CHART_HEIGHT}`}
                  className="w-full"
                  role="img"
                  aria-label="Portfolio cash ladder: stacked projected flows per bucket with cumulative cash line and minimum-balance band"
                >
                  {chart.yTicks.map((tick) => (
                    <g key={tick.y}>
                      <line
                        x1={CASH_LADDER_CHART_PADDING.left}
                        x2={CASH_LADDER_CHART_WIDTH - CASH_LADDER_CHART_PADDING.right}
                        y1={tick.y}
                        y2={tick.y}
                        stroke={GRID}
                        strokeWidth="0.8"
                        opacity="0.55"
                      />
                      <text x={CASH_LADDER_CHART_PADDING.left - 8} y={tick.y + 3} fill={AXIS} textAnchor="end" fontSize="10">
                        {tick.label}
                      </text>
                    </g>
                  ))}

                  {chart.thresholdBand ? (
                    <rect
                      x={CASH_LADDER_CHART_PADDING.left}
                      y={chart.thresholdBand.y}
                      width={CASH_LADDER_CHART_WIDTH - CASH_LADDER_CHART_PADDING.left - CASH_LADDER_CHART_PADDING.right}
                      height={chart.thresholdBand.height}
                      fill={THRESHOLD}
                      opacity="0.08"
                    />
                  ) : null}
                  {chart.thresholdY !== null ? (
                    <line
                      x1={CASH_LADDER_CHART_PADDING.left}
                      x2={CASH_LADDER_CHART_WIDTH - CASH_LADDER_CHART_PADDING.right}
                      y1={chart.thresholdY}
                      y2={chart.thresholdY}
                      stroke={THRESHOLD}
                      strokeWidth="1.2"
                      strokeDasharray="6 3"
                    />
                  ) : null}

                  <line
                    x1={CASH_LADDER_CHART_PADDING.left}
                    x2={CASH_LADDER_CHART_WIDTH - CASH_LADDER_CHART_PADDING.right}
                    y1={chart.zeroLineY}
                    y2={chart.zeroLineY}
                    stroke={BORDER}
                    strokeWidth="1"
                  />

                  {chart.bars.map((bar) => (
                    <g
                      key={bar.index}
                      role="button"
                      tabIndex={0}
                      aria-label={bar.ariaLabel}
                      className="cursor-pointer focus:outline-none"
                      onClick={() => setSelectedBucket(bar.index === selectedBucket ? null : bar.index)}
                      onKeyDown={(event) => {
                        if (event.key === "Enter" || event.key === " ") {
                          event.preventDefault();
                          setSelectedBucket(bar.index === selectedBucket ? null : bar.index);
                        }
                      }}
                    >
                      {bar.segments.map((segment, segmentIndex) => (
                        <rect
                          key={`${bar.index}-${segmentIndex}`}
                          x={bar.x}
                          y={segment.y}
                          width={bar.width}
                          height={segment.height}
                          fill={SOURCE_PALETTE[segment.paletteIndex]}
                          opacity={selectedBucket === null || selectedBucket === bar.index ? 0.85 : 0.35}
                        >
                          <title>{`${bar.label} · ${segment.source}: ${segment.amount.toLocaleString("en-US")}`}</title>
                        </rect>
                      ))}
                      {bar.breachesMinimumBalance ? (
                        <rect
                          x={bar.x - 2}
                          y={CASH_LADDER_CHART_PADDING.top}
                          width={bar.width + 4}
                          height={CASH_LADDER_CHART_HEIGHT - CASH_LADDER_CHART_PADDING.top - CASH_LADDER_CHART_PADDING.bottom}
                          fill="none"
                          stroke={THRESHOLD}
                          strokeWidth="1"
                          strokeDasharray="3 2"
                          opacity="0.7"
                        />
                      ) : null}
                      <text
                        x={bar.x + bar.width / 2}
                        y={CASH_LADDER_CHART_HEIGHT - CASH_LADDER_CHART_PADDING.bottom + 14}
                        fill={AXIS}
                        textAnchor="middle"
                        fontSize="9"
                      >
                        {bar.label.split(" – ")[0]}
                      </text>
                    </g>
                  ))}

                  <polyline
                    points={chart.cumulativePoints}
                    fill="none"
                    stroke={CUMULATIVE}
                    strokeWidth="2"
                  />
                  {chart.cumulativeMarkers.map((marker, index) => (
                    <circle
                      key={index}
                      cx={marker.x}
                      cy={marker.y}
                      r={marker.breaches ? 4 : 2.5}
                      fill={marker.breaches ? THRESHOLD : CUMULATIVE}
                    />
                  ))}
                </svg>
              ) : (
                <EmptyState
                  icon="chart"
                  title={status === "loading" ? "Loading cash ladder…" : "No projected flows"}
                  detail={
                    status === "loading"
                      ? "Running the portfolio projection across held positions."
                      : "No positions produced projected flows inside the horizon."
                  }
                  compact
                />
              )}
            </CardContent>
          </Card>

          <div className="grid gap-4 xl:grid-cols-3">
            <Card className="xl:col-span-2">
              <CardHeader className="pb-2">
                <CardTitle>
                  {selectedBucketLabel ? `Contributing flows · ${selectedBucketLabel}` : "Contributing flows"}
                </CardTitle>
                <CardDescription>
                  {selectedBucketLabel
                    ? "Every flow in the selected bucket, traceable to its projection run and Security Master terms version."
                    : "Click a bar to drill into the instruments and capital activity behind that bucket."}
                </CardDescription>
              </CardHeader>
              <CardContent>
                <DenseDataTable
                  columns={drillColumns}
                  rows={drillRows}
                  getRowId={(row) => row.id}
                  emptyText={
                    selectedBucketLabel
                      ? "No flows in the selected bucket."
                      : "Select a bucket in the chart to inspect its contributing flows."
                  }
                  ariaLabel="Cash ladder contributing flows"
                />
              </CardContent>
            </Card>

            <Card>
              <CardHeader className="pb-2">
                <CardTitle>Scenario semantics</CardTitle>
                <CardDescription>What this scenario models versus what it assumes.</CardDescription>
              </CardHeader>
              <CardContent className="space-y-3 text-sm">
                {activeScenario ? (
                  <>
                    <p className="text-muted-foreground">{activeScenario.description}</p>
                    <div>
                      <h3 className="mb-1 text-xs font-semibold uppercase tracking-wide text-success">Modeled</h3>
                      <ul className="list-disc space-y-1 pl-5 text-muted-foreground">
                        {activeScenario.modeledEffects.map((effect) => (
                          <li key={effect}>{effect}</li>
                        ))}
                      </ul>
                    </div>
                    <div>
                      <h3 className="mb-1 text-xs font-semibold uppercase tracking-wide text-warning">Assumed</h3>
                      <ul className="list-disc space-y-1 pl-5 text-muted-foreground">
                        {activeScenario.assumptions.map((assumption) => (
                          <li key={assumption}>{assumption}</li>
                        ))}
                      </ul>
                    </div>
                  </>
                ) : (
                  <p className="text-muted-foreground">Scenario details load with the ladder.</p>
                )}
                {ladder && (ladder.assumptions.length > 0 || ladder.warnings.length > 0) ? (
                  <div className="border-t pt-3">
                    {ladder.warnings.length > 0 ? (
                      <>
                        <h3 className="mb-1 text-xs font-semibold uppercase tracking-wide text-danger">Warnings</h3>
                        <ul className="list-disc space-y-1 pl-5 text-muted-foreground">
                          {ladder.warnings.map((warning) => (
                            <li key={warning}>{warning}</li>
                          ))}
                        </ul>
                      </>
                    ) : null}
                    {ladder.assumptions.length > 0 ? (
                      <>
                        <h3 className="mb-1 mt-2 text-xs font-semibold uppercase tracking-wide text-muted-foreground">
                          Ladder assumptions
                        </h3>
                        <ul className="list-disc space-y-1 pl-5 text-muted-foreground">
                          {ladder.assumptions.map((assumption) => (
                            <li key={assumption}>{assumption}</li>
                          ))}
                        </ul>
                      </>
                    ) : null}
                  </div>
                ) : null}
              </CardContent>
            </Card>
          </div>
        </>
      ) : null}
    </div>
  );
}
