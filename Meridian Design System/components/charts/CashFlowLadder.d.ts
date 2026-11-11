export interface CashFlowBucket {
  id?: string;
  label: string;
  /** Gross inflow for the bucket. */
  assets: number;
  /** Gross outflow for the bucket. */
  liabilities: number;
  /** Net gap. Defaults to assets − liabilities. */
  net?: number;
  /** Scenario series, drawn dashed against the base bars. */
  scenarioAssets?: number;
  scenarioLiabilities?: number;
  /** Amount whose timing is unknown or unscheduled — kept visible, never netted away. */
  unscheduled?: number;
  /** One of the eleven data conditions. */
  condition?: string;
  /** Shown in the last column when `cumulative` is off. */
  coverage?: React.ReactNode;
  /** Bucket definition note under the label. */
  detail?: React.ReactNode;
}
export interface CashFlowLadderProps extends Omit<React.HTMLAttributes<HTMLDivElement>, "onSelect"> {
  items: CashFlowBucket[];
  valueFormat?: (value: number) => React.ReactNode;
  assetLabel?: string;
  liabilityLabel?: string;
  scenarioLabel?: string;
  showScenario?: boolean;
  /** Stated on the face — never silently combine currencies. */
  currency?: string;
  /** "Nominal" or "Present value" — never mix without an explicit control. */
  basis?: string;
  /** How the buckets are defined, e.g. "Settlement date, calendar quarters". */
  bucketDefinition?: React.ReactNode;
  asOf?: React.ReactNode;
  source?: React.ReactNode;
  coverage?: React.ReactNode;
  /** Show the running cumulative gap in the last column. */
  cumulative?: boolean;
  /** Keep the summary table visible (default). When false it stays available to assistive tech and returns at ≤640px. */
  showTable?: boolean;
  selectedId?: string | null;
  onSelect?: (bucket: CashFlowBucket, index: number) => void;
  description?: string;
}
export declare function CashFlowLadder(props: CashFlowLadderProps): JSX.Element;
