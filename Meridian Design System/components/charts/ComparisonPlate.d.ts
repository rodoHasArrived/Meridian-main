/**
 * Comparison plate — Meridian's standardized unit for comparing repeated
 * entities (peer securities, sovereign curves, funds, provider quality,
 * exception populations). The contract: **every plate in a set shares plot
 * size, domain, tick positions, label placement, padding, and footer
 * geometry**, so a visible difference is a real difference.
 *
 * `PlateGrid` is the normal entry point — it computes one shared domain and
 * tick set across all plates and lays them out on the plate grid. Reach for
 * `ComparisonPlate` alone only when you are supplying the shared domain
 * yourself.
 *
 * A selected plate gets a datum rail and a surface tint. It is never resized,
 * and sorting a set never changes the internal scales. Past ~12 plates, add
 * search/sort/pagination or a table plus one focused plate — do not shrink the
 * type.
 *
 * @example
 * <PlateGrid
 *   categories={["2Y","5Y","10Y","30Y"]}
 *   domain={[0, 6]} format={(v) => v.toFixed(0)}
 *   plates={[
 *     { id:"ust",  title:"United States", tag:"UST",  values:[4.20,4.24,4.45,4.81], footerLabel:"10Y change", footerValue:"+11 bp" },
 *     { id:"gilt", title:"United Kingdom", tag:"GILT", values:[4.11,4.05,4.12,4.68], footerLabel:"10Y change", footerValue:"+10 bp" },
 *   ]}
 *   selectedId={sel} onSelect={setSel} />
 */
export interface PlateSpec {
  id: string;
  title: string;
  /** Right-aligned mono code — market, ticker, entity id. */
  tag?: string;
  values: Array<number | null>;
  /** Per-plate override of the grid categories. */
  categories?: string[];
  role?: "actual" | "prior" | "benchmark" | "scenario" | "target" | "estimated";
  /** Footer metric — same label across the set. */
  footerLabel?: string;
  footerValue?: string;
}

export interface PlateGridProps extends Omit<React.HTMLAttributes<HTMLDivElement>, "onSelect"> {
  plates: PlateSpec[];
  categories: string[];
  /** Shared y domain. Omit to compute one across every plate — never per plate. */
  domain?: [number, number];
  tickCount?: number;
  format?: (v: number) => string;
  role?: PlateSpec["role"];
  selectedId?: string;
  onSelect?: (id: string) => void;
}
export declare function PlateGrid(props: PlateGridProps): JSX.Element;

export interface ComparisonPlateProps extends Omit<React.HTMLAttributes<HTMLElement>, "onSelect"> {
  title: string;
  tag?: string;
  categories: string[];
  values: Array<number | null>;
  role?: PlateSpec["role"];
  /** Required: the domain shared by the whole set. */
  domain: [number, number];
  /** Shared tick values — pass the set's ticks so gridlines align across plates. */
  ticks?: number[];
  format?: (v: number) => string;
  footerLabel?: string;
  footerValue?: string;
  /** Plot height; defaults to `--plate-plot-h`. Keep it identical across a set. */
  height?: number;
  selected?: boolean;
  onSelect?: () => void;
}
export declare function ComparisonPlate(props: ComparisonPlateProps): JSX.Element;
