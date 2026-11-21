import * as React from "react";
export interface PostingLine { id?: string; account: string; description?: string; debit?: number; credit?: number }
export interface PostingEffect { id?: string; label: React.ReactNode; before?: React.ReactNode | number; after?: React.ReactNode | number; basis?: string }
export interface PostingCheck {
  id?: string;
  /** "blocking" makes the post action unavailable; "warning" requires acknowledgment. */
  severity: "blocking" | "warning" | "info";
  label: React.ReactNode;
  detail?: React.ReactNode;
}
export interface PostingImpactPreviewProps extends Omit<React.HTMLAttributes<HTMLElement>, "title"> {
  title?: React.ReactNode;
  subtitle?: React.ReactNode;
  /** Reporting period. */
  period?: React.ReactNode;
  /** Posting period, when it differs from the reporting period. */
  postingPeriod?: React.ReactNode;
  /** open · soft-closed · hard-closed · locked · adjustment · reopened. Anything not open, adjustment, reopened or soft blocks the post. */
  periodState?: string;
  asOf?: React.ReactNode;
  lines: PostingLine[];
  /** Reporting-basis impact, pre → post. */
  effects?: PostingEffect[];
  statements?: (string | { label: string; value?: React.ReactNode })[];
  positions?: (string | { label: string; value?: React.ReactNode })[];
  controls?: (string | { label: string; value?: React.ReactNode })[];
  downstream?: (string | { label: string; value?: React.ReactNode })[];
  /** How the entry would be reversed. */
  reversal?: React.ReactNode;
  /** Additional validations beyond balance, period, and authority. */
  checks?: PostingCheck[];
  /** Force (or suppress) the warning-acknowledgment gate. Defaults to on when warnings exist. */
  requireAcknowledgement?: boolean;
  /** false raises a blocking authority check. */
  authority?: boolean;
  onPost?: () => void;
  onCancel?: () => void;
  postLabel?: React.ReactNode;
}
export declare function PostingImpactPreview(props: PostingImpactPreviewProps): React.JSX.Element;
