import { existsSync, readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import React, { useState, type ComponentType } from "react";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import ts from "typescript";

function reference(name: string): Record<string, unknown> {
  const cache = new Map<string, { exports: Record<string, unknown> }>();
  const load = (input: string): { exports: Record<string, unknown> } => {
    const file = [input, `${input}.jsx`, `${input}.js`].find(existsSync);
    if (!file) throw new Error(`Missing reference ${input}`);
    const existing = cache.get(file); if (existing) return existing;
    const module = { exports: {} as Record<string, unknown> }; cache.set(file, module);
    const code = ts.transpileModule(readFileSync(file, "utf8"), { compilerOptions: { jsx: ts.JsxEmit.React, module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2020, esModuleInterop: true } }).outputText;
    new Function("require", "module", "exports", code)((id: string) => id === "react" ? React : load(resolve(dirname(file), id)).exports, module, module.exports);
    return module;
  };
  return load(resolve(process.cwd(), `../../../Meridian Design System/components/core/${name}.jsx`)).exports;
}

type DateProps = { label?: string; id?: string; value?: string; disabled?: boolean; onChange?: (value: string) => void; "aria-labelledby"?: string; "aria-describedby"?: string };
type RangeProps = { label?: string; start?: string | null; end?: string | null; onChange: (value: { start: string | null; end: string | null }) => void };
const DatePicker = reference("DatePicker").DatePicker as ComponentType<DateProps>;
const RangePicker = reference("DateRangePicker").DateRangePicker as ComponentType<RangeProps>;
const Modal = reference("Modal").Modal as ComponentType<{ open: boolean; onClose: () => void; children: React.ReactNode }>;
const ModalHeader = reference("Modal").ModalHeader as ComponentType<{ title: string }>;
const FormField = reference("FormField").FormField as ComponentType<{ label: string; children: React.ReactElement }>;
afterEach(() => vi.unstubAllEnvs());

describe("reference calendar fields", () => {
  it("preserves supplied IDs and label/feedback associations without submitting a surrounding form", async () => {
    const user = userEvent.setup(); const submitted = vi.fn();
    render(<form onSubmit={(event) => { event.preventDefault(); submitted(); }}><span id="trade-label">Trade date</span><span id="trade-help">Book calendar</span><DatePicker id="trade-date" value="2026-06-09" aria-labelledby="trade-label" aria-describedby="trade-help" /></form>);
    const trigger = screen.getByRole("button", { name: /Trade date Jun 9, 2026/ });
    expect(trigger).toHaveAttribute("id", "trade-date"); expect(trigger).toHaveAttribute("aria-describedby", "trade-help");
    trigger.focus(); await user.keyboard("{Enter}"); expect(screen.getByRole("dialog", { name: "Date calendar" })).toBeInTheDocument(); expect(submitted).not.toHaveBeenCalled();
    expect(screen.getByRole("button", { name: "Tuesday, June 9, 2026" })).toHaveFocus();
    await user.keyboard("{Escape}"); expect(screen.queryByRole("dialog")).not.toBeInTheDocument(); expect(trigger).toHaveFocus();
  });
  it("moves the actual date focus across days/months, emits a civil date, and returns focus", async () => {
    const user = userEvent.setup(); const changed = vi.fn();
    function Controlled() { const [value, setValue] = useState("2026-01-31"); return <DatePicker label="Valuation date" value={value} onChange={(next) => { changed(next); setValue(next); }} />; }
    render(<Controlled />); const trigger = screen.getByRole("button", { name: /Valuation date/ });
    trigger.focus(); await user.keyboard("{ArrowDown}"); await user.keyboard("{PageDown}");
    expect(screen.getByRole("button", { name: "Saturday, February 28, 2026" })).toHaveFocus();
    await user.keyboard("{ArrowRight}{Enter}"); expect(changed).toHaveBeenLastCalledWith("2026-03-01"); expect(trigger).toHaveFocus(); expect(trigger).toHaveTextContent("Mar 1, 2026");
  });
  it("completes an earlier-second-pick range in date order and closes", async () => {
    const user = userEvent.setup(); const changed = vi.fn();
    function Controlled() { const [range, setRange] = useState({ start: "2026-06-09" as string | null, end: "2026-06-15" as string | null }); return <RangePicker label="Reporting period" {...range} onChange={(next) => { changed(next); setRange(next); }} />; }
    render(<Controlled />); const trigger = screen.getByRole("button", { name: /Reporting period/ }); await user.click(trigger);
    const dialog = screen.getByRole("dialog", { name: "Reporting period calendar" });
    await user.keyboard("{Enter}"); expect(changed).toHaveBeenLastCalledWith({ start: "2026-06-09", end: null });
    expect(within(dialog).getAllByRole("grid")).toHaveLength(2);
    await user.keyboard("{ArrowLeft}{Enter}"); expect(changed).toHaveBeenLastCalledWith({ start: "2026-06-08", end: "2026-06-09" });
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument(); expect(trigger).toHaveFocus();
  });
  it("uses the first Escape for the calendar and the second for its package modal", async () => {
    const user = userEvent.setup(); const closed = vi.fn();
    render(<Modal open onClose={closed}><ModalHeader title="Review record" /><DatePicker label="Effective date" value="2026-06-09" /></Modal>);
    await user.click(screen.getByRole("button", { name: /Effective date/ })); await user.keyboard("{Escape}");
    expect(screen.queryByRole("dialog", { name: "Effective date calendar" })).not.toBeInTheDocument(); expect(closed).not.toHaveBeenCalled();
    await user.keyboard("{Escape}"); expect(closed).toHaveBeenCalledTimes(1);
  });
  it("honors external native labels and FormField-generated labels", () => {
    const view = render(<><label htmlFor="settlement">Settlement date</label><DatePicker id="settlement" value="2026-06-09" /></>);
    expect(screen.getByRole("button", { name: "Settlement date" })).toHaveAccessibleDescription("Jun 9, 2026");
    view.rerender(<FormField label="Book date"><DatePicker value="2026-06-09" /></FormField>);
    expect(screen.getByRole("button", { name: "Book date" })).toHaveAccessibleDescription("Jun 9, 2026");
  });
  it("keeps disabled fields closed and invalid dates explicit", async () => {
    const user = userEvent.setup(); const view = render(<DatePicker label="Effective date" disabled value="2026-06-09" />);
    await user.click(screen.getByRole("button", { name: /Effective date/ })); expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    view.rerender(<DatePicker label="Effective date" value="2026-02-31" />); expect(screen.getByRole("button", { name: /Effective date/ })).toHaveTextContent("Invalid date");
  });
  it.each(["America/Los_Angeles", "Pacific/Auckland"])("displays date-only values without a timezone day shift in %s", (timezone) => {
    vi.stubEnv("TZ", timezone); render(<DatePicker label="Trade date" value="2026-06-09" />);
    expect(screen.getByRole("button", { name: /Trade date/ })).toHaveTextContent("Jun 9, 2026");
  });
});
