/**
 * Figure — the parametric analytical figure. One frame, four geometries, series
 * assigned by **role** (`actual · prior · benchmark · scenario · target ·
 * estimated`) — color is decided by `FIGURE_ROLES` and nowhere else.
 *
 * Use it for a **new** ladder / plate / distribution / trace instead of adding another
 * named chart. It does not supersede the existing domain ladders and matrices, which
 * carry bucket-definition, comparison-mode and envelope contracts Figure does not model
 * (4.1 · D-01). Wrap it in `AnalyticalFigure` for the published evidence contract.
 *
 * Geometries replace families of one-off charts:
 * - `"ladder"` — signed magnitude across ordered buckets (cash-flow ladder,
 *   maturity ladder, schedule ladder, income schedule, duration contribution).
 * - `"plate"` — labelled comparison matrix (basis comparison, rollforward,
 *   comparison plate).
 * - `"trace"` — series over an ordered axis, with optional uncertainty `band`.
 * - `"distribution"` — bucketed counts (histogram).
 *
 * `FigureSet` renders small multiples of one geometry. Law:
 * `guidelines/FIGURE_PRIMITIVES.md`; series ledger: `guidelines/VISUALIZATION_GRAMMAR.md`.
 *
 * @example
 * <Figure geometry="ladder" title="Contractual cash flows" clocks="As of 2026-06-30"
 *   series={[{ role: "actual", points: [{ label: "0–1y", value: 12400 }, { label: "1–3y", value: -3100 }] }]} />
 * <Figure geometry="trace" series={[
 *   { role: "actual", label: "Observed", points: pts },
 *   { role: "scenario", label: "+100bp", points: shocked, band: bands },
 * ]} />
 * <Figure geometry="plate" rows={rows} columns={[{ label: "IFRS", role: "actual" }, { label: "US GAAP", role: "prior" }]} cells={cells} />
 */
export type FigureRole = "actual" | "prior" | "benchmark" | "scenario" | "target" | "estimated";
export type FigureGeometry = "ladder" | "plate" | "trace" | "distribution";

export interface FigurePoint { label?: string; value: number; role?: FigureRole }
export interface FigureSeries {
  role?: FigureRole;
  label?: string;
  points?: FigurePoint[];
  /** Uncertainty band as `[lo, hi]` per point — model uncertainty only, never data condition. */
  band?: [number, number][];
}
export interface PlateRow { key?: string; label: string }
export interface PlateColumn { key?: string; label: string; role?: FigureRole }

export interface FigureProps extends React.HTMLAttributes<HTMLElement> {
  geometry?: FigureGeometry;
  series?: FigureSeries[];
  /** plate geometry only. */
  rows?: PlateRow[];
  columns?: PlateColumn[];
  cells?: React.ReactNode[][];
  title?: string;
  /** The clocks the figure is stated in — required on any dated figure. */
  clocks?: string;
  legend?: boolean;
  width?: number;
  height?: number;
  /** Ladder: keep a zero rule with negatives below it (default true). */
  zero?: boolean;
  /** Small-multiple sizing. */
  small?: boolean;
}
export declare function Figure(props: FigureProps): JSX.Element;

export interface FigureSetProps extends React.HTMLAttributes<HTMLDivElement> {
  figures: FigureProps[];
  columns?: number;
}
export declare function FigureSet(props: FigureSetProps): JSX.Element;

export declare const FIGURE_ROLES: Record<FigureRole, { label: string; stroke: string; dash: string | null; fill: string }>;
export declare const GEOMETRIES: FigureGeometry[];
