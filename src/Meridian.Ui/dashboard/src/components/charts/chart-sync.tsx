// Per-screen chart synchronization. Charts stay stateless and emit crosshair /
// activation events; a screen wraps its linked charts in a ChartSyncProvider so a
// crosshair or selection on one chart is reflected on the others. State is
// normalized on the TIMESTAMP, not the point index, so charts with different
// x-domains (e.g. a candle series and an equity curve sampled at different rates)
// stay aligned. Scope this per screen — never globally.
import { createContext, useCallback, useContext, useMemo, useState, type ReactNode } from "react";

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
 * into a per-chart point index. Ties resolve to the earlier index.
 *
 * `timestamps` is assumed ascending (chart x-axes are chronological), which lets
 * this run in O(log N) — it is called per pointer-move frame per synced chart.
 */
export function nearestTimestampIndex(timestamps: number[], target: number | null): number | null {
  if (target == null || timestamps.length === 0) {
    return null;
  }
  let low = 0;
  let high = timestamps.length - 1;
  while (low < high) {
    const mid = Math.floor((low + high) / 2);
    if (timestamps[mid] === target) {
      return mid;
    }
    if (timestamps[mid] < target) {
      low = mid + 1;
    } else {
      high = mid;
    }
  }
  // `low` is the first candidate at or after the target; the earlier neighbour can
  // be closer (and wins ties).
  if (low > 0 && Math.abs(timestamps[low - 1] - target) <= Math.abs(timestamps[low] - target)) {
    return low - 1;
  }
  return low;
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
  const { hoveredTimestamp, setHoveredTimestamp, setSelectedTimestamp } = useChartSync();
  const onActivate = options?.onActivate;
  const crosshairIndex = useMemo(
    () => nearestTimestampIndex(timestamps, hoveredTimestamp),
    [timestamps, hoveredTimestamp]
  );

  // Stable identities so downstream charts don't re-render on every sync update.
  const onCrosshairChange = useCallback(
    (index: number | null) => {
      setHoveredTimestamp(index == null ? null : timestamps[index] ?? null);
    },
    [setHoveredTimestamp, timestamps]
  );

  const onPointActivate = useCallback(
    (index: number) => {
      const timestamp = timestamps[index] ?? null;
      setSelectedTimestamp(timestamp);
      if (timestamp != null) {
        onActivate?.(index, timestamp);
      }
    },
    [setSelectedTimestamp, timestamps, onActivate]
  );

  return { crosshairIndex, onCrosshairChange, onPointActivate };
}
