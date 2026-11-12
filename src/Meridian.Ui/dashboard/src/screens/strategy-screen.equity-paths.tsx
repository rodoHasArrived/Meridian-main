import { useEffect, useMemo } from "react";
import type { ReactNode } from "react";
import {
  CompareSheet,
  CompareTray,
  SelectionCompareProvider,
  useSelectionCompare
} from "@/components/charts/SelectionCompare";
import type { CompareSheetColumn } from "@/components/charts/SelectionCompare";
import { useStrategyEquityPathCompare } from "@/screens/strategy-screen.equity-paths.view-model";
import type { StrategyRunRecord } from "@/types";

/** Drops a run from the equity-path set when the run library no longer lists it. */
function EquityPathCompareRowSync({ runIds }: { runIds: readonly string[] }) {
  const { items, remove } = useSelectionCompare();
  useEffect(() => {
    for (const item of items) {
      if (!runIds.includes(item.id)) {
        remove(item.id);
      }
    }
  }, [items, remove, runIds]);
  return null;
}

/**
 * Equity paths on one scale for the Strategy run library: the design-system SelectionCompare
 * tray and inline sheet, wired the way the package's strategy-runs template wires them. The set
 * (up to six runs) is visual only and independent of the two-run compare / diff / promote
 * selection. Curves are read when the chart is opened, not before.
 */
export function StrategyRunEquityPathCompare({ runs, children }: { runs: StrategyRunRecord[]; children: ReactNode }) {
  const equityPaths = useStrategyEquityPathCompare(runs);
  const runIds = useMemo(() => runs.map((run) => run.id), [runs]);
  const { viewFor } = equityPaths;
  const tableColumns = useMemo<CompareSheetColumn[]>(() => [
    { id: "curve-dates", label: "Curve dates", render: (item) => viewFor(item.id).curveDatesText },
    { id: "curve-points", label: "Curve points", numeric: true, render: (item) => viewFor(item.id).curvePointsText },
    { id: "initial-equity", label: "Initial equity (reported)", numeric: true, render: (item) => viewFor(item.id).initialEquityText },
    { id: "final-equity", label: "Final equity (reported)", numeric: true, render: (item) => viewFor(item.id).finalEquityText }
  ], [viewFor]);

  return (
    <SelectionCompareProvider
      max={equityPaths.maxRuns}
      categories={equityPaths.categories}
      format={equityPaths.formatIndex}
      onChange={equityPaths.onCompareSetChange}
      onOpenChange={equityPaths.onSheetOpenChange}
    >
      <EquityPathCompareRowSync runIds={runIds} />
      <CompareTray
        label="runs to compare"
        emptyLabel={`Tick ▢ Chart on two or more runs to compare their equity paths on one scale (up to ${equityPaths.maxRuns}). This set only draws a chart; it is separate from the two-run selection used by Compare, Diff and Promote.`}
        regionLabel="Equity path comparison set"
        compareLabel={(count) => count >= 2 ? `Chart ${count} equity paths` : "Chart equity paths"}
        compareDisabledReason="Tick ▢ Chart on at least two runs to chart their equity paths."
        removeLabel={(item) => `Remove ${item.title} from the equity path set`}
        fullLabel={(max) => `Set is full (${max}). Remove a run to chart another.`}
      />
      <CompareSheet
        mode="inline"
        title="Equity paths on one scale"
        footerLabel="Total return"
        footerValue={equityPaths.totalReturnFor}
        seriesFor={equityPaths.seriesFor}
        categoryLabels={equityPaths.categoryLabels}
        tableColumns={tableColumns}
        itemColumnLabel="Run"
        closeLabel="Close equity path chart"
        basis={(
          <ul className="list-disc space-y-1 pl-5">
            <li>{equityPaths.basisText.value}</li>
            <li>{equityPaths.basisText.category}</li>
            <li>{equityPaths.basisText.footer}</li>
            <li>{equityPaths.basisText.asOf}</li>
          </ul>
        )}
      />
      {children}
    </SelectionCompareProvider>
  );
}
