import { ArrowUpDown, ChevronRight } from "lucide-react";
import { useDeferredValue, useMemo, useState } from "react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { RunStatusBadge } from "@/components/meridian/run-status-badge";
import type { ResearchRunRecord } from "@/types";

interface EntityDataTableProps {
  rows: ResearchRunRecord[];
  onSelectRun: (run: ResearchRunRecord) => void;
}

type SortKey = "strategyName" | "engine" | "pnl" | "lastUpdated";

export function EntityDataTable({ rows, onSelectRun }: EntityDataTableProps) {
  const [query, setQuery] = useState("");
  const [sortKey, setSortKey] = useState<SortKey>("lastUpdated");
  const deferredQuery = useDeferredValue(query);

  const filteredRows = useMemo(() => {
    const normalized = deferredQuery.trim().toLowerCase();
    const baseRows = normalized
      ? rows.filter((row) =>
          [row.strategyName, row.engine, row.dataset, row.status, row.mode].some((value) =>
            value.toLowerCase().includes(normalized)
          )
        )
      : rows;

    return [...baseRows].sort((left, right) => {
      const leftValue = left[sortKey];
      const rightValue = right[sortKey];
      return rightValue.localeCompare(leftValue);
    });
  }, [deferredQuery, rows, sortKey]);

  return (
    <Card>
      <CardHeader className="flex flex-col gap-4 md:flex-row md:items-end md:justify-between">
        <div className="space-y-1">
          <CardTitle>Research runs</CardTitle>
          <CardDescription>Filter, sort, and drill into active and recent strategy runs.</CardDescription>
        </div>
        <div className="flex flex-col gap-3 sm:flex-row">
          <Input
            aria-label="Filter runs"
            value={query}
            placeholder="Filter by strategy, dataset, mode, or status"
            onChange={(event) => setQuery(event.target.value)}
            className="sm:w-72"
          />
          <Button variant="outline" onClick={() => setSortKey(nextSort(sortKey))}>
            <ArrowUpDown className="mr-2 h-4 w-4" />
            Sort: {sortLabel(sortKey)}
          </Button>
        </div>
      </CardHeader>
      <CardContent>
        <div className="data-grid-surface overflow-x-auto p-2">
          <table className="min-w-full border-separate border-spacing-y-2 text-left text-sm">
            <thead className="text-xs uppercase tracking-[0.16em] text-muted-foreground">
              <tr>
                <th className="px-4 py-2 font-medium">Strategy</th>
                <th className="px-4 py-2 font-medium">Engine</th>
                <th className="px-4 py-2 font-medium">Status</th>
                <th className="px-4 py-2 font-medium">Dataset</th>
                <th className="px-4 py-2 font-medium">Window</th>
                <th className="px-4 py-2 font-medium">P&amp;L</th>
                <th className="px-4 py-2 font-medium">Sharpe</th>
                <th className="px-4 py-2 font-medium">Updated</th>
                <th className="px-4 py-2 font-medium text-right">Details</th>
              </tr>
            </thead>
            <tbody>
              {filteredRows.map((row) => (
                <tr key={row.id} className="rounded-xl bg-background/55 shadow-sm">
                  <td className="rounded-l-xl px-4 py-4">
                    <div className="font-semibold">{row.strategyName}</div>
                  </td>
                  <td className="px-4 py-4">{row.engine}</td>
                  <td className="px-4 py-4">
                    <RunStatusBadge status={row.status} mode={row.mode} />
                  </td>
                  <td className="px-4 py-4 text-muted-foreground">{row.dataset}</td>
                  <td className="px-4 py-4 text-muted-foreground">{row.window}</td>
                  <td className="px-4 py-4 font-mono font-semibold">{row.pnl}</td>
                  <td className="px-4 py-4">{row.sharpe}</td>
                  <td className="px-4 py-4 text-muted-foreground">{row.lastUpdated}</td>
                  <td className="rounded-r-xl px-4 py-4 text-right">
                    <Button variant="ghost" size="sm" onClick={() => onSelectRun(row)}>
                      Open
                      <ChevronRight className="ml-1 h-4 w-4" />
                    </Button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        {filteredRows.length === 0 ? (
          <div className="mt-6 rounded-xl border border-dashed border-border px-4 py-10 text-center text-muted-foreground">
            No runs match the current filter.
          </div>
        ) : null}
      </CardContent>
    </Card>
  );
}

function nextSort(current: SortKey): SortKey {
  if (current === "lastUpdated") {
    return "strategyName";
  }

  if (current === "strategyName") {
    return "engine";
  }

  if (current === "engine") {
    return "pnl";
  }

  return "lastUpdated";
}

function sortLabel(sortKey: SortKey) {
  return sortKey === "lastUpdated" ? "Last Updated" : sortKey === "strategyName" ? "Strategy" : sortKey === "engine" ? "Engine" : "P&L";
}
