/**
 * DiffView — before → after field changes for review/audit rails. Each change renders as a
 * row: small-caps field label, struck red-washed "before", arrow, green-washed "after".
 * Rows where before equals after render unwashed (context rows). Empty/null values show "—".
 *
 * @example
 * <DiffView changes={[
 *   { field: "Threshold", before: "-5.0%", after: "-8.0%" },
 *   { field: "Channels", before: "email", after: "email · pager" },
 *   { field: "Owner", before: "r.alvarez", after: "r.alvarez" },   // unchanged context row
 * ]} />
 */
export interface DiffChange {
  /** Field label (small-caps). */
  field: string;
  before?: React.ReactNode;
  after?: React.ReactNode;
}
export interface DiffViewProps extends React.HTMLAttributes<HTMLDivElement> {
  changes: DiffChange[];
  /** Shown when `changes` is empty. @default "No changes" */
  emptyLabel?: React.ReactNode;
}
export declare function DiffView(props: DiffViewProps): JSX.Element;
