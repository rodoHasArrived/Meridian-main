import { useCallback, useMemo, useRef, useState } from "react";
import * as api from "@/lib/api";
import type {
  CellExecuteResult,
  CellExecutionContext,
  CellExecutionState,
  CellKind,
  CellOutput,
  CellSnippet,
  DataFetchRequest,
  DataFetchResult,
  NotebookCell
} from "@/types";

// ── Snippet library (extensible) ──────────────────────────────────────────────

export const builtInSnippets: CellSnippet[] = [
  {
    id: "snippet.markdown.note",
    label: "Markdown note",
    description: "Narrative cell with title and bullets.",
    kind: "markdown",
    source: "## Hypothesis\n\n- Define the signal\n- Define the entry / exit\n- Define the risk guard"
  },
  {
    id: "snippet.code.fetchPrices",
    label: "Fetch prices",
    description: "Pull price bars for the active context symbol.",
    kind: "code",
    source: "// Pull prices for the active context\nvar prices = await Data.Prices(ContextSymbol, ContextFrom, ContextTo);\nPrint($\"Loaded {prices.Count} bars for {ContextSymbol}\");"
  },
  {
    id: "snippet.code.sma",
    label: "Simple moving average",
    description: "Compute a rolling SMA and print the last value.",
    kind: "code",
    source: "var window = Param<int>(\"window\", 20, 2, 200, \"SMA window\");\nvar sma = prices.Select(p => p.Close).RollingAverage(window);\nPrint($\"SMA({window}) last = {sma.Last():F2}\");"
  },
  {
    id: "snippet.code.backtest",
    label: "Backtest skeleton",
    description: "Single-symbol backtest with bar-by-bar callback.",
    kind: "code",
    source: "var result = await Backtest\n    .WithSymbols(ContextSymbol)\n    .From(ContextFrom).To(ContextTo)\n    .WithInitialCash(100_000m)\n    .OnBar((bar, ctx) =>\n    {\n        // entry/exit logic here\n    })\n    .RunAsync();\n\nPrintMetric(\"sharpe\", result.Sharpe);\nPrintMetric(\"return\", result.TotalReturn);"
  },
  {
    id: "snippet.code.signal",
    label: "Signal expression",
    description: "Boolean signal evaluation with PrintMetric output.",
    kind: "code",
    source: "var signal = prices.Last().Close > sma.Last();\nPrintMetric(\"signal\", signal ? 1 : 0);"
  }
];

// ── Pure helpers (exported for testing) ───────────────────────────────────────

export function buildInitialCells(): NotebookCell[] {
  return [makeCell(1, "code", "")];
}

export function applyCellExecuteResult(
  cells: NotebookCell[],
  result: CellExecuteResult
): NotebookCell[] {
  return cells.map((cell) => {
    if (cell.id !== result.cellId) {
      return cell;
    }

    return {
      ...cell,
      state: (result.success ? "done" : "error") satisfies CellExecutionState,
      statusText: result.success
        ? `Done in ${result.elapsedMs}ms`
        : (result.errorMessage ?? "Execution error"),
      output: result.output
    };
  });
}

export function markDownstreamStale(
  cells: NotebookCell[],
  fromIndex: number
): NotebookCell[] {
  return cells.map((cell, index) => {
    if (index <= fromIndex) {
      return cell;
    }

    if (cell.state === "idle" || cell.state === "stale") {
      return cell;
    }

    return { ...cell, state: "stale" satisfies CellExecutionState, statusText: "Stale" };
  });
}

// ── View model hook ────────────────────────────────────────────────────────────

export interface QuantNotebookCellViewModel extends NotebookCell {
  deleteConfirmationPending: boolean;
  deleteLabel: string;
  deleteAriaLabel: string;
  deleteDisabledReason: string | null;
}

export interface QuantNotebookViewModel {
  cells: QuantNotebookCellViewModel[];
  context: CellExecutionContext;
  dataResult: DataFetchResult | null;
  dataFetchState: "idle" | "loading" | "done" | "error";
  fetchError: string | null;
  snippets: CellSnippet[];
  clearOutputsLabel: string;
  clearOutputsAriaLabel: string;
  clearOutputsDisabledReason: string | null;
  clearOutputsConfirmationPending: boolean;
  addCell: (kind?: CellKind) => void;
  insertSnippet: (snippetId: string) => void;
  setCellKind: (id: string, kind: CellKind) => void;
  removeCell: (id: string) => void;
  updateCellSource: (id: string, source: string) => void;
  toggleCellCollapse: (id: string) => void;
  runCell: (id: string) => Promise<void>;
  runAll: () => Promise<void>;
  clearOutputs: () => void;
  setContext: (patch: Partial<CellExecutionContext>) => void;
  fetchData: () => Promise<void>;
  dismissDataResult: () => void;
}

let cellSeq = 1;

function makeCell(ordinal: number, kind: CellKind = "code", source = ""): NotebookCell {
  return {
    id: `cell-${(cellSeq++).toString()}`,
    ordinal,
    kind,
    source,
    state: "idle",
    statusText: "Idle",
    collapsed: false,
    output: []
  };
}

function reordinal(cells: NotebookCell[]): NotebookCell[] {
  return cells.map((cell, index) => ({ ...cell, ordinal: index + 1 }));
}

export function useQuantNotebookViewModel(): QuantNotebookViewModel {
  const [cells, setCells] = useState<NotebookCell[]>(buildInitialCells);
  const [context, setContextState] = useState<CellExecutionContext>({});
  const [dataResult, setDataResult] = useState<DataFetchResult | null>(null);
  const [dataFetchState, setDataFetchState] = useState<"idle" | "loading" | "done" | "error">("idle");
  const [fetchError, setFetchError] = useState<string | null>(null);
  const [pendingDeleteCellId, setPendingDeleteCellId] = useState<string | null>(null);
  const [clearOutputsConfirmationPending, setClearOutputsConfirmationPending] = useState(false);

  const runningRef = useRef(false);

  const clearPendingDestructiveAction = useCallback(() => {
    setPendingDeleteCellId(null);
    setClearOutputsConfirmationPending(false);
  }, []);

  const addCell = useCallback((kind: CellKind = "code") => {
    clearPendingDestructiveAction();
    setCells((prev) => reordinal([...prev, makeCell(prev.length + 1, kind)]));
  }, [clearPendingDestructiveAction]);

  const insertSnippet = useCallback((snippetId: string) => {
    const snippet = builtInSnippets.find((s) => s.id === snippetId);
    if (!snippet) {
      return;
    }

    clearPendingDestructiveAction();
    setCells((prev) => reordinal([...prev, makeCell(prev.length + 1, snippet.kind, snippet.source)]));
  }, [clearPendingDestructiveAction]);

  const setCellKind = useCallback((id: string, kind: CellKind) => {
    clearPendingDestructiveAction();
    setCells((prev) =>
      prev.map((cell): NotebookCell =>
        cell.id === id
          ? { ...cell, kind, state: "idle", statusText: "Idle", output: [] }
          : cell
      )
    );
  }, [clearPendingDestructiveAction]);

  const removeCell = useCallback((id: string) => {
    const target = cells.find((cell) => cell.id === id);
    if (!target || cells.length <= 1 || target.state === "running") {
      return;
    }

    if (pendingDeleteCellId !== id) {
      setPendingDeleteCellId(id);
      return;
    }

    setCells((prev) => {
      if (prev.length <= 1) {
        return prev;
      }

      return reordinal(prev.filter((c) => c.id !== id));
    });
    setPendingDeleteCellId(null);
  }, [cells, pendingDeleteCellId]);

  const updateCellSource = useCallback((id: string, source: string) => {
    clearPendingDestructiveAction();
    setCells((prev) => {
      const index = prev.findIndex((c) => c.id === id);
      if (index === -1) {
        return prev;
      }

      const updated = prev.map((cell, i): NotebookCell => {
        if (i === index) {
          return { ...cell, source, state: "stale", statusText: "Stale" };
        }

        return cell;
      });

      return markDownstreamStale(updated, index);
    });
  }, [clearPendingDestructiveAction]);

  const toggleCellCollapse = useCallback((id: string) => {
    clearPendingDestructiveAction();
    setCells((prev) =>
      prev.map((cell) =>
        cell.id === id ? { ...cell, collapsed: !cell.collapsed } : cell
      )
    );
  }, [clearPendingDestructiveAction]);

  const setCellState = useCallback(
    (id: string, state: CellExecutionState, statusText: string) => {
      setCells((prev) =>
        prev.map((cell) =>
          cell.id === id ? { ...cell, state, statusText } : cell
        )
      );
    },
    []
  );

  const runCell = useCallback(
    async (id: string) => {
      clearPendingDestructiveAction();
      const cell = cells.find((c) => c.id === id);
      if (!cell) {
        return;
      }

      if (cell.kind === "markdown") {
        return;
      }

      setCellState(id, "running", "Running…");

      try {
        const result = await api.executeCell({ cellId: id, source: cell.source, context });
        setCells((prev) => applyCellExecuteResult(prev, result));
      } catch (err) {
        const errorMessage = err instanceof Error ? err.message : "Execution failed";
        setCells((prev) =>
          prev.map((c) =>
            c.id === id
              ? {
                  ...c,
                  state: "error",
                  statusText: errorMessage,
                  output: [{ kind: "error", text: errorMessage, tone: "danger" }]
                }
              : c
          )
        );
      }
    },
    [cells, clearPendingDestructiveAction, context, setCellState]
  );

  const runAll = useCallback(async () => {
    if (runningRef.current) {
      return;
    }

    clearPendingDestructiveAction();
    runningRef.current = true;

    try {
      for (const cell of cells) {
        if (cell.kind === "markdown") {
          continue;
        }

        if (cell.state === "done") {
          continue;
        }

        setCellState(cell.id, "running", "Running…");

        try {
          const result = await api.executeCell({ cellId: cell.id, source: cell.source, context });
          setCells((prev) => applyCellExecuteResult(prev, result));
        } catch (err) {
          const errorMessage = err instanceof Error ? err.message : "Execution failed";
          setCells((prev) =>
            prev.map((c) =>
              c.id === cell.id
                ? {
                    ...c,
                    state: "error",
                    statusText: errorMessage,
                    output: [{ kind: "error", text: errorMessage, tone: "danger" }]
                  }
                : c
            )
          );
          break;
        }
      }
    } finally {
      runningRef.current = false;
    }
  }, [cells, clearPendingDestructiveAction, context, setCellState]);

  const clearOutputs = useCallback(() => {
    const hasRunningCell = cells.some((cell) => cell.state === "running");
    const hasClearableOutput = cells.some((cell) => cell.kind !== "markdown" && cell.output.length > 0);
    if (hasRunningCell || !hasClearableOutput) {
      setClearOutputsConfirmationPending(false);
      return;
    }

    if (!clearOutputsConfirmationPending) {
      setPendingDeleteCellId(null);
      setClearOutputsConfirmationPending(true);
      return;
    }

    setCells((prev) =>
      prev.map((cell): NotebookCell =>
        cell.kind === "markdown"
          ? cell
          : { ...cell, state: "idle", statusText: "Idle", output: [] }
      )
    );
    setClearOutputsConfirmationPending(false);
  }, [cells, clearOutputsConfirmationPending]);

  const setContext = useCallback((patch: Partial<CellExecutionContext>) => {
    clearPendingDestructiveAction();
    setContextState((prev) => ({ ...prev, ...patch }));
    setCells((prev) =>
      prev.map((cell): NotebookCell =>
        cell.kind !== "markdown" && (cell.state === "done" || cell.state === "error")
          ? { ...cell, state: "stale", statusText: "Stale" }
          : cell
      )
    );
  }, [clearPendingDestructiveAction]);

  const fetchData = useCallback(async () => {
    clearPendingDestructiveAction();
    if (!context.symbol) {
      setFetchError("Symbol is required.");
      setDataFetchState("error");
      return;
    }

    setDataFetchState("loading");
    setFetchError(null);

    const request: DataFetchRequest = {
      symbol: context.symbol,
      from: context.from ?? new Date(Date.now() - 30 * 24 * 60 * 60 * 1000).toISOString().slice(0, 10),
      to: context.to ?? new Date().toISOString().slice(0, 10),
      interval: (context.interval as DataFetchRequest["interval"]) ?? "daily"
    };

    try {
      const result = await api.fetchQuantData(request);
      setDataResult(result);
      setDataFetchState("done");
    } catch (err) {
      setFetchError(err instanceof Error ? err.message : "Data fetch failed.");
      setDataFetchState("error");
    }
  }, [clearPendingDestructiveAction, context]);

  const dismissDataResult = useCallback(() => {
    clearPendingDestructiveAction();
    setDataResult(null);
    setDataFetchState("idle");
    setFetchError(null);
  }, [clearPendingDestructiveAction]);

  const hasRunningCell = cells.some((cell) => cell.state === "running");
  const hasClearableOutput = cells.some((cell) => cell.kind !== "markdown" && cell.output.length > 0);
  const clearOutputsDisabledReason = hasRunningCell
    ? "Wait for running cells to finish before clearing outputs."
    : hasClearableOutput
      ? null
      : "Run a cell before clearing outputs.";

  const cellViewModels = useMemo<QuantNotebookCellViewModel[]>(
    () =>
      cells.map((cell) => {
        const deleteConfirmationPending = pendingDeleteCellId === cell.id;
        const isRunning = cell.state === "running";
        return {
          ...cell,
          deleteConfirmationPending,
          deleteLabel: deleteConfirmationPending ? "Confirm" : "Delete",
          deleteAriaLabel: deleteConfirmationPending
            ? `Confirm delete cell ${cell.ordinal.toString()}. This removes the cell source and output.`
            : `Delete cell ${cell.ordinal.toString()}. Press again to confirm.`,
          deleteDisabledReason: isRunning ? `Wait for cell ${cell.ordinal.toString()} to finish running before deleting it.` : null
        };
      }),
    [cells, pendingDeleteCellId]
  );

  return {
    cells: cellViewModels,
    context,
    dataResult,
    dataFetchState,
    fetchError,
    snippets: builtInSnippets,
    clearOutputsLabel: clearOutputsConfirmationPending ? "Confirm clear" : "Clear",
    clearOutputsAriaLabel: clearOutputsConfirmationPending
      ? "Confirm clear all notebook outputs. This removes displayed execution results."
      : "Clear all notebook outputs. Press again to confirm.",
    clearOutputsDisabledReason,
    clearOutputsConfirmationPending,
    addCell,
    insertSnippet,
    setCellKind,
    removeCell,
    updateCellSource,
    toggleCellCollapse,
    runCell,
    runAll,
    clearOutputs,
    setContext,
    fetchData,
    dismissDataResult
  };
}

// ── Output tone helpers ────────────────────────────────────────────────────────

export function cellOutputToneClass(
  tone: CellOutput["tone"]
): string {
  switch (tone) {
    case "success":
      return "text-success";
    case "warning":
      return "text-warning";
    case "danger":
      return "text-danger";
    default:
      return "text-foreground";
  }
}

export function cellStateBadgeVariant(
  state: CellExecutionState
): "outline" | "success" | "warning" | "danger" {
  switch (state) {
    case "done":
      return "success";
    case "running":
      return "warning";
    case "error":
      return "danger";
    default:
      return "outline";
  }
}

export function cellStateLabel(state: CellExecutionState): string {
  switch (state) {
    case "idle":
      return "Idle";
    case "running":
      return "Running";
    case "done":
      return "Done";
    case "error":
      return "Error";
    case "stale":
      return "Stale";
  }
}
