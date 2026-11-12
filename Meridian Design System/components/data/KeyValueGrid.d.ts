import type React from "react";
/** Label-over-value fact grid used in entity summaries and detail panes. */
export interface KeyValueGridProps {
  items: {
    label: string;
    value: React.ReactNode;
    /** Depth tier — `"full"` fields withdraw at summary depth. */
    tier?: "summary" | "full";
  }[];
  /** @default 2 */
  columns?: number;
  /** Reading depth. At `"summary"`, `tier: "full"` fields withdraw and the grid states
   *  how many it withheld. @default "full" */
  detail?: "summary" | "full";
  /** Wire the withheld-fields reveal — usually `setDetailFor(region, level)`. */
  onReveal?: (level: "summary" | "full") => void;
}
export declare function KeyValueGrid(props: KeyValueGridProps): React.JSX.Element;
