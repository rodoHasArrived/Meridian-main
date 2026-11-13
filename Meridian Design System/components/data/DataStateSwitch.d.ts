/**
 * DataStateSwitch — the data-state ladder (loading / empty / error / ready) as one wrapper.
 * Children render only in `ready`; every other state paints a full-surface slot instead, so a
 * screen can demonstrate all four without branching its own layout.
 *
 * @example
 * <DataStateSwitch state={dataState} label="Positions" actionLabel="Retry" onAction={reload}>
 *   <PositionsTable rows={rows} />
 * </DataStateSwitch>
 */
import React from "react";
export type DataState = "ready" | "loading" | "empty" | "error";
export interface DataStateSwitchProps {
  /** @default "ready" */
  state?: DataState;
  /** Name of the surface, used in the slot copy. @default "This surface" */
  label?: string;
  /** Skeleton row count in `loading`. @default 8 */
  rows?: number;
  /** Skeleton column count in `loading`. @default 6 */
  columns?: number;
  /** Overrides the default explanatory line. */
  detail?: string;
  /** Recovery action label; omitted = no button. */
  actionLabel?: string;
  onAction?: () => void;
  children?: React.ReactNode;
  /** 3.5 · Announcement for NON-ready states only — ready children are never wrapped in a live
   *  region. @default assertive for error, polite otherwise */
  announce?: "polite" | "assertive" | "off";
}
export function DataStateSwitch(props: DataStateSwitchProps): React.ReactElement;
