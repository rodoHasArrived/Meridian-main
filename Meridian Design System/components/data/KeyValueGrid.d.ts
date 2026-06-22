/** Label-over-value fact grid used in entity summaries and detail panes. */
export interface KeyValueGridProps {
  items: { label: string; value: React.ReactNode }[];
  /** @default 2 */
  columns?: number;
}
export declare function KeyValueGrid(props: KeyValueGridProps): JSX.Element;
