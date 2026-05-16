import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { QuantNotebook } from "@/components/meridian/quant-notebook";
import type {
  QuantNotebookCellViewModel,
  QuantNotebookViewModel
} from "@/components/meridian/quant-notebook.view-model";
import { buildDataContextPanel } from "@/components/meridian/quant-notebook.view-model";

function makeCell(overrides: Partial<QuantNotebookCellViewModel> = {}): QuantNotebookCellViewModel {
  return {
    id: "cell-1",
    ordinal: 1,
    kind: "code",
    source: "Print(\"hello\")",
    state: "done",
    statusText: "Done in 5ms",
    collapsed: false,
    output: [{ kind: "console", text: "hello" }],
    runCommand: {
      label: "Run",
      ariaLabel: "Run cell 1",
      disabled: false,
      disabledReason: null,
      busy: false
    },
    sourceField: {
      label: "Cell 1 source",
      placeholder: "// Cell 1\n// Use Data.Prices(symbol), Backtest.WithSymbols(...), or any C# expression",
      disabled: false,
      disabledReason: null,
      disabledReasonId: "quant-notebook-cell-1-source-disabled-reason",
      describedBy: null,
      spellCheck: false
    },
    deleteConfirmationPending: false,
    deleteLabel: "Delete",
    deleteAriaLabel: "Delete cell 1. Press again to confirm.",
    deleteDisabledReason: null,
    ...overrides
  };
}

function makeVm(overrides: Partial<QuantNotebookViewModel> = {}): QuantNotebookViewModel {
  const base: QuantNotebookViewModel = {
    cells: [makeCell()],
    context: {},
    dataResult: null,
    dataFetchState: "idle",
    fetchError: null,
    dataContextPanel: buildDataContextPanel({}, null, "idle", null),
    snippets: [],
    runAllCommand: {
      label: "Run all",
      ariaLabel: "Run all notebook cells",
      disabled: false,
      disabledReason: null,
      busy: false
    },
    clearOutputsLabel: "Clear",
    clearOutputsAriaLabel: "Clear all notebook outputs. Press again to confirm.",
    clearOutputsDisabledReason: null,
    clearOutputsConfirmationPending: false,
    addCell: vi.fn(),
    insertSnippet: vi.fn(),
    setCellKind: vi.fn(),
    removeCell: vi.fn(),
    updateCellSource: vi.fn(),
    toggleCellCollapse: vi.fn(),
    runCell: vi.fn(),
    runAll: vi.fn(),
    clearOutputs: vi.fn(),
    setContext: vi.fn(),
    fetchData: vi.fn(),
    dismissDataResult: vi.fn()
  };
  return {
    ...base,
    ...overrides,
    dataContextPanel: overrides.dataContextPanel ?? base.dataContextPanel
  };
}

describe("QuantNotebook", () => {
  it("renders clear-output disabled reasons from the view model", () => {
    render(
      <QuantNotebook
        vm={makeVm({
          cells: [makeCell({ state: "idle", statusText: "Idle", output: [] })],
          clearOutputsDisabledReason: "Run a cell before clearing outputs."
        })}
      />
    );

    const clearButton = screen.getByRole("button", {
      name: "Clear all notebook outputs. Press again to confirm."
    });
    expect(clearButton).toBeDisabled();
    expect(clearButton).toHaveAttribute("title", "Run a cell before clearing outputs.");
  });

  it("renders run-all disabled reason from the view model while a cell is running", () => {
    render(
      <QuantNotebook
        vm={makeVm({
          cells: [makeCell({ state: "running", statusText: "Running..." })],
          runAllCommand: {
            label: "Running...",
            ariaLabel: "Notebook cells are running",
            disabled: true,
            disabledReason: "Wait for running cells to finish before running all notebook cells.",
            busy: true
          }
        })}
      />
    );

    const runAllButton = screen.getByRole("button", { name: "Notebook cells are running" });
    expect(runAllButton).toBeDisabled();
    expect(runAllButton).toHaveAttribute(
      "title",
      "Wait for running cells to finish before running all notebook cells."
    );
    expect(runAllButton).toHaveAttribute("aria-busy", "true");
  });

  it("renders per-cell run disabled reason from the view model", () => {
    render(
      <QuantNotebook
        vm={makeVm({
          cells: [
            makeCell({
              state: "running",
              statusText: "Running...",
              runCommand: {
                label: "Running...",
                ariaLabel: "Cell 1 is running",
                disabled: true,
                disabledReason: "Wait for cell 1 to finish running before rerunning it.",
                busy: true
              },
              deleteDisabledReason: "Wait for cell 1 to finish running before deleting it."
            })
          ]
        })}
      />
    );

    const runCellButton = screen.getByRole("button", { name: "Cell 1 is running" });
    expect(runCellButton).toBeDisabled();
    expect(runCellButton).toHaveAttribute("title", "Wait for cell 1 to finish running before rerunning it.");
  });

  it("renders cell source semantics from the view model", () => {
    render(
      <QuantNotebook
        vm={makeVm({
          cells: [
            makeCell({
              kind: "markdown",
              source: "",
              sourceField: {
                label: "Cell 1 source",
                placeholder: "## Section 1\n\nNarrative, hypothesis, or analysis notes.",
                disabled: true,
                disabledReason: "Cell 1 is running; wait before editing the source.",
                disabledReasonId: "quant-notebook-cell-1-source-disabled-reason",
                describedBy: "quant-notebook-cell-1-source-disabled-reason",
                spellCheck: true
              }
            })
          ]
        })}
      />
    );

    const source = screen.getByLabelText("Cell 1 source");
    expect(source).toHaveAttribute("placeholder", "## Section 1\n\nNarrative, hypothesis, or analysis notes.");
    expect(source).toBeDisabled();
    expect(source).toHaveAttribute("title", "Cell 1 is running; wait before editing the source.");
    expect(source).toHaveAttribute("aria-describedby", "quant-notebook-cell-1-source-disabled-reason");
    expect(screen.getByText("Cell 1 is running; wait before editing the source.")).toHaveAttribute(
      "id",
      "quant-notebook-cell-1-source-disabled-reason"
    );
    expect(source).toHaveAttribute("spellcheck", "true");
  });

  it("uses the confirmation label and action for armed clear-output state", async () => {
    const user = userEvent.setup();
    const clearOutputs = vi.fn();

    render(
      <QuantNotebook
        vm={makeVm({
          clearOutputs,
          clearOutputsLabel: "Confirm clear",
          clearOutputsAriaLabel: "Confirm clear all notebook outputs. This removes displayed execution results.",
          clearOutputsConfirmationPending: true
        })}
      />
    );

    await user.click(screen.getByRole("button", { name: /Confirm clear all notebook outputs/ }));

    expect(clearOutputs).toHaveBeenCalledTimes(1);
    expect(screen.getByRole("button", { name: /Confirm clear all notebook outputs/ })).toHaveTextContent("Confirm clear");
  });

  it("renders fetch disabled reason and labeled data context fields from the view model", () => {
    render(<QuantNotebook vm={makeVm()} />);

    const fetchButton = screen.getByRole("button", { name: "Fetch notebook data context" });
    expect(fetchButton).toBeDisabled();
    expect(fetchButton).toHaveAttribute("title", "Enter a symbol before fetching notebook data.");
    expect(screen.getByLabelText("Symbol")).toHaveAttribute("id", "quant-notebook-context-symbol");
    expect(screen.getByLabelText("From date")).toHaveAttribute("type", "date");
    expect(screen.getByText("Enter a symbol and fetch bars before running context-aware cells.")).toBeInTheDocument();
  });

  it("renders data-context loading locks as visible linked field feedback", () => {
    render(
      <QuantNotebook
        vm={makeVm({
          context: { symbol: "AAPL" },
          dataFetchState: "loading",
          dataContextPanel: buildDataContextPanel({ symbol: "AAPL" }, null, "loading", null)
        })}
      />
    );

    const symbol = screen.getByLabelText("Symbol");
    expect(symbol).toBeDisabled();
    expect(symbol).toHaveAttribute(
      "aria-describedby",
      "quant-notebook-context-symbol-help quant-notebook-context-symbol-disabled-reason"
    );
    expect(screen.getByText("Data fetch is in progress; wait before editing the symbol.")).toHaveAttribute(
      "id",
      "quant-notebook-context-symbol-disabled-reason"
    );

    const interval = screen.getByLabelText("Interval");
    expect(interval).toBeDisabled();
    expect(interval).toHaveAttribute(
      "aria-describedby",
      "quant-notebook-context-interval-help quant-notebook-context-interval-disabled-reason"
    );
    expect(screen.getByText("Data fetch is in progress; wait before editing the interval.")).toHaveAttribute(
      "id",
      "quant-notebook-context-interval-disabled-reason"
    );
  });

  it("renders data fetch errors as an alert linked to the panel status", () => {
    render(
      <QuantNotebook
        vm={makeVm({
          dataFetchState: "error",
          fetchError: "Symbol is required.",
          dataContextPanel: buildDataContextPanel({}, null, "error", "Symbol is required.")
        })}
      />
    );

    expect(screen.getByRole("alert")).toHaveTextContent("Symbol is required.");
    expect(screen.getByLabelText("Symbol")).toHaveAttribute("aria-invalid", "true");
  });

  it("renders fetched notebook data with the dense table preview", () => {
    const dataResult = {
      symbol: "AAPL",
      from: "2026-01-01",
      to: "2026-01-31",
      interval: "daily" as const,
      rowCount: 6,
      bars: [
        { timestamp: "2026-01-01T00:00:00Z", open: 100, high: 101, low: 99, close: 100.5, volume: 1000 },
        { timestamp: "2026-01-02T00:00:00Z", open: 101, high: 102, low: 100, close: 101.5, volume: 2000 },
        { timestamp: "2026-01-03T00:00:00Z", open: 102, high: 103, low: 101, close: 102.5, volume: 3000 },
        { timestamp: "2026-01-04T00:00:00Z", open: 103, high: 104, low: 102, close: 103.5, volume: 4000 },
        { timestamp: "2026-01-05T00:00:00Z", open: 104, high: 105, low: 103, close: 104.5, volume: 5000 },
        { timestamp: "2026-01-06T00:00:00Z", open: 105, high: 106, low: 104, close: 105.5, volume: 6000 }
      ]
    };

    render(
      <QuantNotebook
        vm={makeVm({
          context: { symbol: "AAPL" },
          dataResult,
          dataFetchState: "done",
          dataContextPanel: buildDataContextPanel({ symbol: "AAPL" }, dataResult, "done", null)
        })}
      />
    );

    expect(screen.getAllByText("AAPL · daily · 6 bars").length).toBeGreaterThan(0);
    expect(screen.getByRole("table", { name: "AAPL notebook data preview" })).toBeInTheDocument();
    expect(screen.getByText("2026-01-01")).toBeInTheDocument();
    expect(screen.getAllByText(/Showing first 5 of 6 bars\./).length).toBeGreaterThan(0);
    expect(screen.getByRole("button", { name: "Dismiss AAPL notebook data preview" })).toBeInTheDocument();
  });
});
