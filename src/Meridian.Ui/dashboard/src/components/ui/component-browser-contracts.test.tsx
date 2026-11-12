import { readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import React, { type ComponentType } from "react";
import ts from "typescript";
import { describe, expect, it, vi } from "vitest";

const modules = new Map<string, Record<string, unknown>>();
function load(file: string): Record<string, unknown> {
  const cached = modules.get(file);
  if (cached) return cached;
  const code = ts.transpileModule(readFileSync(file, "utf8"), {
    compilerOptions: { jsx: ts.JsxEmit.React, module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2020, esModuleInterop: true }
  }).outputText;
  const module = { exports: {} as Record<string, unknown> };
  modules.set(file, module.exports);
  new Function("require", "module", "exports", code)((id: string) => {
    if (id === "react") return React;
    if (id.startsWith(".")) return load(resolve(dirname(file), `${id}.js`));
    throw new Error(`Unexpected reference dependency: ${id}`);
  }, module, module.exports);
  return module.exports;
}
function reference<Props extends object>(file: string, name: string): ComponentType<Props> {
  return load(resolve(process.cwd(), `../../../Meridian Design System/components/${file}.jsx`))[name] as ComponentType<Props>;
}

type QuoteRow = { strike: number; call: { bid: number }; put: { bid: number } };
const OptionChain = reference<{ rows: QuoteRow[]; columns: string[]; underlyingPrice: number; onSelect?: (row: QuoteRow) => void }>("trading/OptionChainTable", "OptionChainTable");
const DataTable = reference<{ columns: { key: string; label: string }[]; rows: Record<string, unknown>[]; "aria-label"?: string; "aria-labelledby"?: string }>("data/DenseDataTable", "DenseDataTable");
const quotes = [
  { strike: 105, call: { bid: 1.2 }, put: { bid: 5.3 } },
  { strike: 95, call: { bid: 5.4 }, put: { bid: 1.1 } }
];

describe("reference browser table contracts", () => {
  it("owns all option-chain headers and spot values in table rows", () => {
    render(<OptionChain rows={quotes} columns={["bid"]} underlyingPrice={100} />);
    const table = screen.getByRole("table", { name: "Option chain" });
    const rows = within(table).getAllByRole("row");
    expect(rows).toHaveLength(5);
    expect(within(rows[0]!).getAllByRole("columnheader")).toHaveLength(3);
    expect(within(rows[1]!).getAllByRole("columnheader")).toHaveLength(3);
    const spot = within(table).getByRole("cell", { name: "100.00 · underlying" });
    expect(spot).toHaveAttribute("aria-colspan", "3");
    expect(spot.parentElement).toHaveAttribute("role", "row");
    expect(within(table).getAllByRole("rowheader").map((cell) => cell.textContent)).toEqual(["95.00", "105.00"]);
    expect(table).not.toHaveAttribute("tabindex");
  });

  it("announces the active strike and selects the unchanged source row by keyboard", async () => {
    const user = userEvent.setup(); const selected = vi.fn();
    render(<OptionChain rows={quotes} columns={["bid"]} underlyingPrice={100} onSelect={selected} />);
    const grid = screen.getByRole("grid", { name: "Option chain" });
    await user.tab(); expect(grid).toHaveFocus();
    await user.keyboard("{End}");
    expect(document.getElementById(grid.getAttribute("aria-activedescendant")!)).toHaveTextContent("105.00");
    await user.keyboard("{Enter}");
    expect(selected).toHaveBeenCalledTimes(1);
    expect(selected.mock.calls[0]![0]).toBe(quotes[0]);
    await user.keyboard("{Home} ");
    expect(selected.mock.calls[1]![0]).toBe(quotes[1]);
  });

  it("keeps a read-only dense table reachable for keyboard scrolling", async () => {
    const user = userEvent.setup();
    render(<DataTable columns={[{ key: "amount", label: "Amount" }]} rows={[{ amount: "1,234.50" }]} aria-label="Custodian balances" />);
    const region = screen.getByRole("region", { name: "Custodian balances" });
    await user.tab(); expect(region).toHaveFocus();
    expect(within(region).getByRole("table", { name: "Custodian balances" })).toBeInTheDocument();
    expect(within(region).getByRole("cell", { name: "1,234.50" })).toBeInTheDocument();
  });

  it("names both the dense table and scroll region through the visible heading", () => {
    render(<><h2 id="allocation-heading">Allocation evidence</h2><DataTable columns={[{ key: "source", label: "Source" }]} rows={[]} aria-labelledby="allocation-heading" /></>);
    expect(screen.getByRole("region", { name: "Allocation evidence" })).toBeInTheDocument();
    expect(screen.getByRole("table", { name: "Allocation evidence" })).toBeInTheDocument();
  });
});
