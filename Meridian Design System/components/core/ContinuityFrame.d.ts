export type ContinuityState = "live" | "delayed" | "stale" | "partial" | "offline" | "recovering" | "error";
export interface ContinuityFrameProps extends React.HTMLAttributes<HTMLElement> {
  state?: ContinuityState;
  subject?: React.ReactNode;
  snapshotAt?: React.ReactNode;
  /** Pre-formatted age. Supplied, it wins and no timer runs. */
  age?: React.ReactNode;
  /** Timestamp the retained evidence was captured (ISO string, Date, or epoch ms).
   *  The frame derives and ticks the age itself — 1s under a minute, 15s under an
   *  hour, 60s beyond — so hosts do not run their own clock. */
  since?: string | number | Date;
  coverage?: React.ReactNode;
  readOnly?: boolean;
  /** Fade retained content so it never reads as live. `"auto"` (default) dims on
   *  stale / offline / error — the same states already locked read-only. */
  dim?: boolean | "auto";
  /** Corner mark on retained content. Empty string removes it. Default "Retained". */
  retainedLabel?: React.ReactNode;
  /** Renders a retry control in the status row. Omit for no affordance. */
  onRetry?: (event: React.MouseEvent) => void;
  retryLabel?: string;
  retrying?: boolean;
}
export declare function ContinuityStatus(props: ContinuityFrameProps): JSX.Element;
export declare function ContinuityFrame(props: ContinuityFrameProps): JSX.Element;
