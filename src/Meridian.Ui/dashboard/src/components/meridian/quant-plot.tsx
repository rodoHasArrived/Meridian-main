import { useId, useMemo, useRef, type ReactNode } from "react";
import { ChartEvidenceMark, ChartEvidenceView, type ChartEvidenceProof } from "@/components/charts/chart-evidence";
import type { QuantPlot } from "@/types";
import {
  buildQuantPlotViewModel,
  histogramBins,
  prepareSeries,
  QUANT_PLOT_HEIGHT,
  QUANT_PLOT_WIDTH,
  type QuantPlotAxisTickViewModel,
  type QuantPlotFrameViewModel,
  type QuantPlotViewModel
} from "./quant-plot.view-model";

export { buildQuantPlotViewModel, histogramBins, prepareSeries };

interface QuantPlotChartProps {
  plot: QuantPlot;
  proof?: ChartEvidenceProof;
}

export function QuantPlotChart({ plot, proof }: QuantPlotChartProps) {
  const vm = useMemo(() => buildQuantPlotViewModel(plot), [plot]);

  if (vm.kind === "unsupported") {
    return (
      <div className={`rounded-md border px-3 py-3 text-xs ${vm.toneClassName}`} role="status">
        <div className="font-semibold text-foreground">{vm.title}</div>
        <div className="mt-1">{vm.message}</div>
      </div>
    );
  }

  return <RenderableQuantPlot vm={vm} proof={proof} />;
}

export function QuantPlotSource({ source, children }: { source?: string | null; children: (proof?: ChartEvidenceProof) => ReactNode }) {
  const id = useId();
  const details = useRef<HTMLDetailsElement>(null);
  const proof = source ? {
    href: `#${id}`,
    label: "Open submitted script",
    onOpen: () => { if (details.current) details.current.open = true; }
  } : undefined;
  return (
    <>
      {children(proof)}
      {source ? (
        <details ref={details} className="mt-2 rounded-sm border border-border/60 px-3 py-2 text-xs">
          <summary id={id} className="cursor-pointer font-medium">Submitted script</summary>
          <pre className="mt-2 max-h-48 overflow-auto whitespace-pre-wrap break-words font-mono">{source}</pre>
        </details>
      ) : null}
    </>
  );
}

function RenderableQuantPlot({ vm, proof }: { vm: Exclude<QuantPlotViewModel, { kind: "unsupported" }>; proof?: ChartEvidenceProof }) {
  return (
    <ChartFrame frame={vm.frame} proof={proof}>
      {"axisTicks" in vm ? <YAxis ticks={vm.axisTicks} /> : null}
      {vm.kind === "line" ? (
        <>
          {vm.series.map((series) => (
            <g key={series.id}>
              {series.areaPath ? <path d={series.areaPath} fill={series.color} fillOpacity={series.fillOpacity} /> : null}
              <path
                d={series.linePath}
                fill="none"
                stroke={series.color}
                strokeLinecap="round"
                strokeLinejoin="round"
                strokeWidth={1.5}
              />
            </g>
          ))}
          <line {...vm.zeroLine} />
        </>
      ) : null}
      {vm.kind === "bar" ? (
        <>
          {vm.bars.map((bar) => (
            <rect key={bar.id} {...bar} />
          ))}
        </>
      ) : null}
      {vm.kind === "scatter" ? (
        <>
          {vm.points.map((point) => (
            <circle key={point.id} {...point} />
          ))}
        </>
      ) : null}
      {vm.kind === "histogram" ? (
        <>
          {vm.bars.map((bar) => (
            <rect key={bar.id} {...bar} />
          ))}
        </>
      ) : null}
      {vm.kind === "candlestick" ? (
        <>
          {vm.candles.map((candle) => (
            <g key={candle.id}>
              <line {...candle.wick} />
              <rect {...candle.body} />
            </g>
          ))}
        </>
      ) : null}
      {vm.kind === "heatmap" ? (
        <>
          {vm.cells.map((cell) => (
            <rect key={cell.id} {...cell} />
          ))}
          {vm.xLabels.map((label) => (
            <text
              key={label.id}
              x={label.x}
              y={label.y}
              fill={label.fill}
              fontSize={label.fontSize}
              textAnchor={label.textAnchor}
            >
              {label.label}
            </text>
          ))}
          {vm.yLabels.map((label) => (
            <text
              key={label.id}
              x={label.x}
              y={label.y}
              fill={label.fill}
              fontSize={label.fontSize}
              textAnchor={label.textAnchor}
            >
              {label.label}
            </text>
          ))}
          <defs>
            <linearGradient id={vm.scale.gradientId} x1="0" x2="1" y1="0" y2="0">
              <stop offset="0%" stopColor={vm.scale.stops[0]} />
              <stop offset="50%" stopColor={vm.scale.stops[1]} />
              <stop offset="100%" stopColor={vm.scale.stops[2]} />
            </linearGradient>
          </defs>
          <rect
            x={vm.scale.x}
            y={vm.scale.y}
            width={vm.scale.width}
            height={vm.scale.height}
            fill={`url(#${vm.scale.gradientId})`}
            rx={vm.scale.rx}
          />
        </>
      ) : null}
    </ChartFrame>
  );
}

function ChartFrame({ frame, proof, children }: { frame: QuantPlotFrameViewModel; proof?: ChartEvidenceProof; children: ReactNode }) {
  const descriptionId = `${frame.descriptionId}-${useId()}`;
  const evidence = frame.marks.map((mark) => ({ ...mark, proof }));
  return (
    <figure className="min-w-0 rounded-md border border-border/60 bg-secondary/15 p-3">
      <figcaption className="mb-2 flex flex-wrap items-baseline justify-between gap-2 text-xs">
        <span className="font-semibold text-foreground">{frame.title}</span>
        {frame.legend.length > 0 ? (
          <div className="flex flex-wrap gap-x-3 gap-y-1 text-muted-foreground">
            {frame.legend.map((entry) => (
              <span key={entry.label} className="inline-flex items-center gap-1.5">
                <span aria-hidden="true" className="inline-block h-2 w-2 rounded-sm" style={{ backgroundColor: entry.color }} />
                {entry.label}
              </span>
            ))}
          </div>
        ) : null}
      </figcaption>
      <ChartEvidenceView label={frame.title} evidence={evidence}>
        {(inspection) => (
          <svg
            role="group"
            aria-label={frame.ariaLabel}
            aria-describedby={`${descriptionId} ${inspection.hintId}`}
            viewBox={frame.viewBox}
            className="h-auto w-full overflow-visible"
            preserveAspectRatio="xMidYMid meet"
            width={QUANT_PLOT_WIDTH}
            height={QUANT_PLOT_HEIGHT}
          >
            {children}
            {frame.marks.map((mark, index) => (
              <ChartEvidenceMark key={mark.id} inspection={inspection} index={index} geometry={mark.geometry} />
            ))}
          </svg>
        )}
      </ChartEvidenceView>
      <p id={descriptionId} className="sr-only">
        {frame.description}
      </p>
    </figure>
  );
}

function YAxis({ ticks }: { ticks: QuantPlotAxisTickViewModel[] }) {
  return (
    <>
      {ticks.map((tick) => (
        <g key={tick.id}>
          <line x1={44} x2={QUANT_PLOT_WIDTH - 12} y1={tick.y} y2={tick.y} stroke={tick.gridStroke} strokeWidth={1} />
          <text x={38} y={tick.y + 3} fill={tick.textFill} fontSize={10} textAnchor="end">
            {tick.label}
          </text>
        </g>
      ))}
    </>
  );
}
