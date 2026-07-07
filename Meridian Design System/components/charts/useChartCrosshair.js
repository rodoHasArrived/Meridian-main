// Meridian chart-interaction layer — the crosshair/tooltip/synced-cursor machinery the SVG
// charts were missing. Pure state + geometry helpers; charts stay declarative.
//
//   useChartCrosshair(count)         → { index, bind, clear } — pointer→nearest-index tracking
//   ChartCursorSync / useSyncedCursor → broadcast one cursor index across stacked charts
import React from "react";

// Maps a pointer position over a plot to the nearest data index. `bind` spreads onto the
// element that wraps the SVG (needs position:relative and a known plot inset).
export function useChartCrosshair(count, { plotLeft = 0.05, plotRight = 0.93 } = {}) {
  const [index, setIndex] = React.useState(null);
  const onMove = React.useCallback((e) => {
    const rect = e.currentTarget.getBoundingClientRect();
    const frac = (e.clientX - rect.left) / rect.width;
    const t = (frac - plotLeft) / (plotRight - plotLeft);
    const i = Math.round(t * (count - 1));
    setIndex(Math.max(0, Math.min(count - 1, i)));
  }, [count, plotLeft, plotRight]);
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
  const onMove = (e) => {
    const rect = e.currentTarget.getBoundingClientRect();
    const frac = (e.clientX - rect.left) / rect.width;
    const pl = (opts && opts.plotLeft) ?? 0.05, pr = (opts && opts.plotRight) ?? 0.93;
    const t = (frac - pl) / (pr - pl);
    const i = Math.round(t * (count - 1));
    ctx.setIndex(Math.max(0, Math.min(count - 1, i)));
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
//     window.MeridianDesignSystem_4f61be.ChartCursor;
export const ChartCursor = { ChartCursorSync, useChartCrosshair, useSyncedCursor };
