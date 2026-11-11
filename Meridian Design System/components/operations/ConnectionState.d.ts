import * as React from "react";
/**
 * Connection state — the visible half of Meridian's last-known-good contract:
 * `live · delayed · partial · offline · error`. Sits next to the evidence it
 * describes (not in a corner of the chrome) and always answers *how old*, *what
 * is missing*, and *what happens next*.
 *
 * The rule it enforces: once a view has rendered valid data it never reverts to
 * an empty skeleton. Keep the last good evidence on screen, state its age, and
 * offer the recovery action.
 *
 * @example
 * <ConnectionState state="live" subject="Prices" age="42s" cadence="15s" />
 * <ConnectionState state="partial" detail="3 of 11 tenors missing" />
 * <ConnectionState state="offline" age="11:29:42 UTC" actionLabel="Reconnect" onAction={reconnect} />
 * <ConnectionState state="error" detail="Curve builder v14.7 failed — observations still valid"
 *   actionLabel="Retry" onAction={retry} block />
 */
export interface ConnectionStateProps extends React.HTMLAttributes<HTMLDivElement> {
  /** Connection condition. */
  state?: "live" | "delayed" | "partial" | "offline" | "error";
  /** What is connected ("Prices", "Custodian feed") — prefixes the label. */
  subject?: string;
  /** Age or timestamp of the last good data ("42s", "11:29:42 UTC"). */
  age?: string;
  /** Expected refresh cadence ("15s"). */
  cadence?: string;
  /** Free text — what is missing, what failed, what remains valid. */
  detail?: string;
  /** Recovery action label (default "Retry"); the button only renders with `onAction`. */
  actionLabel?: string;
  onAction?: () => void;
  /** Full-width strip instead of a fitted chip. */
  block?: boolean;
}
export declare function ConnectionState(props: ConnectionStateProps): React.JSX.Element;
