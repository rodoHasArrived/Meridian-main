export type SeriesRole = "actual" | "prior" | "benchmark" | "scenario" | "target" | "estimated" | "positive" | "negative";
export interface SeriesRoleStyle { stroke: string; width: string; dash: string; marker: "filled" | "open" | "none"; }
/** Semantic series roles; values are CSS token references, not resolved colors. */
export declare const ROLES: Record<SeriesRole, SeriesRoleStyle>;
/** Legacy chart-display formatter carrier. Values must be finite and correctly scaled. */
export declare const Fmt: {
  price: (value: number) => string;
  currency: (value: number) => string;
  bp: (value: number) => string;
  volume: (value: number) => string;
  date: (value: Date | string | number) => string;
};
