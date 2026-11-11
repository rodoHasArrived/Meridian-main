import * as React from "react";

export interface PeerMetric {
  id?: string | number;
  label: string;
  value: number | null;
  peers?: number[];
  /** Names for `peers`, by position. Unnamed peers read as "Peer <position>". */
  peerLabels?: string[];
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
  /** Names the group and emits a subject/median/rank/peer-values table. */
  ariaLabel?: string;
  /**
   * Each rail with peers is a button that opens the list of peer values. false renders a
   * static strip; the table still carries the values. @default true
   */
  peerDetail?: boolean;
  caption?: string;
}
export declare function PeerComparisonStrip(props: PeerComparisonStripProps): React.ReactElement;
