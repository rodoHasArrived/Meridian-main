import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { QuantNotebook } from "@/components/meridian/quant-notebook";
import type {
  QuantNotebookCellViewModel,
  QuantNotebookViewModel
} from "@/components/meridian/quant-notebook.view-model";

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
    deleteConfirmationPending: false,
    deleteLabel: "Delete",
    deleteAriaLabel: "Delete cell 1. Press again to confirm.",
    deleteDisabledReason: null,
    ...overrides
  };
}

function makeVm(overrides: Partial<QuantNotebookViewModel> = {}): QuantNotebookViewModel {
  return {
    cells: [makeCell()],
    context: {},
    dataResult: null,
    dataFetchState: "idle",
    fetchError: null,
    snippets: [],
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
    dismissDataResult: vi.fn(),
    ...overrides
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
});
