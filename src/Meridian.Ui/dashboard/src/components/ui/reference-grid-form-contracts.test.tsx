import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { fireEvent, render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import React, { type ComponentType, type ReactNode } from "react";
import ts from "typescript";
import { describe, expect, it, vi } from "vitest";

function reference<Props extends object>(file: string, exportedName: string): ComponentType<Props> {
  const source = readFileSync(resolve(process.cwd(), `../../../Meridian Design System/components/${file}.jsx`), "utf8");
  const code = ts.transpileModule(source, {
    compilerOptions: { jsx: ts.JsxEmit.React, module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2020, esModuleInterop: true }
  }).outputText;
  const module = { exports: {} as Record<string, ComponentType<Props>> };
  new Function("require", "module", "exports", code)((id: string) => {
    if (id !== "react") throw new Error(`Unexpected reference dependency: ${id}`);
    return React;
  }, module, module.exports);
  return module.exports[exportedName]!;
}

const Correlation = reference<{ labels: string[]; matrix: number[][]; showValues?: boolean }>("charts/CorrelationHeatmap", "CorrelationHeatmap");
const Coverage = reference<{ rows: string[]; cols: string[]; onCellClick?: (...args: unknown[]) => void }>("charts/CoverageMatrix", "CoverageMatrix");
const Worksheet = reference<{ columns: string[]; rows: number; cells?: Record<string, { value: string }>; editable?: boolean; onCellCommit?: (ref: string, value: string) => void }>("data/WorksheetGrid", "WorksheetGrid");
const FormField = reference<{ id?: string; label?: string; hint?: string; error?: string; required?: boolean; children?: ReactNode }>("core/FormField", "FormField");
const Slider = reference<{ id?: string; label?: string; value?: number; onChange?: (value: number) => void }>("core/Slider", "Slider");
const VirtualizedList = reference<{ items: number[]; rowHeight?: number; height?: number; overscan?: number; onScroll?: () => void; "aria-label"?: string }>("data/VirtualizedList", "VirtualizedList");

describe("reference table and grid ownership", () => {
  it("keeps read-only correlation values accessible inside owned table rows", () => {
    render(<Correlation labels={["AAPL", "MSFT"]} matrix={[[1, 0.4], [0.4, 1]]} showValues={false} />);
    const table = screen.getByRole("table", { name: "Correlation matrix" });
    const rows = within(table).getAllByRole("row");
    expect(rows).toHaveLength(3);
    expect(within(rows[0]!).getAllByRole("columnheader")).toHaveLength(3);
    expect(within(rows[1]!).getByRole("rowheader", { name: "AAPL" })).toBeInTheDocument();
    expect(within(rows[1]!).getByRole("cell", { name: "AAPL · MSFT = 0.40" })).toBeInTheDocument();
  });
  it("preserves coverage keyboard navigation and activation inside semantic grid cells", async () => {
    const user = userEvent.setup(); const selected = vi.fn();
    render(<Coverage rows={["AAPL", "MSFT"]} cols={["Monday", "Tuesday"]} onCellClick={selected} />);
    const grid = screen.getByRole("grid", { name: "Coverage matrix" });
    expect(within(grid).getAllByRole("row")).toHaveLength(3);
    expect(within(grid).getAllByRole("gridcell")).toHaveLength(4);
    const first = screen.getByRole("button", { name: "AAPL · Monday · No session" });
    await user.tab(); expect(first).toHaveFocus();
    await user.keyboard("{ArrowRight}{ArrowDown}");
    expect(screen.getByRole("button", { name: "MSFT · Tuesday · No session" })).toHaveFocus();
    await user.keyboard("{Enter}");
    expect(selected).toHaveBeenCalledWith(expect.objectContaining({ id: "MSFT" }), expect.objectContaining({ id: "Tuesday" }), { status: "none" });
  });
  it("exposes noninteractive coverage as a table", () => {
    render(<Coverage rows={["AAPL"]} cols={["Monday"]} />);
    expect(screen.getByRole("table", { name: "Coverage matrix" })).toBeInTheDocument();
    expect(screen.getByRole("cell")).toHaveAttribute("aria-colindex", "2");
  });
  it("tracks worksheet selection and restores grid focus after committing an edit", async () => {
    const user = userEvent.setup(); const committed = vi.fn();
    render(<Worksheet columns={["A", "B"]} rows={2} cells={{ A1: { value: "10" } }} editable onCellCommit={committed} />);
    const grid = screen.getByRole("grid", { name: "Worksheet" });
    expect(within(grid).getAllByRole("row")).toHaveLength(3);
    expect(within(grid).getAllByRole("columnheader")).toHaveLength(3);
    await user.tab(); await user.keyboard("{ArrowRight}");
    const active = document.getElementById(grid.getAttribute("aria-activedescendant")!);
    expect(active).toHaveAttribute("role", "gridcell"); expect(active).toHaveAttribute("aria-selected", "true");
    await user.keyboard("{F2}");
    const input = screen.getByRole("textbox", { name: "Edit A1" }); expect(input).toHaveFocus();
    await user.click(input); expect(input).toHaveFocus();
    await user.clear(input); await user.type(input, "20"); await user.keyboard("{Enter}");
    expect(committed).toHaveBeenCalledTimes(1); expect(committed).toHaveBeenCalledWith("A1", "20");
    expect(grid).toHaveFocus();
    expect(document.getElementById(grid.getAttribute("aria-activedescendant")!)).toHaveAttribute("aria-selected", "true");
  });
});

describe("reference form and virtualized-list contracts", () => {
  it("associates a stable field label and combines validation with existing descriptions", () => {
    const view = render(<><p id="existing-help">Retained source</p><FormField label="Capital" hint="Annual basis" required><input id="capital-input" aria-describedby="existing-help" defaultValue="2500000" /></FormField></>);
    const input = screen.getByRole("textbox", { name: "Capital" });
    expect(input).toHaveAttribute("id", "capital-input"); expect(input).toBeRequired();
    expect(input).toHaveAccessibleDescription("Retained source Annual basis");
    view.rerender(<><p id="existing-help">Retained source</p><FormField label="Capital" error="Review the source amount" required><input id="capital-input" aria-describedby="existing-help" defaultValue="2500000" /></FormField></>);
    expect(input).toHaveAttribute("aria-invalid", "true");
    expect(input).toHaveAccessibleDescription("Retained source Review the source amount");
  });
  it("keeps a generated field id stable across hint and error updates", () => {
    const view = render(<FormField label="Strategy" hint="Review"><input /></FormField>);
    const input = screen.getByLabelText("Strategy"); const id = input.id;
    view.rerender(<FormField label="Strategy" error="Required"><input /></FormField>);
    expect(screen.getByLabelText("Strategy").id).toBe(id);
  });
  it("names the slider through its visible label and retains caller IDs", () => {
    const changed = vi.fn(); render(<Slider id="yield-floor" label="Minimum yield" value={3} onChange={changed} />);
    const slider = screen.getByRole("slider", { name: "Minimum yield" });
    expect(slider).toHaveAttribute("id", "yield-floor");
    fireEvent.change(slider, { target: { value: "4" } }); expect(changed).toHaveBeenCalledWith(4);
  });
  it("makes the virtualized scroll region keyboard accessible while preserving window positions", async () => {
    const user = userEvent.setup(); const scrolled = vi.fn();
    render(<VirtualizedList items={Array.from({ length: 100 }, (_, index) => index)} height={68} rowHeight={34} overscan={0} aria-label="Loaded evidence" onScroll={scrolled} />);
    const list = screen.getByRole("list", { name: "Loaded evidence" });
    await user.tab(); expect(list).toHaveFocus();
    expect(within(list).getAllByRole("listitem")).toHaveLength(2);
    expect(within(list).getAllByRole("listitem")[0]).toHaveAttribute("aria-posinset", "1");
    fireEvent.scroll(list, { target: { scrollTop: 340 } });
    const rows = within(list).getAllByRole("listitem");
    expect(rows[0]).toHaveAttribute("aria-posinset", "11"); expect(rows[0]).toHaveAttribute("aria-setsize", "100");
    expect(scrolled).toHaveBeenCalledTimes(1);
  });
});
