// Per-screen chart synchronization. Charts stay stateless and emit crosshair /
// activation events; a screen wraps its linked charts in a ChartSyncProvider so a
// crosshair or selection on one chart is reflected on the others. State is
// normalized on the TIMESTAMP, not the point index, so charts with different
// x-domains (e.g. a candle series and an equity curve sampled at different rates)
// stay aligned. Scope this per screen — never globally.
import { createContext, useContext, useMemo, useState, type ReactNode } from "react";

export interface ChartSyncState {
  /** Timestamp (epoch ms) currently hovered across the synced charts, or null. */
  hoveredTimestamp: number | null;
  /** Timestamp (epoch ms) of the activated/selected point, or null. */
  selectedTimestamp: number | null;
}

export interface ChartSyncApi extends ChartSyncState {
  setHoveredTimestamp: (timestamp: number | null) => void;
  setSelectedTimestamp: (timestamp: number | null) => void;
}

const ChartSyncContext = createContext<ChartSyncApi | null>(null);

export function ChartSyncProvider({ children }: { children: ReactNode }) {
  const [hoveredTimestamp, setHoveredTimestamp] = useState<number | null>(null);
  const [selectedTimestamp, setSelectedTimestamp] = useState<number | null>(null);
  const value = useMemo<ChartSyncApi>(
    () => ({ hoveredTimestamp, selectedTimestamp, setHoveredTimestamp, setSelectedTimestamp }),
    [hoveredTimestamp, selectedTimestamp]
  );
  return <ChartSyncContext.Provider value={value}>{children}</ChartSyncContext.Provider>;
}

export function useChartSync(): ChartSyncApi {
  const context = useContext(ChartSyncContext);
  if (!context) {
    throw new Error("useChartSync must be used within a ChartSyncProvider");
  }
  return context;
}

/**
 * Index of the timestamp closest to `target`, or null when there is no target or
 * no timestamps. Used to translate the shared (timestamp-based) sync state back
 * into a per-chart point index.
 */
export function nearestTimestampIndex(timestamps: number[], target: number | null): number | null {
  if (target == null || timestamps.length === 0) {
    return null;
  }
  let bestIndex = 0;
  let bestDistance = Infinity;
  for (let index = 0; index < timestamps.length; index++) {
    const distance = Math.abs(timestamps[index] - target);
    if (distance < bestDistance) {
      bestDistance = distance;
      bestIndex = index;
    }
  }
  return bestIndex;
}

export interface ChartCrosshairSync {
  /** Crosshair index for this chart, resolved from the shared hovered timestamp. */
  crosshairIndex: number | null;
  onCrosshairChange: (index: number | null) => void;
  onPointActivate: (index: number) => void;
}

/**
 * Bind one chart to the shared sync state. `timestamps` maps this chart's point
 * indices to epoch-ms timestamps; the returned handlers translate index events
 * into shared-timestamp updates and back, so linked charts track each other even
 * with mismatched x-domains. `onActivate` fires on point activation for the
 * screen's evidence drill.
 */
export function useChartCrosshairSync(
  timestamps: number[],
  options?: { onActivate?: (index: number, timestamp: number) => void }
): ChartCrosshairSync {
  const sync = useChartSync();
  const crosshairIndex = useMemo(
    () => nearestTimestampIndex(timestamps, sync.hoveredTimestamp),
    [timestamps, sync.hoveredTimestamp]
  );

  const onCrosshairChange = (index: number | null) => {
    sync.setHoveredTimestamp(index == null ? null : timestamps[index] ?? null);
  };

  const onPointActivate = (index: number) => {
    const timestamp = timestamps[index] ?? null;
    sync.setSelectedTimestamp(timestamp);
    if (timestamp != null) {
      options?.onActivate?.(index, timestamp);
    }
  };

  return { crosshairIndex, onCrosshairChange, onPointActivate };
}
