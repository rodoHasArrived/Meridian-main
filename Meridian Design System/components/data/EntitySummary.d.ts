/** Identity fact band — label-over-value grid with per-item mono/color control. */
export interface EntitySummaryItem {
  label: string;
  value: React.ReactNode;
  /** Set false for prose values (names, descriptions). @default true */
  mono?: boolean;
  /** Override value color, e.g. "#26BF86" for healthy. */
  color?: string;
  /** Depth tier — `"full"` facets withdraw at summary depth. Identity facets (name, id,
   *  version, condition) stay untiered: they never withdraw. */
  tier?: "summary" | "full";
}
export interface EntitySummaryProps {
  items: EntitySummaryItem[];
  /** @default 3 */
  columns?: number;
  /** Reading depth. At `"summary"`, `tier: "full"` facets withdraw and the band states
   *  how many it withheld. @default "full" */
  detail?: "summary" | "full";
  /** Wire the withheld-facets reveal — usually `setDetailFor(region, level)`. */
  onReveal?: (level: "summary" | "full") => void;
}
export declare function EntitySummary(props: EntitySummaryProps): JSX.Element;
