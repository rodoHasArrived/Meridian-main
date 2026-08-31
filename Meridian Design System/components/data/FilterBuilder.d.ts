/**
 * FilterBuilder — structured field / operator / value query rows, AND-combined. Where
 * `FilteredDataTable` does free text + facet chips, FilterBuilder composes explicit
 * predicates for registry surfaces (security master, run ledgers, report catalogs).
 * Operators follow the field type: text (contains / is / is not / starts with / is empty),
 * number (= ≠ > ≥ < ≤ between), date (on / before / after / between), enum (is / is not).
 *
 * `FilterBuilder.predicate(fields, rows)` compiles the same row shape to a `(item) => boolean`
 * test anywhere (the `onApply` callback also receives it precompiled). Incomplete rows are
 * ignored — never treated as match-nothing.
 *
 * @example
 * const FIELDS = [
 *   { key: "symbol", label: "Symbol", type: "text" },
 *   { key: "adv", label: "ADV ($)", type: "number" },
 *   { key: "listed", label: "Listed", type: "date" },
 *   { key: "class", label: "Asset class", type: "enum", options: ["Equity", "ETF", "ADR"] },
 * ];
 * <FilterBuilder fields={FIELDS} summary={`${shown} of ${total} rows`}
 *   onApply={(rows, pred) => setVisible(all.filter(pred))} />
 */
export interface FilterField {
  key: string;
  label?: string;
  /** Drives the operator set and value input. @default "text" */
  type?: "text" | "number" | "date" | "enum";
  /** Options for `type: "enum"` — strings or { value, label }. */
  options?: (string | { value: string; label: string })[];
}
export interface FilterRow {
  field: string;
  /** Operator id — e.g. "contains", "is-not", "gte", "between", "empty". */
  op: string;
  value?: string;
  /** Upper bound for "between". */
  value2?: string;
}
export interface FilterBuilderProps extends React.HTMLAttributes<HTMLDivElement> {
  fields: FilterField[];
  /** Controlled rows. */
  value?: FilterRow[];
  /** Uncontrolled initial rows. @default one blank row */
  defaultValue?: FilterRow[];
  /** Fires on every row edit. */
  onChange?: (rows: FilterRow[]) => void;
  /** Fires on Apply (and Clear) with the rows and a compiled predicate. */
  onApply?: (rows: FilterRow[], predicate: (item: Record<string, unknown>) => boolean) => void;
  /** Hide the Apply button when filtering live via `onChange`. @default true */
  showApply?: boolean;
  /** Right-aligned mono result summary, e.g. "42 of 180 rows". */
  summary?: React.ReactNode;
  /** @default "+ Add filter" */
  addLabel?: string;
}
export declare function FilterBuilder(props: FilterBuilderProps): JSX.Element;
