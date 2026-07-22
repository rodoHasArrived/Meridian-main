/**
 * Allocation editor — interactive split of a fixed total across editable weights, computed
 * with `Money.allocateAmount` (largest-remainder at the currency minor unit) so the parts sum
 * EXACTLY to the total — the footer proves it. Shows each line's amount and % share live as
 * weights are edited. For fee splits, cost allocations, wash-rule lot assignment.
 */
export interface AllocationLine {
  label: string;
  /** Relative weight (any non-negative number — not required to sum to anything). */
  weight: number;
}
export interface AllocationEditorProps {
  /** The fixed total being split. */
  total: number | string;
  /** Initial lines — internal weight state is seeded from these. */
  lines: AllocationLine[];
  /** @default "USD" */
  currency?: string;
  /** Minor-unit decimals for the split. @default 2 */
  decimals?: number;
  /** Fired on every weight edit with the exact amounts and current weights. */
  onChange?: (amounts: number[], weights: number[]) => void;
  /** Accessible caption / region label. */
  caption?: string;
}
export declare function AllocationEditor(props: AllocationEditorProps): JSX.Element;
