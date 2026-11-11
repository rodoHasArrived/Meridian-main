// Meridian chart-interaction layer — crosshair/tooltip/synced-cursor machinery. Pure state +
// geometry helpers; charts stay declarative. Pixel-based since the real-pixel frame rewrite:
// pass the SAME padLeft/rail you gave the chart (ChartFrame defaults: padLeft 10, rail 62)
// and the pointer→index mapping is exact at any container width.
//
//   useChartCrosshair(count, {padLeft, rail}) → { index, bind, clear }
//   ChartCursorSync / useSyncedCursor         → broadcast one cursor index across stacked charts
import React from "react";

function indexAt(e, count, padLeft, rail) {
  const rect = e.currentTarget.getBoundingClientRect();
  const span = rect.width - padLeft - rail;
  if (span <= 0) return null;
  const t = (e.clientX - rect.left - padLeft) / span;
  const i = Math.round(t * (count - 1));
  return Math.max(0, Math.min(count - 1, i));
}

// Maps a pointer position over a plot to the nearest data index. `bind` spreads onto the
// element that wraps the chart (it supplies position:relative for tooltip anchoring).
export function useChartCrosshair(count, { padLeft = 10, rail = 62 } = {}) {
  const [index, setIndex] = React.useState(null);
  const onMove = React.useCallback((e) => {
    const i = indexAt(e, count, padLeft, rail);
    if (i != null) setIndex(i);
  }, [count, padLeft, rail]);
  const clear = React.useCallback(() => setIndex(null), []);
  return {
    index,
    setIndex,
    clear,
    bind: { onMouseMove: onMove, onMouseLeave: clear, style: { position: "relative" } },
  };
}

// Broadcast a single cursor index across any number of charts inside the provider — hover
// one, all show the crosshair at the same bar. The classic multi-pane workstation move.
const CursorContext = React.createContext(null);

export function ChartCursorSync({ children }) {
  const [index, setIndex] = React.useState(null);
  const value = React.useMemo(() => ({ index, setIndex }), [index]);
  return React.createElement(CursorContext.Provider, { value }, children);
}

// Inside a ChartCursorSync, returns { index, bind, clear } wired to the shared cursor.
// Standalone (no provider), falls back to a local crosshair so charts work either way.
export function useSyncedCursor(count, opts) {
  const ctx = React.useContext(CursorContext);
  const local = useChartCrosshair(count, opts);
  if (!ctx) return local;
  const padLeft = (opts && opts.padLeft) ?? 10, rail = (opts && opts.rail) ?? 62;
  const onMove = (e) => {
    const i = indexAt(e, count, padLeft, rail);
    if (i != null) ctx.setIndex(i);
  };
  return {
    index: ctx.index,
    setIndex: ctx.setIndex,
    clear: () => ctx.setIndex(null),
    bind: { onMouseMove: onMove, onMouseLeave: () => ctx.setIndex(null), style: { position: "relative" } },
  };
}

// Capitalized carrier so the crosshair hooks surface on window.<Namespace> alongside the
// already-capitalized ChartCursorSync (the compiler only exposes capital-initial exports;
// React hooks stay lowercase). Consume as:
//   const { useChartCrosshair, useSyncedCursor, ChartCursorSync } =
//     window.MeridianDesignSystem_205d17.ChartCursor;
export const ChartCursor = { ChartCursorSync, useChartCrosshair, useSyncedCursor };
