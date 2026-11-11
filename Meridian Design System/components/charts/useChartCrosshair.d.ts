import * as React from "react";
export interface ChartCrosshairOptions { padLeft?: number; rail?: number; }
export interface ChartCrosshairState {
  index: number | null;
  setIndex: React.Dispatch<React.SetStateAction<number | null>>;
  clear: () => void;
  bind: { onMouseMove: React.MouseEventHandler<HTMLElement>; onMouseLeave: React.MouseEventHandler<HTMLElement>; style: React.CSSProperties };
}
/** Shared cursor state for charts in one synchronized group. */
export declare function ChartCursorSync(props: { children?: React.ReactNode }): React.ReactElement;
/** Carrier for the internal hooks; use inside React function components. */
export declare const ChartCursor: {
  ChartCursorSync: typeof ChartCursorSync;
  useChartCrosshair: (count: number, options?: ChartCrosshairOptions) => ChartCrosshairState;
  useSyncedCursor: (count: number, options?: ChartCrosshairOptions) => ChartCrosshairState;
};
