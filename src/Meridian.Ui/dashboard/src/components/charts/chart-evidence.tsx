import { useId, useRef, useState, type KeyboardEvent, type ReactNode } from "react";
import { stepCrosshairIndex } from "./chart-interaction";
import "@/styles/chart-evidence.css";

export interface ChartEvidenceValue {
  series: string;
  value: number | string | null;
  unit: string | null;
  basis: string;
}

export interface ChartEvidenceProof {
  href: string;
  label: string;
  onOpen?: () => void;
}

export interface ChartEvidence {
  id: string;
  label: string;
  asOf?: string | null;
  values: readonly ChartEvidenceValue[];
  proof?: ChartEvidenceProof | null;
  sourceDetail?: string;
}

interface ChartMarkProps {
  tabIndex: number;
  "aria-label": string;
  "aria-controls": string;
  "aria-current": true | undefined;
  "data-chart-point": number;
  onFocus: () => void;
  onPointerEnter: () => void;
  onClick: () => void;
  onKeyDown: (event: KeyboardEvent<SVGElement | HTMLButtonElement>) => void;
}

export interface ChartInspection {
  markProps: (index: number, onActivate?: () => void) => ChartMarkProps;
  pointId: (index: number) => string;
  activeIndex: number;
  hintId: string;
}

function pointName(point: ChartEvidence) {
  const series = point.values.map(({ series, value, unit }) => (
    `${series} ${value ?? "Unavailable"}${unit && value !== null ? ` ${unit}` : ""}`
  ));
  return [point.label, point.asOf, ...series].filter(Boolean).join(". ");
}

export function ChartEvidenceView({
  label,
  evidence,
  initialIndex = 0,
  children
}: {
  label: string;
  evidence: readonly ChartEvidence[];
  initialIndex?: number;
  children: (inspection: ChartInspection) => ReactNode;
}) {
  const id = useId();
  const hintId = `${id}-keys`;
  const inspectorId = `${id}-inspector`;
  const plot = useRef<HTMLDivElement>(null);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const selectedIndex = selectedId === null ? -1 : evidence.findIndex((point) => point.id === selectedId);
  const activeIndex = selectedIndex < 0 ? initialIndex : selectedIndex;
  const active = evidence[activeIndex];

  function select(index: number) {
    setSelectedId(evidence[index]?.id ?? null);
  }

  function markProps(index: number, onActivate?: () => void): ChartMarkProps {
    const inspect = () => select(index);
    const activate = () => {
      inspect();
      onActivate?.();
    };
    return {
      tabIndex: index === activeIndex ? 0 : -1,
      "aria-label": pointName(evidence[index]),
      "aria-controls": inspectorId,
      "aria-current": index === activeIndex ? true : undefined,
      "data-chart-point": index,
      onFocus: inspect,
      onPointerEnter: inspect,
      onClick: activate,
      onKeyDown: (event) => {
        if (event.key === " " || event.key === "Enter") {
          event.preventDefault();
          activate();
          return;
        }
        const next = stepCrosshairIndex(index, event.key, evidence.length);
        if (next === null) return;
        event.preventDefault();
        plot.current?.querySelector<SVGElement | HTMLButtonElement>(`[data-chart-point="${next}"]`)?.focus();
      }
    };
  }

  const inspection = { markProps, pointId: (index: number) => evidence[index].id, activeIndex, hintId };
  if (!active) return children(inspection);

  return (
    <div className="min-w-0">
      <div ref={plot}>{children(inspection)}</div>
      <section id={inspectorId} aria-label={`${label} inspector`} className="decision-chart-inspector mt-2 rounded-sm border border-border/70 bg-secondary/15 px-3 py-2 text-xs">
        <div className="flex flex-wrap items-baseline justify-between gap-x-4 gap-y-1">
          <strong className="text-foreground">Chart data</strong>
          {evidence.length > 1 ? (
            <div className="flex items-center gap-2">
              <label htmlFor={`${id}-observation`}>Point {activeIndex + 1} of {evidence.length}</label>
              <input
                id={`${id}-observation`}
                className="decision-chart-range"
                type="range"
                min={0}
                max={evidence.length - 1}
                value={activeIndex}
                onChange={(event) => select(event.currentTarget.valueAsNumber)}
                aria-label={`Choose a point in ${label}`}
                aria-valuetext={pointName(active)}
                aria-controls={inspectorId}
              />
            </div>
          ) : null}
        </div>
        <p id={hintId} className="sr-only">Use Left and Right Arrow to inspect points. Home and End jump to the first and last point.</p>
        {selectedId !== null && selectedIndex < 0 ? (
          <p role="status" className="mt-1 text-muted-foreground">The previously inspected point is outside the current chart.</p>
        ) : null}
        <table aria-label={`${label} chart data`} className="my-1 w-full table-fixed text-left">
          <caption className="break-words py-1 text-left font-medium text-foreground">{active.label}</caption>
          <thead className="border-b border-border/70 text-muted-foreground">
            <tr>
              <th scope="col" className="w-[24%] py-1 pr-2">Series</th>
              <th scope="col" className="w-[17%] py-1 pr-2">Plotted value</th>
              <th scope="col" className="w-[19%] py-1 pr-2">Unit</th>
              <th scope="col" className="py-1">Calculation basis</th>
            </tr>
          </thead>
          <tbody className="break-words text-foreground">
            {active.values.map((row) => (
              <tr key={row.series} className="align-top">
                <th scope="row" className="py-1 pr-2 font-medium">{row.series}</th>
                <td className="py-1 pr-2 font-mono tabular-nums">{row.value ?? "Unavailable"}</td>
                <td className="py-1 pr-2">{row.unit ?? "Unreported"}</td>
                <td className="py-1">{row.basis}</td>
              </tr>
            ))}
          </tbody>
        </table>
        <dl className="flex flex-wrap gap-x-5 gap-y-1 border-t border-border/70 pt-1.5 text-muted-foreground">
          <div className="flex flex-wrap gap-x-2">
            <dt className="font-medium">Timestamp / as-of</dt>
            <dd className="break-all">{active.asOf ?? "Unavailable"}</dd>
          </div>
          <div className="flex flex-wrap gap-x-2">
            <dt className="font-medium">Proof</dt>
            <dd>
              {active.proof ? (
                <a className="decision-chart-link font-medium text-primary underline underline-offset-2" href={active.proof.href} onClick={active.proof.onOpen}>
                  {active.proof.label}
                </a>
              ) : "Unavailable"}
            </dd>
          </div>
        </dl>
        {active.sourceDetail ? <p className="mt-1 break-words text-muted-foreground">{active.sourceDetail}</p> : null}
      </section>
    </div>
  );
}

export type ChartMarkGeometry = { x: number; y: number; radius?: number } | { x: number; y: number; width: number; height: number };

export function ChartEvidenceMark({
  inspection,
  index,
  geometry,
  point,
  onActivate,
  pressed
}: {
  inspection: ChartInspection;
  index: number;
  geometry: ChartMarkGeometry;
  point?: { x: number; y: number; radius?: number };
  onActivate?: () => void;
  pressed?: boolean;
}) {
  return (
    <g role="button" className="decision-chart-mark" aria-pressed={pressed} {...inspection.markProps(index, onActivate)}>
      {"width" in geometry ? (
        <rect x={geometry.x} y={geometry.y} width={geometry.width} height={geometry.height} className="decision-chart-hit" vectorEffect="non-scaling-stroke" />
      ) : (
        <circle cx={geometry.x} cy={geometry.y} r={geometry.radius ?? 6} className="decision-chart-hit" vectorEffect="non-scaling-stroke" />
      )}
      {point ? <circle cx={point.x} cy={point.y} r={point.radius ?? 6} className="decision-chart-hit" vectorEffect="non-scaling-stroke" /> : null}
    </g>
  );
}
