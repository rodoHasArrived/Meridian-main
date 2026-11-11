export type MaturityMode = "amount" | "percent";
export interface MaturityBucket {
  id?: string;
  /** Must match a declared bucket label, or the row is appended as undeclared. */
  label: string;
  primary: number;
  comparison: number;
  /** One of the eleven data conditions; renders as a glyph beside the primary value. */
  condition?: string;
  /** Force unknown-maturity treatment; also inferred from labels like "Unknown". */
  unknown?: boolean;
  detail?: string;
  [key: string]: unknown;
}
export interface MaturityLadderProps extends Omit<React.HTMLAttributes<HTMLDivElement>, "onSelect"> {
  items: MaturityBucket[];
  /** Declared bucket order. Every bucket renders, at zero if unpopulated. Defaults to MATURITY_BUCKETS. */
  buckets?: string[];
  primaryLabel?: string;
  comparisonLabel?: string;
  /** States what is being compared, e.g. "Portfolio versus benchmark", "Assets versus liabilities". */
  comparisonMode?: string;
  /** Controlled amount/percent mode. Modes are never mixed in one reading. */
  mode?: MaturityMode;
  defaultMode?: MaturityMode;
  onModeChange?: (mode: MaturityMode) => void;
  valueFormat?: (value: number) => React.ReactNode;
  currency?: string;
  /** What the exposure is measured in, e.g. "Market value", "Notional". */
  measure?: string;
  asOf?: string;
  source?: string;
  coverage?: string;
  /** How buckets are defined, e.g. "By effective maturity, call-adjusted". */
  bucketDefinition?: string;
  selectedId?: string | number | null;
  onSelect?: (bucket: MaturityBucket, index: number) => void;
  /** False hides the table visually but keeps it for assistive technology and narrow screens. */
  showTable?: boolean;
  /** Dashed parity marker at the comparison value. */
  showParity?: boolean;
  description?: string;
}
export declare const MATURITY_BUCKETS: string[];
export declare function MaturityLadder(props: MaturityLadderProps): JSX.Element;
