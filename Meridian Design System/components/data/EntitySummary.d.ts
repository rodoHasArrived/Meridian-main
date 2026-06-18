/** Identity fact band — label-over-value grid with per-item mono/color control. */
export interface EntitySummaryItem {
  label: string;
  value: React.ReactNode;
  /** Set false for prose values (names, descriptions). @default true */
  mono?: boolean;
  /** Override value color, e.g. "#26BF86" for healthy. */
  color?: string;
}
export interface EntitySummaryProps {
  items: EntitySummaryItem[];
  /** @default 3 */
  columns?: number;
}
export declare function EntitySummary(props: EntitySummaryProps): JSX.Element;
