/**
 * Trace chart — the annotated line primitive the Meridian visualization grammar
 * is built on. Everything the grammar requires is structural, not optional:
 *
 * - **Series roles** (`actual · prior · benchmark · scenario · target ·
 *   estimated`) resolve to color, stroke width, dash, and marker from the role
 *   ledger. You never pass a color.
 * - **Direct labels** at each series endpoint with a surface halo and
 *   deterministic collision resolution — no detached legend.
 * - **Model intervals** draw as bands (`band`), visually distinct from observed
 *   data condition, which belongs to `DataCondition`/`EvidenceInspector`.
 * - **Selection is committed**: click or ←/→/Home/End fires `onSelect`; the
 *   selected index gets a datum rail and enlarged markers. Hover only previews.
 * - **Fallbacks**: `description` becomes the accessible name and the caption of
 *   a visually-hidden data table (`Missing` for null values, never blank).
 *
 * A `null` in `values` breaks the line — that is the missing-data encoding.
 *
 * @example
 * <TraceChart
 *   description="US Treasury curve — current, one-month prior, and bull-steepener scenario."
 *   categories={["1M","3M","6M","1Y","2Y","3Y","5Y","7Y","10Y","20Y","30Y"]}
 *   yUnit="Yield (%)" xUnit="Tenor" yFormat={(v) => v.toFixed(1) + "%"}
 *   series={[
 *     { id:"cur",   label:"Current",        role:"actual",   values:[...] },
 *     { id:"prior", label:"1M prior",       role:"prior",    values:[...] },
 *     { id:"scen",  label:"Bull steepener", role:"scenario", values:[...] },
 *   ]}
 *   annotations={[
 *     { type:"range",   from:4, to:6, label:"2Y–5Y belly" },
 *     { type:"bracket", from:4, to:6, at:4.4, label:"Belly +11–13 bp vs 1M" },
 *   ]}
 *   selected={{ seriesId:"cur", index:8 }} onSelect={setSelection} />
 */
export type SeriesRole = "actual" | "prior" | "benchmark" | "scenario" | "target" | "estimated" | "positive" | "negative";

export interface TraceSeries {
  id: string;
  /** Direct-label text — kept short; the value is appended automatically. */
  label: string;
  role: SeriesRole;
  /** One value per category; `null` = missing (line breaks, table says Missing). */
  values: Array<number | null>;
  hidden?: boolean;
}

export interface TraceAnnotation {
  /** `range` shades a category span · `line` draws a reference datum ·
   *  `bracket` measures a span at a value. One primary annotation per view. */
  type: "range" | "line" | "bracket";
  /** Category index (range/bracket). */
  from?: number;
  to?: number;
  /** Y value (line/bracket). */
  at?: number;
  label?: string;
}

export interface TraceChartProps extends Omit<React.HTMLAttributes<HTMLDivElement>, "onSelect"> {
  /** X categories — tenors, periods, buckets. */
  categories: string[];
  series: TraceSeries[];
  /** Model interval band: `upper`/`lower` (90%) and optional `innerUpper`/`innerLower` (50%). */
  band?: { upper: number[]; lower: number[]; innerUpper?: number[]; innerLower?: number[] };
  annotations?: TraceAnnotation[];
  /** Force a shared domain across charts that must compare (plates do this for you). */
  yDomain?: [number, number];
  /** Major tick count — 4–6. */
  tickCount?: number;
  yFormat?: (v: number) => string;
  /** Small-caps unit declared once, above the axis. */
  yUnit?: string;
  xUnit?: string;
  height?: number;
  /** Endpoint labels; off only when an adjacent key is unavoidable (>4 series). */
  directLabels?: boolean;
  selected?: { seriesId?: string; index: number } | null;
  onSelect?: (sel: { seriesId?: string; index: number }) => void;
  /** Accessible name + table caption. Write it as the claim, not the topic. */
  description?: string;
  /** Visually-hidden exact-value table. Leave on. */
  tableFallback?: boolean;
}
export declare function TraceChart(props: TraceChartProps): JSX.Element;
