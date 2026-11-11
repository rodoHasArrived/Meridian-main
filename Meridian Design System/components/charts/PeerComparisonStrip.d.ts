import * as React from "react";

export interface PeerMetric {
  id?: string | number;
  label: string;
  value: number | null;
  peers?: number[];
  /** Independent domain for this metric, not a shared unit across rows. */
  domain?: [number, number];
  median?: number | null;
  fmt?: (value: number) => string;
  /** Omit when "better" is not meaningful. false ranks lower values first. */
  higherIsBetter?: boolean;
  note?: string;
}
export interface PeerComparisonStripProps {
  metrics?: PeerMetric[];
  /** @default "Subject" */
  subjectLabel?: string;
  /** Label column width in pixels. @default 168 */
  labelWidth?: number;
  /** Value column width in pixels. @default 92 */
  valueWidth?: number;
  /** @default true */
  showRank?: boolean;
  fmt?: (value: number) => string;
  /** Names the group and emits a subject/median/rank table. */
  ariaLabel?: string;
  caption?: string;
}
export declare function PeerComparisonStrip(props: PeerComparisonStripProps): React.ReactElement;
