import { act, renderHook, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import {
  applyCellExecuteResult,
  buildDataContextPanel,
  buildInitialCells,
  cellOutputToneClass,
  cellStateBadgeVariant,
  cellStateLabel,
  markDownstreamStale,
  useQuantNotebookViewModel
} from "@/components/meridian/quant-notebook.view-model";
import type { CellExecuteResult, NotebookCell } from "@/types";

vi.mock("@/lib/api", async () => {
  const actual = await vi.importActual<typeof import("@/lib/api")>("@/lib/api");
  return {
    ...actual,
    executeCell: vi.fn(),
    fetchQuantData: vi.fn()
  };
});

const api = await import("@/lib/api");

function makeCells(states: NotebookCell["state"][]): NotebookCell[] {
  return states.map((state, index) => ({
    id: `c${index.toString()}`,
    ordinal: index + 1,
    kind: "code",
    source: `// cell ${index.toString()}`,
    state,
    statusText: state,
    collapsed: false,
    output: []
  }));
}

describe("quant-notebook pure helpers", () => {
  it("buildInitialCells returns one idle cell", () => {
    const cells = buildInitialCells();
    expect(cells).toHaveLength(1);
    expect(cells[0].state).toBe("idle");
    expect(cells[0].ordinal).toBe(1);
    expect(cells[0].output).toEqual([]);
  });

  it("applyCellExecuteResult marks the target cell done on success", () => {
    const cells = makeCells(["running", "idle"]);
    const result: CellExecuteResult = {
      cellId: cells[0].id,
      success: true,
      output: [{ kind: "console", text: "ok" }],
      elapsedMs: 42,
      errorMessage: null
    };

    const next = applyCellExecuteResult(cells, result);

    expect(next[0].state).toBe("done");
    expect(next[0].statusText).toBe("Done in 42ms");
    expect(next[0].output).toEqual(result.output);
    expect(next[1]).toBe(cells[1]); // untouched
  });

  it("applyCellExecuteResult marks the target cell error on failure", () => {
    const cells = makeCells(["running"]);
    const result: CellExecuteResult = {
      cellId: cells[0].id,
      success: false,
      output: [{ kind: "error", text: "boom", tone: "danger" }],
      elapsedMs: 5,
      errorMessage: "Compilation failed"
    };

    const next = applyCellExecuteResult(cells, result);

    expect(next[0].state).toBe("error");
    expect(next[0].statusText).toBe("Compilation failed");
    expect(next[0].output[0].tone).toBe("danger");
  });

  it("markDownstreamStale only stales cells past the index that were executed", () => {
    const cells = makeCells(["done", "done", "idle", "done"]);

    const next = markDownstreamStale(cells, 0);

    expect(next[0].state).toBe("done"); // untouched
    expect(next[1].state).toBe("stale");
    expect(next[2].state).toBe("idle"); // already idle, unchanged
    expect(next[3].state).toBe("stale");
  });

  it("cellStateBadgeVariant maps states to variants", () => {
    expect(cellStateBadgeVariant("done")).toBe("success");
    expect(cellStateBadgeVariant("running")).toBe("warning");
    expect(cellStateBadgeVariant("error")).toBe("danger");
    expect(cellStateBadgeVariant("idle")).toBe("outline");
    expect(cellStateBadgeVariant("stale")).toBe("outline");
  });

  it("cellStateLabel returns human-readable labels", () => {
    expect(cellStateLabel("idle")).toBe("Idle");
    expect(cellStateLabel("running")).toBe("Running");
    expect(cellStateLabel("done")).toBe("Done");
    expect(cellStateLabel("error")).toBe("Error");
    expect(cellStateLabel("stale")).toBe("Stale");
  });

  it("cellOutputToneClass maps tones to text classes", () => {
    expect(cellOutputToneClass("success")).toBe("text-success");
    expect(cellOutputToneClass("warning")).toBe("text-warning");
    expect(cellOutputToneClass("danger")).toBe("text-danger");
    expect(cellOutputToneClass("default")).toBe("text-foreground");
    expect(cellOutputToneClass(undefined)).toBe("text-foreground");
  });

  it("buildDataContextPanel owns fetch disabled, loading, and error state", () => {
    const empty = buildDataContextPanel({}, null, "idle", null);

    expect(empty.fetchCommand).toMatchObject({
      label: "Fetch",
      disabled: true,
      disabledReason: "Enter a symbol before fetching notebook data.",
      busy: false
    });
    expect(empty.statusTone).toBe("idle");
    expect(empty.fields.symbol.error).toBe(false);

    const loading = buildDataContextPanel({ symbol: "AAPL" }, null, "loading", null);

    expect(loading.fetchCommand).toMatchObject({
      label: "Loading...",
      disabled: true,
      disabledReason: "Notebook data fetch is already loading.",
      busy: true
    });
    expect(loading.fields.from.disabledReason).toBe("Data fetch is in progress; wait before editing the start date.");

    const error = buildDataContextPanel({}, null, "error", "Symbol is required.");

    expect(error.statusTone).toBe("error");
    expect(error.statusText).toBe("Symbol is required.");
    expect(error.fields.symbol.error).toBe(true);
  });

  it("buildDataContextPanel formats result rows for the preview table", () => {
    const panel = buildDataContextPanel(
      { symbol: "AAPL" },
      {
        symbol: "AAPL",
        from: "2026-01-01",
        to: "2026-01-31",
        interval: "daily",
        rowCount: 6,
        bars: [
          { timestamp: "2026-01-01T00:00:00Z", open: 100, high: 101.125, low: 99.5, close: 100.25, volume: 1000 },
          { timestamp: "2026-01-02T15:30:00Z", open: 101, high: 102, low: 100, close: 101.5, volume: 2500 },
          { timestamp: "2026-01-03T00:00:00Z", open: 102, high: 103, low: 101, close: 102.5, volume: 3000 },
          { timestamp: "2026-01-04T00:00:00Z", open: 103, high: 104, low: 102, close: 103.5, volume: 4000 },
          { timestamp: "2026-01-05T00:00:00Z", open: 104, high: 105, low: 103, close: 104.5, volume: 5000 },
          { timestamp: "2026-01-06T00:00:00Z", open: 105, high: 106, low: 104, close: 105.5, volume: 6000 }
        ]
      },
      "done",
      null
    );

    expect(panel.statusTone).toBe("success");
    expect(panel.statusText).toBe("Loaded 6 daily bars for AAPL.");
    expect(panel.result?.summaryText).toBe("AAPL · daily · 6 bars");
    expect(panel.result?.previewNotice).toBe("Showing first 5 of 6 bars.");
    expect(panel.result?.rows).toHaveLength(5);
    expect(panel.result?.rows[0]).toMatchObject({
      timestampLabel: "2026-01-01",
      openLabel: "100.00",
      highLabel: "101.125",
      lowLabel: "99.50",
      closeLabel: "100.25",
      volumeLabel: "1,000"
    });
  });
});

describe("useQuantNotebookViewModel", () => {
  it("starts with one idle cell and no data result", () => {
    const { result } = renderHook(() => useQuantNotebookViewModel());

    expect(result.current.cells).toHaveLength(1);
    expect(result.current.cells[0].state).toBe("idle");
    expect(result.current.dataResult).toBeNull();
    expect(result.current.dataFetchState).toBe("idle");
  });

  it("addCell appends a new cell with the next ordinal", () => {
    const { result } = renderHook(() => useQuantNotebookViewModel());

    act(() => {
      result.current.addCell();
    });

    expect(result.current.cells).toHaveLength(2);
    expect(result.current.cells[1].ordinal).toBe(2);
    expect(result.current.cells[1].state).toBe("idle");
  });

  it("removeCell requires confirmation before removing and reordinals the rest", () => {
    const { result } = renderHook(() => useQuantNotebookViewModel());

    act(() => {
      result.current.addCell();
      result.current.addCell();
    });

    const middleId = result.current.cells[1].id;

    act(() => {
      result.current.removeCell(middleId);
    });

    expect(result.current.cells).toHaveLength(3);
    expect(result.current.cells[1].deleteConfirmationPending).toBe(true);
    expect(result.current.cells[1]).toMatchObject({
      deleteLabel: "Confirm",
      deleteAriaLabel: "Confirm delete cell 2. This removes the cell source and output.",
      deleteDisabledReason: null
    });

    act(() => {
      result.current.removeCell(middleId);
    });

    expect(result.current.cells).toHaveLength(2);
    expect(result.current.cells.map((c) => c.ordinal)).toEqual([1, 2]);
    expect(result.current.cells.every((cell) => !cell.deleteConfirmationPending)).toBe(true);
  });

  it("removeCell keeps at least one cell", () => {
    const { result } = renderHook(() => useQuantNotebookViewModel());

    const onlyId = result.current.cells[0].id;

    act(() => {
      result.current.removeCell(onlyId);
    });

    expect(result.current.cells).toHaveLength(1);
    expect(result.current.cells[0].deleteConfirmationPending).toBe(false);
  });

  it("clears a pending cell delete when the operator edits instead", () => {
    const { result } = renderHook(() => useQuantNotebookViewModel());

    act(() => {
      result.current.addCell();
    });

    const secondId = result.current.cells[1].id;

    act(() => {
      result.current.removeCell(secondId);
    });

    expect(result.current.cells[1].deleteConfirmationPending).toBe(true);

    act(() => {
      result.current.updateCellSource(secondId, "// keep this cell");
    });

    expect(result.current.cells[1].deleteConfirmationPending).toBe(false);
    expect(result.current.cells[1].source).toBe("// keep this cell");
  });

  it("updateCellSource marks the cell stale and stales downstream done cells", () => {
    const { result } = renderHook(() => useQuantNotebookViewModel());

    act(() => {
      result.current.addCell();
      result.current.addCell();
    });

    const firstId = result.current.cells[0].id;
    const thirdId = result.current.cells[2].id;

    // Manually set cell 3 to done via runCell mock
    vi.mocked(api.executeCell).mockResolvedValueOnce({
      cellId: thirdId,
      success: true,
      output: [],
      elapsedMs: 10,
      errorMessage: null
    });

    act(() => {
      void result.current.runCell(thirdId);
    });

    return waitFor(() => {
      expect(result.current.cells[2].state).toBe("done");
    }).then(() => {
      act(() => {
        result.current.updateCellSource(firstId, "new source");
      });

      expect(result.current.cells[0].source).toBe("new source");
      expect(result.current.cells[0].state).toBe("stale");
      expect(result.current.cells[2].state).toBe("stale");
    });
  });

  it("runCell calls the API and stores the result", async () => {
    const { result } = renderHook(() => useQuantNotebookViewModel());
    const cellId = result.current.cells[0].id;

    vi.mocked(api.executeCell).mockResolvedValueOnce({
      cellId,
      success: true,
      output: [{ kind: "console", text: "mock output" }],
      elapsedMs: 25,
      errorMessage: null
    });

    await act(async () => {
      await result.current.runCell(cellId);
    });

    expect(api.executeCell).toHaveBeenCalledWith({
      cellId,
      source: result.current.cells[0].source,
      context: {}
    });
    expect(result.current.cells[0].state).toBe("done");
    expect(result.current.cells[0].output).toHaveLength(1);
    expect(result.current.cells[0].output[0].text).toBe("mock output");
  });

  it("runCell surfaces errors as cell error state", async () => {
    const { result } = renderHook(() => useQuantNotebookViewModel());
    const cellId = result.current.cells[0].id;

    vi.mocked(api.executeCell).mockRejectedValueOnce(new Error("network down"));

    await act(async () => {
      await result.current.runCell(cellId);
    });

    expect(result.current.cells[0].state).toBe("error");
    expect(result.current.cells[0].statusText).toBe("network down");
    expect(result.current.cells[0].output[0].text).toBe("network down");
  });

  it("clearOutputs requires confirmation before resetting cells to idle", async () => {
    const { result } = renderHook(() => useQuantNotebookViewModel());
    const cellId = result.current.cells[0].id;

    expect(result.current.clearOutputsDisabledReason).toBe("Run a cell before clearing outputs.");

    vi.mocked(api.executeCell).mockResolvedValueOnce({
      cellId,
      success: true,
      output: [{ kind: "console", text: "x" }],
      elapsedMs: 1,
      errorMessage: null
    });

    await act(async () => {
      await result.current.runCell(cellId);
    });

    act(() => {
      result.current.clearOutputs();
    });

    expect(result.current.clearOutputsConfirmationPending).toBe(true);
    expect(result.current.clearOutputsLabel).toBe("Confirm clear");
    expect(result.current.clearOutputsAriaLabel).toBe(
      "Confirm clear all notebook outputs. This removes displayed execution results."
    );
    expect(result.current.cells[0].state).toBe("done");
    expect(result.current.cells[0].output).toHaveLength(1);

    act(() => {
      result.current.clearOutputs();
    });

    expect(result.current.clearOutputsConfirmationPending).toBe(false);
    expect(result.current.cells[0].state).toBe("idle");
    expect(result.current.cells[0].output).toEqual([]);
    expect(result.current.clearOutputsDisabledReason).toBe("Run a cell before clearing outputs.");
  });

  it("clearOutputs confirmation clears when the operator edits instead", async () => {
    const { result } = renderHook(() => useQuantNotebookViewModel());
    const cellId = result.current.cells[0].id;

    vi.mocked(api.executeCell).mockResolvedValueOnce({
      cellId,
      success: true,
      output: [{ kind: "console", text: "x" }],
      elapsedMs: 1,
      errorMessage: null
    });

    await act(async () => {
      await result.current.runCell(cellId);
    });

    act(() => {
      result.current.clearOutputs();
    });

    expect(result.current.clearOutputsConfirmationPending).toBe(true);

    act(() => {
      result.current.updateCellSource(cellId, "// keep output until rerun");
    });

    expect(result.current.clearOutputsConfirmationPending).toBe(false);
    expect(result.current.cells[0].source).toBe("// keep output until rerun");
    expect(result.current.cells[0].output).toHaveLength(1);
  });

  it("setContext patches context and marks executed cells stale", async () => {
    const { result } = renderHook(() => useQuantNotebookViewModel());
    const cellId = result.current.cells[0].id;

    vi.mocked(api.executeCell).mockResolvedValueOnce({
      cellId,
      success: true,
      output: [],
      elapsedMs: 5,
      errorMessage: null
    });

    await act(async () => {
      await result.current.runCell(cellId);
    });

    act(() => {
      result.current.setContext({ symbol: "AAPL" });
    });

    expect(result.current.context.symbol).toBe("AAPL");
    expect(result.current.cells[0].state).toBe("stale");
  });

  it("fetchData requires a symbol", async () => {
    const { result } = renderHook(() => useQuantNotebookViewModel());

    await act(async () => {
      await result.current.fetchData();
    });

    expect(result.current.dataFetchState).toBe("error");
    expect(result.current.fetchError).toBe("Symbol is required.");
    expect(api.fetchQuantData).not.toHaveBeenCalled();
  });

  it("fetchData stores the result on success", async () => {
    const { result } = renderHook(() => useQuantNotebookViewModel());

    vi.mocked(api.fetchQuantData).mockResolvedValueOnce({
      symbol: "AAPL",
      from: "2026-01-01",
      to: "2026-01-31",
      interval: "daily",
      bars: [
        { timestamp: "2026-01-01T00:00:00Z", open: 100, high: 101, low: 99, close: 100.5, volume: 1000 }
      ],
      rowCount: 1
    });

    act(() => {
      result.current.setContext({ symbol: "AAPL" });
    });

    await act(async () => {
      await result.current.fetchData();
    });

    expect(api.fetchQuantData).toHaveBeenCalledTimes(1);
    expect(result.current.dataFetchState).toBe("done");
    expect(result.current.dataResult?.symbol).toBe("AAPL");
    expect(result.current.dataResult?.rowCount).toBe(1);
  });

  it("dismissDataResult clears state", async () => {
    const { result } = renderHook(() => useQuantNotebookViewModel());

    vi.mocked(api.fetchQuantData).mockResolvedValueOnce({
      symbol: "AAPL",
      from: "2026-01-01",
      to: "2026-01-31",
      interval: "daily",
      bars: [],
      rowCount: 0
    });

    act(() => {
      result.current.setContext({ symbol: "AAPL" });
    });

    await act(async () => {
      await result.current.fetchData();
    });

    act(() => {
      result.current.dismissDataResult();
    });

    expect(result.current.dataResult).toBeNull();
    expect(result.current.dataFetchState).toBe("idle");
  });
});
