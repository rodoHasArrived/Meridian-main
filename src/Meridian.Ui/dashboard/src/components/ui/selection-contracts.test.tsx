import { existsSync, readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import React, { useState, type ComponentType } from "react";
import ts from "typescript";
import { afterEach, describe, expect, it, vi } from "vitest";
import { DensityToggle, type DensityToggleProps } from "./density-toggle";
import { RadioGroup, type RadioGroupProps } from "./radio-group";
import { SegmentedControl } from "./segmented-control";

// Exercise editable reference sources instead of a potentially stale bundle.
function reference<Props extends object>(name: string, exportedName = name): ComponentType<Props> {
  const root = resolve(process.cwd(), "../../../Meridian Design System/components");
  const cache = new Map<string, { exports: Record<string, ComponentType<Props>> }>();
  const load = (file: string): { exports: Record<string, ComponentType<Props>> } => {
    const filename = [file, `${file}.jsx`, `${file}.js`].find((candidate) => existsSync(candidate));
    if (!filename) throw new Error(`Missing reference source: ${file}`);
    const cached = cache.get(filename);
    if (cached) return cached;
    const source = readFileSync(filename, "utf8");
    const code = ts.transpileModule(source, {
      compilerOptions: { jsx: ts.JsxEmit.React, module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2020, esModuleInterop: true }
    }).outputText;
    const module = { exports: {} as Record<string, ComponentType<Props>> };
    cache.set(filename, module);
    new Function("require", "module", "exports", code)((id: string) => {
      if (id === "react") return React;
      if (id.startsWith(".")) return load(resolve(dirname(filename), id)).exports;
      throw new Error(`Unexpected reference dependency: ${id}`);
    }, module, module.exports);
    return module;
  };
  return load(resolve(root, name.includes("/") ? `${name}.jsx` : `core/${name}.jsx`)).exports[exportedName]!;
}

type ChoiceProps = Pick<RadioGroupProps, "options" | "value" | "onChange" | "aria-label">;
const implementations = [
  { name: "native", Density: DensityToggle, Radio: RadioGroup, Segmented: SegmentedControl },
  { name: "reference", Density: reference<DensityToggleProps>("DensityToggle"), Radio: reference<ChoiceProps>("RadioGroup"), Segmented: reference<ChoiceProps>("SegmentedControl") }
];
afterEach(() => { document.body.removeAttribute("data-theme-density"); localStorage.clear(); });

for (const implementation of implementations) {
  describe(`${implementation.name} selection keyboard contracts`, () => {
    it("keeps one density tab stop and synchronizes focus, choice, and actual density", async () => {
      const user = userEvent.setup(); const Control = implementation.Density;
      render(<Control />);
      const choices = screen.getAllByRole("radio");
      expect(choices.filter((choice) => choice.tabIndex === 0)).toEqual([screen.getByRole("radio", { name: "Default" })]);
      await user.tab(); expect(screen.getByRole("radio", { name: "Default" })).toHaveFocus();
      await user.keyboard("{ArrowRight}"); expect(screen.getByRole("radio", { name: "Spacious" })).toHaveFocus();
      expect(screen.getByRole("radio", { name: "Spacious" })).toHaveAttribute("aria-checked", "true");
      expect(document.body).toHaveAttribute("data-theme-density", "spacious");
      await user.keyboard("{ArrowRight}"); expect(screen.getByRole("radio", { name: "Terminal" })).toHaveFocus();
      await user.keyboard("{End}"); expect(screen.getByRole("radio", { name: "Spacious" })).toHaveFocus();
      await user.keyboard("{Home}"); expect(screen.getByRole("radio", { name: "Terminal" })).toHaveFocus();
      expect(choices.filter((choice) => choice.tabIndex === 0)).toHaveLength(1);
    });
    it("keeps density operable when browser storage is denied", async () => {
      const user = userEvent.setup(); const Control = implementation.Density;
      const read = vi.spyOn(Storage.prototype, "getItem").mockImplementation(() => { throw new Error("Storage denied"); });
      const write = vi.spyOn(Storage.prototype, "setItem").mockImplementation(() => { throw new Error("Storage denied"); });
      try {
        render(<Control persist="test-density" />);
        await user.click(screen.getByRole("radio", { name: "Compact" }));
        expect(screen.getByRole("radio", { name: "Compact" })).toHaveAttribute("aria-checked", "true");
        expect(document.body).toHaveAttribute("data-theme-density", "compact");
      } finally { read.mockRestore(); write.mockRestore(); }
    });
    for (const kind of ["radio", "segmented"] as const) {
      const role = kind === "radio" ? "radio" : "tab";
      const Control = kind === "radio" ? implementation.Radio : implementation.Segmented;
      const options = [{ value: "live", label: "Live" }, { value: "paper", label: "Paper", disabled: true }, { value: "fixture", label: "Fixture" }];
      it(`${kind} focuses enabled choices with arrows and Home/End`, async () => {
        const user = userEvent.setup(); const changed = vi.fn();
        function Controlled() {
          const [value, setValue] = useState("live");
          return <Control aria-label="Execution mode" options={options} value={value} onChange={(next: string) => { changed(next); setValue(next); }} />;
        }
        render(<Controlled />);
        await user.tab(); expect(screen.getByRole(role, { name: "Live" })).toHaveFocus();
        await user.keyboard("{ArrowDown}"); expect(screen.getByRole(role, { name: "Fixture" })).toHaveFocus();
        expect(changed).toHaveBeenLastCalledWith("fixture");
        await user.keyboard("{ArrowRight}"); expect(screen.getByRole(role, { name: "Live" })).toHaveFocus();
        await user.keyboard("{End}"); expect(screen.getByRole(role, { name: "Fixture" })).toHaveFocus();
        await user.keyboard("{Home}"); expect(screen.getByRole(role, { name: "Live" })).toHaveFocus();
        expect(screen.getAllByRole(role).filter((choice) => choice.tabIndex === 0)).toHaveLength(1);
        await user.click(screen.getByRole(role, { name: "Paper" })); expect(changed).not.toHaveBeenCalledWith("paper");
      });
      it(`${kind} retains an enabled entry point if the selected value is absent or disabled`, () => {
        const view = render(<Control options={options} value="paper" />);
        expect(screen.getByRole(role, { name: "Live" })).toHaveAttribute("tabindex", "0");
        view.rerender(<Control options={options} value="missing" />);
        expect(screen.getByRole(role, { name: "Live" })).toHaveAttribute("tabindex", "0");
        view.rerender(<Control options={options.map((option) => ({ ...option, disabled: true }))} value="paper" />);
        expect(screen.getAllByRole(role).filter((choice) => choice.tabIndex === 0)).toHaveLength(0);
      });
      it(`${kind} focuses the requested choice without overriding controlled selection`, async () => {
        const user = userEvent.setup(); const onChange = vi.fn();
        render(<Control options={options} value="live" onChange={onChange} />);
        screen.getByRole(role, { name: "Live" }).focus(); await user.keyboard("{ArrowRight}");
        expect(screen.getByRole(role, { name: "Fixture" })).toHaveFocus(); expect(onChange).toHaveBeenCalledWith("fixture");
        if (kind === "radio") expect(screen.getByRole(role, { name: "Live" })).toBeChecked();
        else expect(screen.getByRole(role, { name: "Live" })).toHaveAttribute("aria-selected", "true");
      });
    }
  });
}


describe("reference boolean controls", () => {
  const Toggle = reference<{ checked?: boolean; onChange?: (checked: boolean) => void; label?: string; disabled?: boolean }>("Checkbox", "Toggle");
  it("exposes a named switch with real Space/Enter activation and disabled protection", async () => {
    const user = userEvent.setup(); const changed = vi.fn();
    function Controlled() {
      const [checked, setChecked] = useState(false);
      return <Toggle label="Pause alerts" checked={checked} onChange={(next) => { changed(next); setChecked(next); }} />;
    }
    const view = render(<Controlled />);
    const toggle = screen.getByRole("switch", { name: "Pause alerts" });
    await user.tab(); expect(toggle).toHaveFocus();
    await user.keyboard(" "); expect(toggle).toBeChecked(); expect(changed).toHaveBeenLastCalledWith(true);
    await user.keyboard("{Enter}"); expect(toggle).not.toBeChecked(); expect(changed).toHaveBeenLastCalledWith(false);
    view.rerender(<Toggle label="Pause alerts" checked={false} disabled onChange={changed} />);
    changed.mockClear(); await user.click(screen.getByRole("switch", { name: "Pause alerts" }));
    expect(changed).not.toHaveBeenCalled();
  });
});


describe("reference evidence and form contracts", () => {
  const StatementTable = reference<Record<string, unknown>>("accounting/StatementTable", "StatementTable");
  const ReconciliationPanel = reference<Record<string, unknown>>("accounting/ReconciliationPanel", "ReconciliationPanel");
  const FormRow = reference<{ label?: string; hint?: string; error?: string; children: React.ReactNode }>("Form", "FormRow");
  const ProgressBar = reference<Record<string, unknown>>("ProgressBar");
  const NumberInput = reference<Record<string, unknown>>("NumberInput");
  const Checkbox = reference<Record<string, unknown>>("Checkbox");
  it("keeps the native financial table inside a named scrolling region", () => {
    render(<StatementTable sections={[{ label: "Assets", rows: [{ label: "Cash", value: 100 }] }]} />);
    expect(screen.getByRole("region", { name: "Financial statement" })).toContainElement(screen.getByRole("table"));
  });
  it("associates a form-row label and error without discarding child identity or descriptions", () => {
    render(<><span id="external-detail">Retained input scope</span><FormRow label="Reference" error="A reference is required"><input id="reference" aria-describedby="external-detail" /></FormRow></>);
    const field = screen.getByRole("textbox", { name: "Reference" });
    expect(field).toHaveAttribute("id", "reference");
    expect(field).toHaveAttribute("aria-invalid", "true");
    expect(field).toHaveAccessibleDescription("Retained input scope A reference is required");
  });
  it("forwards form-row names and validation into boolean and numeric primitives", () => {
    render(<><FormRow label="Enable alerts" error="Review the alert policy"><Checkbox checked={false} /></FormRow><FormRow label="Threshold" hint="USD minimum"><NumberInput value={100} /></FormRow></>);
    expect(screen.getByRole("checkbox", { name: "Enable alerts" })).toHaveAttribute("aria-invalid", "true");
    expect(screen.getByRole("checkbox", { name: "Enable alerts" })).toHaveAccessibleDescription("Review the alert policy");
    expect(screen.getByRole("spinbutton", { name: "Threshold" })).toHaveAccessibleDescription("USD minimum");
  });
  it("names progress tracks and numeric inputs from their visible labels", async () => {
    const user = userEvent.setup(); const onChange = vi.fn(); const submit = vi.fn();
    render(<form onSubmit={(event) => { event.preventDefault(); submit(); }}><ProgressBar label="Backfill progress" value={40} /><NumberInput label="Lot size" value={10} min={0} max={20} step={5} onChange={onChange} /></form>);
    expect(screen.getByRole("progressbar", { name: "Backfill progress" })).toHaveAttribute("aria-valuenow", "40");
    expect(screen.getByRole("spinbutton", { name: "Lot size" })).toHaveValue(10);
    await user.click(screen.getByRole("button", { name: "Increase Lot size" }));
    expect(onChange).toHaveBeenCalledWith(15);
    expect(submit).not.toHaveBeenCalled();
  });
  it("labels read-only reconciliation controls and sends raw booleans for the stated loaded scope", async () => {
    const user = userEvent.setup(); const onToggleItem = vi.fn(); const onToggleAll = vi.fn();
    const left = { title: "Statement", items: [{ id: "a", ref: "A", memo: "Alpha", amount: 100 }, { id: "b", ref: "B", memo: "Beta", amount: 20, matched: true }] };
    const right = { title: "Ledger", items: [] };
    const view = render(<ReconciliationPanel left={left} right={right} onToggleItem={onToggleItem} onToggleAll={onToggleAll} />);
    await user.click(screen.getByRole("checkbox", { name: "Match Statement item A" }));
    expect(onToggleItem).toHaveBeenCalledWith("a", true);
    expect(screen.getByRole("checkbox", { name: "Match Statement item A" })).not.toBeChecked();
    await user.type(screen.getByRole("textbox", { name: "Filter reconciliation items" }), "Alpha");
    await user.click(screen.getByRole("checkbox", { name: "Mark all 2 loaded Statement items as matched" }));
    expect(onToggleAll).toHaveBeenCalledWith(left, true);
    view.rerender(<ReconciliationPanel left={left} right={right} />);
    expect(screen.getByRole("checkbox", { name: "Mark all 2 loaded Statement items as matched" })).toBeDisabled();
    expect(screen.getByRole("checkbox", { name: "Match Statement item A" })).toBeDisabled();
  });
});

describe("reference select and split-pane keyboard behavior", () => {
  const Select = reference<Record<string, unknown>>("Select");
  const SplitPane = reference<Record<string, unknown>>("SplitPane");
  it("opens a named select, skips separators/disabled choices, and returns focus after selection", async () => {
    const user = userEvent.setup(); const changed = vi.fn();
    function Controlled() {
      const [value, setValue] = useState("live");
      return <><Select label="Environment" value={value} onChange={(next: string) => { changed(next); setValue(next); }} options={[{ value: "live", label: "Live" }, "---", { value: "paper", label: "Paper", disabled: true }, { value: "fixture", label: "Fixture" }]} /><button>Next field</button></>;
    }
    render(<Controlled />);
    const trigger = screen.getByRole("button", { name: "Environment Live" });
    await user.tab(); expect(trigger).toHaveFocus();
    await user.keyboard("{ArrowDown}"); expect(screen.getByRole("option", { name: "Live" })).toHaveFocus();
    await user.keyboard("{ArrowDown}"); expect(screen.getByRole("option", { name: "Fixture" })).toHaveFocus();
    await user.keyboard("{Enter}"); expect(changed).toHaveBeenCalledWith("fixture");
    expect(screen.queryByRole("listbox")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Environment Fixture" })).toHaveFocus();
    await user.keyboard("{ArrowUp}{Home}"); expect(screen.getByRole("option", { name: "Live" })).toHaveFocus();
    await user.keyboard("{Escape}"); expect(screen.getByRole("button", { name: "Environment Fixture" })).toHaveFocus();
    expect(changed).toHaveBeenCalledTimes(1);
    await user.keyboard("{ArrowDown}{Tab}"); expect(screen.getByRole("button", { name: "Next field" })).toHaveFocus();
  });
  it("clamps the initial panel to its declared bounds and remains usable with storage denied", async () => {
    const user = userEvent.setup();
    const read = vi.spyOn(Storage.prototype, "getItem").mockImplementation(() => { throw new Error("Storage denied"); });
    const write = vi.spyOn(Storage.prototype, "setItem").mockImplementation(() => { throw new Error("Storage denied"); });
    try {
      render(<SplitPane initial={900} min={200} max={400} persistKey="contract"><div>Records</div><div>Evidence</div></SplitPane>);
      const resize = screen.getByRole("separator", { name: "Resize panel" });
      expect(resize).toHaveAttribute("aria-valuenow", "400");
      resize.focus(); await user.keyboard("{ArrowLeft}");
      expect(resize).toHaveAttribute("aria-valuenow", "392");
      await user.keyboard("{Home}"); expect(resize).toHaveAttribute("aria-valuenow", "200");
    } finally { read.mockRestore(); write.mockRestore(); }
  });
});


describe("reference ledger inspection keyboard contract", () => {
  const LedgerTable = reference<Record<string, unknown>>("accounting/LedgerTable", "LedgerTable");
  it("moves one row tab stop and inspects the exact source row after sorting, without child activation bubbling", async () => {
    const user = userEvent.setup(); const inspect = vi.fn(); const nested = vi.fn();
    const rows = [{ date: "2026-06-02", ref: "B", memo: "First source", debit: 10 }, { date: "2026-06-01", ref: "A", memo: <button onClick={nested}>Retained source</button>, credit: 10, status: "void" }];
    const view = render(<LedgerTable rows={rows} onRowClick={inspect} selectedIndex={0} />);
    const dateHeader = screen.getByRole("columnheader", { name: /^Date/ });
    dateHeader.focus();
    await user.keyboard("{Enter}"); expect(dateHeader).toHaveFocus();
    expect(dateHeader).toHaveAttribute("aria-sort", "ascending");
    await user.keyboard("{Enter}"); expect(dateHeader).toHaveFocus();
    expect(dateHeader).toHaveAttribute("aria-sort", "descending");
    await user.keyboard("{Enter}"); expect(dateHeader).toHaveFocus();
    const first = screen.getByText("First source").closest("tr")!;
    const second = screen.getByText("Retained source").closest("tr")!;
    first.focus(); await user.keyboard("{Home}"); expect(second).toHaveFocus();
    expect([first, second].filter((row) => row.tabIndex === 0)).toEqual([second]);
    await user.keyboard("{Enter}"); expect(inspect).toHaveBeenLastCalledWith(rows[1], 1);
    await user.keyboard("{End} "); expect(inspect).toHaveBeenLastCalledWith(rows[0], 0);
    inspect.mockClear(); screen.getByRole("button", { name: "Retained source" }).focus();
    await user.keyboard("{Enter}"); expect(nested).toHaveBeenCalledTimes(1); expect(inspect).not.toHaveBeenCalled();
    view.rerender(<LedgerTable rows={rows} />);
    expect(first).not.toHaveAttribute("tabindex"); expect(second).not.toHaveAttribute("tabindex");
  });
});

describe("reference blocking-overlay focus contract", () => {
  const Modal = reference<{ open: boolean; onClose?: () => void; children: React.ReactNode }>("Modal");
  it("skips unavailable and negative-tabindex controls, cycles actual stops, and restores the trigger", async () => {
    const user = userEvent.setup();
    function Example() {
      const [open, setOpen] = useState(false);
      return <><button onClick={() => setOpen(true)}>Review record</button><Modal open={open} onClose={() => setOpen(false)}>
        <div data-autofocus>Plain text is not a focus target</div>
        <div hidden><button>Hidden ancestor</button></div>
        <div inert><button>Inert ancestor</button></div>
        <div aria-hidden="true"><button>Hidden from accessibility tree</button></div>
        <div style={{ display: "none" }}><button>Not displayed</button></div>
        <div style={{ visibility: "hidden" }}><button>Not visible</button></div>
        <fieldset disabled><button>Disabled by fieldset</button></fieldset>
        <details><summary tabIndex={-1}>Closed evidence</summary><button>Collapsed content</button></details>
        <input type="hidden" />
        <button tabIndex={-2}>Programmatic stop only</button>
        <button style={{ position: "fixed" }}>First available</button>
        <button onClick={() => setOpen(false)}>Save and close</button>
      </Modal></>;
    }
    render(<Example />);
    const trigger = screen.getByRole("button", { name: "Review record" });
    await user.click(trigger);
    expect(screen.getByRole("button", { name: "First available" })).toHaveFocus();
    await user.keyboard("{Shift>}{Tab}{/Shift}");
    expect(screen.getByRole("button", { name: "Save and close" })).toHaveFocus();
    await user.tab(); expect(screen.getByRole("button", { name: "First available" })).toHaveFocus();
    await user.tab(); expect(screen.getByRole("button", { name: "Save and close" })).toHaveFocus();
    await user.keyboard("{Enter}"); expect(screen.queryByRole("dialog")).not.toBeInTheDocument(); expect(trigger).toHaveFocus();
  });
  it("honors a safe negative-tabindex initial target without adding it to the Tab cycle", async () => {
    const user = userEvent.setup();
    render(<Modal open>
      <div hidden><h2 tabIndex={-1} data-autofocus>Hidden heading</h2></div>
      <h2 tabIndex={-1} data-autofocus>Review evidence</h2>
      <button>First action</button><button>Last action</button>
    </Modal>);
    expect(screen.getByRole("heading", { name: "Review evidence" })).toHaveFocus();
    await user.tab(); expect(screen.getByRole("button", { name: "First action" })).toHaveFocus();
    await user.keyboard("{Shift>}{Tab}{/Shift}"); expect(screen.getByRole("button", { name: "Last action" })).toHaveFocus();
    await user.tab(); expect(screen.getByRole("button", { name: "First action" })).toHaveFocus();
  });
  it("keeps a read-only overlay focusable when it has no controls and removes its temporary tab stop on close", async () => {
    const user = userEvent.setup();
    const view = render(<><button>Open evidence</button><Modal open={false}>Read-only evidence</Modal></>);
    const trigger = screen.getByRole("button", { name: "Open evidence" }); trigger.focus();
    view.rerender(<><button>Open evidence</button><Modal open>Read-only evidence</Modal></>);
    const panel = screen.getByRole("dialog"); expect(panel).toHaveFocus(); expect(panel).toHaveAttribute("tabindex", "-1");
    await user.tab(); expect(panel).toHaveFocus();
    await user.keyboard("{Shift>}{Tab}{/Shift}"); expect(panel).toHaveFocus();
    view.rerender(<><button>Open evidence</button><Modal open={false}>Read-only evidence</Modal></>);
    expect(trigger).toHaveFocus(); expect(panel).not.toHaveAttribute("tabindex");
  });
});
