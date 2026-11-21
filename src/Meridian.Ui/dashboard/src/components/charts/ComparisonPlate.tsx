// Meridian comparison plate — dashboard-native port of the design-system 4.3.0 `ComparisonPlate`
// and `PlateGrid` (components/charts/ComparisonPlate.jsx, release-candidate maturity). Every plate
// in a set shares plot size, domain, tick positions, label placement, padding and footer geometry,
// so a visible difference between plates is a real difference and not an artefact of scaling.
// PlateGrid computes the ONE domain and the ONE tick set; a plate may be selected (rail + tint)
// but is never resized.
//
// Adapted for the workstation, behaviour unchanged: text is 12px or larger (the reference uses
// 10–11px), strokes resolve the dashboard chart roles (graphite / slate / neutral) with hex
// fallbacks like the other chart primitives, and no stylesheet is injected at runtime.
//
// Port extensions:
//  - a plate may carry a `status` instead of a drawn series. It keeps its place and its geometry
//    and says in words why nothing is drawn, so an entity whose series is still loading or is
//    unavailable is never dropped from the set without a trace;
//  - PlateGrid hands its formatter the shared scale (domain, ticks, tick step), so a host can keep
//    label precision at or below the tick step — two gridlines never read the same;
//  - the label gutters widen, identically for every plate of a grid, when a label is longer than
//    the default gutter, so a long tick or end label is neither clipped nor drawn over the trace.
import { cn } from "@/lib/utils";

/** Series roles from the visualization ledger. Colour and dash are assigned by role, never by instance. */
export type PlateRole = "actual" | "prior" | "benchmark" | "scenario" | "target" | "estimated";

export interface PlateStatus {
  /** `loading` — the series is on its way; `unavailable` — there is no series to draw. */
  kind: "loading" | "unavailable";
  /** Operator copy shown in the plot area in place of the trace. */
  text: string;
}

export interface PlateSpec {
  id: string;
  title: string;
  tag?: string;
  values: ReadonlyArray<number | null>;
  /** Overrides the grid's categories for this plate. Same length as `values`. */
  categories?: readonly string[];
  /** @default "actual" */
  role?: PlateRole;
  footerLabel?: string;
  footerValue?: string;
  /** Port extension: state the reason instead of drawing. Excluded from the shared domain. */
  status?: PlateStatus;
}

export interface ComparisonPlateProps {
  title: string;
  tag?: string;
  categories: readonly string[];
  values: ReadonlyArray<number | null>;
  /** @default "actual" */
  role?: PlateRole;
  /** The set's shared value domain. */
  domain: readonly [number, number];
  /** The set's shared tick positions. Derived from `domain` when omitted. */
  ticks?: readonly number[];
  /** @default String */
  format?: (value: number) => string;
  footerLabel?: string;
  footerValue?: string;
  /** Plot height in px. Identical for every plate in a grid. @default 132 */
  height?: number;
  /** Label gutters in view-box units. Derived from this plate's own labels when omitted. */
  gutters?: PlateGutters;
  selected?: boolean;
  /** Providing this renders the plate as a `<button aria-pressed>`. */
  onSelect?: () => void;
  status?: PlateStatus;
  className?: string;
}

interface PlateRoleStyle {
  stroke: string;
  dash?: string;
  marker: "filled" | "open" | "none";
}

// Role ledger resolved to the dashboard chart tokens: observations are graphite, the prior /
// comparison channel is slate, the benchmark is neutral. Dash and marker carry the role too, so
// no role is told apart by colour alone.
const PLATE_ROLE_STYLES: Record<PlateRole, PlateRoleStyle> = {
  actual: { stroke: "var(--chart-series-1, #29323A)", marker: "filled" },
  prior: { stroke: "var(--chart-compare-1, #7890A4)", marker: "filled" },
  benchmark: { stroke: "var(--chart-compare-2, #8C918F)", dash: "4 4", marker: "none" },
  scenario: { stroke: "var(--brand-signal, #C56343)", dash: "7 5", marker: "open" },
  target: { stroke: "var(--chart-axis, #606870)", dash: "2 4", marker: "none" },
  estimated: {
    stroke: "color-mix(in srgb, var(--chart-series-1, #29323A) 62%, var(--chart-plot, #F8F7F3))",
    dash: "5 4",
    marker: "open"
  }
};

const GRID = "var(--chart-grid, #E2DED5)";
const AXIS = "var(--chart-axis, #606870)";
const HALO = "var(--chart-plot, #F8F7F3)";

/** View-box width shared by every plate. The grid's minimum column keeps it at 1:1 or larger. */
export const PLATE_VIEW_WIDTH = 240;
/** Default plot height shared by every plate. */
export const PLATE_PLOT_HEIGHT = 132;
const PAD_TOP = 10;
const PAD_BOTTOM = 20;
const PAD_LEFT = 36;
const PAD_RIGHT = 46;
const TEXT_STYLE = { fontFamily: "var(--font-data, monospace)", fontSize: 12 } as const;
// Advance of one character of the 12px data face (monospace, 0.6em) with a little slack.
const LABEL_CHAR_WIDTH = 7.3;
const TICK_LABEL_GAP = 6;
const END_LABEL_OFFSET = 5;
const END_LABEL_SLACK = 2;
// Gutters stop growing here so the trace always keeps a readable share of the 240-unit view box.
const MAX_GUTTER = 84;

function isPlottable(value: number | null | undefined): value is number {
  return typeof value === "number" && Number.isFinite(value);
}

function firstPlottableIndex(values: ReadonlyArray<number | null>): number {
  let index = 0;
  while (index < values.length && !isPlottable(values[index])) index += 1;
  return index;
}

function lastPlottableIndex(values: ReadonlyArray<number | null>): number {
  let index = values.length - 1;
  while (index >= 0 && !isPlottable(values[index])) index -= 1;
  return index;
}

/** The scale every plate of a grid shares, as handed to the grid's formatter. */
export interface PlateScale {
  domain: readonly [number, number];
  ticks: readonly number[];
  /** Distance between adjacent ticks; `null` when there are fewer than two. */
  step: number | null;
}

/** Left (tick labels) and right (end label) gutters of the plot, in view-box units. */
export interface PlateGutters {
  left: number;
  right: number;
}

function longest(labels: readonly string[]): number {
  return labels.reduce((length, label) => Math.max(length, label.length), 0);
}

/**
 * Gutters wide enough for the longest tick label and the longest end label. Never narrower than
 * the reference geometry, so short labels leave the plate exactly as the package draws it.
 */
export function plateGutters(tickLabels: readonly string[], endLabels: readonly string[]): PlateGutters {
  const left = Math.ceil(longest(tickLabels) * LABEL_CHAR_WIDTH) + TICK_LABEL_GAP;
  const right = END_LABEL_OFFSET + Math.ceil(longest(endLabels) * LABEL_CHAR_WIDTH) + END_LABEL_SLACK;
  return {
    left: Math.min(MAX_GUTTER, Math.max(PAD_LEFT, left)),
    right: Math.min(MAX_GUTTER, Math.max(PAD_RIGHT, right))
  };
}

/** Padded domain across any number of series (nulls ignored). `[0, 1]` when nothing is plottable. */
export function plateDomain(
  series: ReadonlyArray<ReadonlyArray<number | null>>,
  pad = 0.08
): [number, number] {
  let min = Infinity;
  let max = -Infinity;
  for (const values of series) {
    for (const value of values) {
      if (!isPlottable(value)) continue;
      if (value < min) min = value;
      if (value > max) max = value;
    }
  }
  if (min === Infinity) return [0, 1];
  const padding = (max - min || Math.abs(max) || 1) * pad;
  return [min - padding, max + padding];
}

/**
 * Round tick values covering [min, max] in interpretable steps. This is the package kernel's
 * `niceTicks`, kept here rather than the shared `./ticks` helper because a 132px plate needs the
 * kernel's coarser steps (about `count` ticks, not up to 2.5× that).
 */
export function plateTicks(min: number, max: number, count = 4): number[] {
  if (!Number.isFinite(min) || !Number.isFinite(max)) return [];
  if (min === max) return [min];
  const raw = (max - min) / Math.max(1, count - 1);
  const magnitude = Math.pow(10, Math.floor(Math.log10(raw)));
  const normalized = raw / magnitude;
  const step = (normalized >= 7.5 ? 10 : normalized >= 3.5 ? 5 : normalized >= 1.5 ? 2 : 1) * magnitude;
  if (!Number.isFinite(step) || step <= 0) return [min];
  const start = Math.ceil(min / step) * step;
  const ticks: number[] = [];
  for (let value = start; value <= max + step * 0.001; value += step) {
    ticks.push(Number(value.toFixed(10)));
  }
  return ticks;
}

/** The shared scale for a domain: its ticks and the step between them. */
export function plateScale(domain: readonly [number, number], tickCount = 4): PlateScale {
  const ticks = plateTicks(domain[0], domain[1], tickCount);
  const step = ticks.length >= 2 ? Number((ticks[1] - ticks[0]).toFixed(10)) : null;
  return { domain, ticks, step };
}

/** Polyline path with null-gap support: a missing value breaks the line instead of bridging it. */
export function platePath(
  values: ReadonlyArray<number | null>,
  x: (index: number) => number,
  y: (value: number) => number
): string {
  let path = "";
  let penDown = false;
  values.forEach((value, index) => {
    if (!isPlottable(value)) {
      penDown = false;
      return;
    }
    path += `${penDown ? "L" : "M"}${x(index).toFixed(2)} ${y(value).toFixed(2)}`;
    penDown = true;
  });
  return path;
}

function linearScale(domain: readonly [number, number], range: readonly [number, number]) {
  const span = domain[1] - domain[0] || 1;
  return (value: number) => range[0] + ((value - domain[0]) / span) * (range[1] - range[0]);
}

const PLATE_FRAME_CLASS =
  "flex min-w-0 flex-col rounded-[var(--radius-chip)] border border-l-[3px] border-border p-3 text-left font-sans";

export function ComparisonPlate({
  title,
  tag,
  categories,
  values,
  role = "actual",
  domain,
  ticks,
  format = String,
  footerLabel,
  footerValue,
  height = PLATE_PLOT_HEIGHT,
  gutters,
  selected = false,
  onSelect,
  status,
  className
}: ComparisonPlateProps) {
  const plotHeight = height - PAD_TOP - PAD_BOTTOM;
  const tickValues = ticks && ticks.length > 0 ? ticks : plateTicks(domain[0], domain[1], 4);
  const pointCount = Math.max(categories.length, values.length);
  const roleStyle = PLATE_ROLE_STYLES[role] ?? PLATE_ROLE_STYLES.actual;

  const firstIndex = firstPlottableIndex(values);
  const lastIndex = lastPlottableIndex(values);
  const firstValue = firstIndex < values.length ? values[firstIndex] : null;
  const lastValue = lastIndex >= 0 ? values[lastIndex] : null;

  const { left: padLeft, right: padRight } = gutters
    ?? plateGutters(tickValues.map((tick) => format(tick)), isPlottable(lastValue) ? [format(lastValue)] : []);
  const x = linearScale([0, Math.max(1, pointCount - 1)], [padLeft, PLATE_VIEW_WIDTH - padRight]);
  const y = linearScale(domain, [PAD_TOP + plotHeight, PAD_TOP]);

  const footerText = [footerLabel, footerValue].filter(Boolean).join(" ");
  const figureLabel = [
    footerText ? `${title} — ${footerText}` : title,
    isPlottable(firstValue) && isPlottable(lastValue)
      ? `first ${format(firstValue)}, last ${format(lastValue)}`
      : null
  ].filter(Boolean).join("; ");

  const frameClassName = cn(
    PLATE_FRAME_CLASS,
    selected
      ? "border-l-[var(--interaction-selected)] bg-[var(--interaction-selected-surface)]"
      : "border-l-transparent bg-[var(--chart-plot)]",
    className
  );

  const body = (
    <>
      <span className="mb-2 flex items-baseline gap-2">
        <span className="min-w-0 truncate text-sm font-semibold text-foreground">{title}</span>
        {tag ? (
          <span className="ml-auto shrink-0 font-mono text-xs uppercase tracking-wide text-muted-foreground">{tag}</span>
        ) : null}
      </span>
      {status ? (
        <span
          data-plate-status={status.kind}
          className="flex items-center border border-dashed border-border px-3 text-xs leading-5 text-muted-foreground"
          style={{ height }}
        >
          {status.text}
        </span>
      ) : (
        <svg
          viewBox={`0 0 ${PLATE_VIEW_WIDTH} ${height}`}
          width="100%"
          height={height}
          preserveAspectRatio="xMidYMid meet"
          role="img"
          aria-label={figureLabel}
        >
          {tickValues.map((tick) => (
            <g key={tick}>
              <line x1={padLeft} x2={PLATE_VIEW_WIDTH - 4} y1={y(tick)} y2={y(tick)} stroke={GRID} strokeWidth={1} />
              <text x={0} y={y(tick) + 4} fill={AXIS} style={TEXT_STYLE}>{format(tick)}</text>
            </g>
          ))}
          <path
            d={platePath(values, x, y)}
            fill="none"
            stroke={roleStyle.stroke}
            strokeWidth={1.5}
            strokeDasharray={roleStyle.dash}
            strokeLinejoin="round"
          />
          {roleStyle.marker === "none"
            ? null
            : values.map((value, index) => (isPlottable(value) ? (
              <circle
                key={index}
                cx={x(index)}
                cy={y(value)}
                r={2.2}
                fill={roleStyle.marker === "filled" ? roleStyle.stroke : HALO}
                stroke={roleStyle.stroke}
                strokeWidth={roleStyle.marker === "filled" ? 0 : 1}
              />
            ) : null))}
          {isPlottable(lastValue) ? (
            <text
              x={x(lastIndex) + END_LABEL_OFFSET}
              y={y(lastValue) + 4}
              fill={roleStyle.stroke}
              stroke={HALO}
              strokeWidth={3}
              strokeLinejoin="round"
              paintOrder="stroke"
              style={{ ...TEXT_STYLE, fontWeight: 600 }}
            >
              {format(lastValue)}
            </text>
          ) : null}
          {categories.map((category, index) => (category ? (
            <text key={index} x={x(index)} y={height - 4} fill={AXIS} textAnchor="middle" style={TEXT_STYLE}>
              {category}
            </text>
          ) : null))}
        </svg>
      )}
      {footerLabel || footerValue ? (
        <span className="mt-2 flex items-baseline gap-2 border-t border-[var(--border-divider)] pt-2">
          <span className="text-xs text-muted-foreground">{footerLabel}</span>
          <span className="ml-auto font-mono text-xs font-semibold tabular-nums text-foreground">{footerValue}</span>
        </span>
      ) : null}
    </>
  );

  // A plate with nothing drawn has nothing to select; it stays a static frame of the same size.
  if (onSelect && !status) {
    return (
      <button
        type="button"
        aria-pressed={selected}
        onClick={onSelect}
        data-plate-selected={selected ? "true" : undefined}
        className={cn(
          frameClassName,
          "cursor-pointer hover:border-y-[var(--border-hover)] hover:border-r-[var(--border-hover)] focus-visible:outline-hidden focus-visible:ring-2 focus-visible:ring-[var(--interaction-focus)]"
        )}
      >
        {body}
      </button>
    );
  }

  return <div className={frameClassName}>{body}</div>;
}

ComparisonPlate.displayName = "ComparisonPlate";

export interface PlateGridProps {
  plates: readonly PlateSpec[];
  categories: readonly string[];
  /** Explicit shared domain. Computed across every drawn plate when omitted. */
  domain?: readonly [number, number];
  /** @default 4 */
  tickCount?: number;
  /**
   * Formats tick labels, end labels and the figure name. The second argument is the grid's shared
   * scale, so the formatter can keep its precision at or below the tick step. @default String
   */
  format?: (value: number, scale: PlateScale) => string;
  /** Role for plates that do not name one. @default "actual" */
  role?: PlateRole;
  selectedId?: string | null;
  onSelect?: (id: string) => void;
  className?: string;
}

export function PlateGrid({
  plates,
  categories,
  domain,
  tickCount = 4,
  format = String,
  role = "actual",
  selectedId = null,
  onSelect,
  className
}: PlateGridProps) {
  // ONE domain and ONE tick set for the whole set — never per-plate auto-scaling.
  const drawn = plates.filter((plate) => !plate.status);
  const sharedDomain = domain ?? plateDomain(drawn.map((plate) => plate.values));
  const scale = plateScale(sharedDomain, tickCount);
  const sharedTicks = scale.ticks;
  const sharedFormat = (value: number) => format(value, scale);
  // ONE pair of gutters too: the plot is the same width in every plate whatever its own labels.
  const sharedGutters = plateGutters(
    sharedTicks.map(sharedFormat),
    drawn.flatMap((plate) => {
      const last = plate.values[lastPlottableIndex(plate.values)];
      return isPlottable(last) ? [sharedFormat(last)] : [];
    })
  );
  return (
    <div
      className={cn("grid w-full gap-3", className)}
      // auto-fill (the reference uses auto-fit): a set of two keeps the same plate width as a set
      // of six instead of stretching each plate across half the sheet.
      style={{ gridTemplateColumns: "repeat(auto-fill, minmax(min(100%, 266px), 1fr))" }}
      data-plate-domain={`${sharedDomain[0]},${sharedDomain[1]}`}
    >
      {plates.map((plate) => (
        <ComparisonPlate
          key={plate.id}
          title={plate.title}
          tag={plate.tag}
          categories={plate.categories ?? categories}
          values={plate.values}
          role={plate.role ?? role}
          domain={sharedDomain}
          ticks={sharedTicks}
          format={sharedFormat}
          gutters={sharedGutters}
          footerLabel={plate.footerLabel}
          footerValue={plate.footerValue}
          selected={selectedId === plate.id}
          onSelect={onSelect ? () => onSelect(plate.id) : undefined}
          status={plate.status}
        />
      ))}
    </div>
  );
}

PlateGrid.displayName = "PlateGrid";
