import { useState, type ComponentProps } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { axe } from "jest-axe";
import { ScopePicker } from "@/components/meridian/scope-picker";
import type { AppShellOperatingScopeInput } from "@/app-shell.operating-scope";
import type { ScopeFundAccountOption } from "@/lib/operating-scope/fund-accounts";
import type { SymbolRecord } from "@/types";

const FUND_ACCOUNTS: ScopeFundAccountOption[] = [
  { id: "fund-a", label: "Alpha Fund" },
  { id: "fund-b", label: "Beta Fund" }
];

function symbolRecord(symbol: string, provider: string): SymbolRecord {
  return { symbol, provider } as unknown as SymbolRecord;
}

function renderPicker(overrides: Partial<ComponentProps<typeof ScopePicker>> = {}) {
  const onApply = vi.fn();
  const onOpenChange = vi.fn();
  const scope: AppShellOperatingScopeInput = overrides.scope ?? {};
  const searchSymbols = overrides.searchSymbols ?? vi.fn().mockResolvedValue([]);
  render(
    <ScopePicker
      open
      onOpenChange={onOpenChange}
      scope={scope}
      fundAccounts={FUND_ACCOUNTS}
      onApply={onApply}
      searchSymbols={searchSymbols}
      {...overrides}
    />
  );
  return { onApply, onOpenChange, searchSymbols };
}

describe("ScopePicker", () => {
  afterEach(() => {
    cleanup();
    vi.useRealTimers();
  });

  it("seeds the fields from the active scope when opened", () => {
    renderPicker({ scope: { symbol: "AAPL", fundAccountId: "fund-a", provider: "alpaca" } });

    expect(screen.getByLabelText("Subject symbol")).toHaveValue("AAPL");
    expect(screen.getByLabelText("Fund account")).toHaveValue("fund-a");
    expect(screen.getByLabelText("Provider")).toHaveValue("alpaca");
  });

  it("shows live symbol suggestions and applies the clicked one", async () => {
    const searchSymbols = vi.fn().mockResolvedValue([symbolRecord("MSFT", "alpaca"), symbolRecord("MSTR", "alpaca")]);
    const { onApply } = renderPicker({ searchSymbols });

    fireEvent.change(screen.getByLabelText("Subject symbol"), { target: { value: "ms" } });

    const suggestion = await screen.findByRole("option", { name: /MSFT/ });
    fireEvent.click(suggestion);

    expect(searchSymbols).toHaveBeenCalledWith("ms", expect.objectContaining({ signal: expect.any(AbortSignal) }));
    expect(screen.getByLabelText("Subject symbol")).toHaveValue("MSFT");

    fireEvent.click(screen.getByRole("button", { name: "Apply scope" }));
    expect(onApply).toHaveBeenCalledWith(expect.objectContaining({ symbol: "MSFT" }));
  });

  it("emits the complete next scope on apply, carrying untouched dimensions", () => {
    const { onApply, onOpenChange } = renderPicker({
      scope: { symbol: "AAPL", fundAccountId: "fund-a", provider: "alpaca", runId: "run-9", from: "2026-01-01" }
    });

    fireEvent.change(screen.getByLabelText("Provider"), { target: { value: "polygon" } });
    fireEvent.click(screen.getByRole("button", { name: "Apply scope" }));

    expect(onApply).toHaveBeenCalledWith({
      symbol: "AAPL",
      fundAccountId: "fund-a",
      provider: "polygon",
      runId: "run-9",
      from: "2026-01-01",
      to: null,
      date: null,
      asOf: null
    });
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it("stages the entire scope reset until Apply is selected", () => {
    const { onApply } = renderPicker({ scope: { symbol: "AAPL", provider: "alpaca", runId: "run-9", asOf: "2026-01-16" } });

    fireEvent.click(screen.getByRole("button", { name: "Clear scope" }));

    expect(onApply).not.toHaveBeenCalled();
    expect(screen.getByLabelText("Subject symbol")).toHaveValue("");
    expect(screen.getByText(/Scope reset staged/)).toBeInTheDocument();
    expect(screen.queryByRole("region", { name: "Retained scope" })).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Apply scope" }));
    expect(onApply).toHaveBeenCalledWith({});
  });
  it("shows retained date and run values with screen applicability", () => {
    renderPicker({
      scope: { runId: "run-9", from: "2026-01-01", to: "2026-01-31", date: "2026-01-15", asOf: "2026-01-16" },
      scopeDimensionsInEffect: ["fundAccountId", "window"]
    });
    expect(screen.getByRole("region", { name: "Retained scope" })).toHaveTextContent("run-9");
    for (const value of ["2026-01-01", "2026-01-31", "2026-01-15", "2026-01-16"]) expect(screen.getByText(value)).toBeInTheDocument();
    expect(screen.getByLabelText("Subject symbol")).toHaveAccessibleDescription(/Retained for other screens/);
    expect(screen.getByLabelText("Fund account")).toHaveAccessibleDescription("Applied on this screen");
  });

  it("cancels a staged reset without changing the current scope", () => {
    const onApply = vi.fn();
    const scope = { symbol: "AAPL", runId: "run-9", asOf: "2026-01-16" };
    function Harness() {
      const [open, setOpen] = useState(false);
      return <><button onClick={() => setOpen(true)}>Edit scope</button><ScopePicker open={open} onOpenChange={setOpen} scope={scope} fundAccounts={[]} onApply={onApply} searchSymbols={vi.fn().mockResolvedValue([])} /></>;
    }
    render(<Harness />);
    const trigger = screen.getByRole("button", { name: "Edit scope" });
    trigger.focus();
    fireEvent.click(trigger);
    fireEvent.click(screen.getByRole("button", { name: "Clear scope" }));
    fireEvent.click(screen.getByRole("button", { name: "Cancel" }));
    expect(onApply).not.toHaveBeenCalled();
    expect(trigger).toHaveFocus();
    fireEvent.click(trigger);
    expect(screen.getByLabelText("Subject symbol")).toHaveValue("AAPL");
    expect(screen.getByText("run-9")).toBeInTheDocument();
  });

  it("reports failed searches and retries without losing manually typed input", async () => {
    const searchSymbols = vi.fn().mockRejectedValueOnce(new Error("offline")).mockResolvedValueOnce([]);
    renderPicker({ searchSymbols });
    const input = screen.getByRole("combobox", { name: "Subject symbol" });
    fireEvent.change(input, { target: { value: "CUSTOM" } });
    expect(screen.getByText("Searching symbols…")).toBeInTheDocument();
    const retry = await screen.findByRole("button", { name: "Retry symbol search" });
    expect(input).toHaveValue("CUSTOM");
    fireEvent.click(retry);
    expect(await screen.findByText(/No matching symbols/)).toBeInTheDocument();
    expect(searchSymbols).toHaveBeenCalledTimes(2);
    expect(input).toHaveValue("CUSTOM");
  });

  it("selects suggestions with arrow keys and Enter without changing provider", async () => {
    const searchSymbols = vi.fn().mockResolvedValue([symbolRecord("MSFT", "alpaca"), symbolRecord("MSTR", "alpaca")]);
    const { onApply } = renderPicker({ searchSymbols, scope: { provider: "polygon" } });
    const input = screen.getByRole("combobox", { name: "Subject symbol" });
    fireEvent.change(input, { target: { value: "ms" } });
    await screen.findByRole("option", { name: /MSFT/ });
    fireEvent.keyDown(input, { key: "ArrowDown" });
    expect(input).toHaveAttribute("aria-activedescendant", screen.getByRole("option", { name: /MSFT/ }).id);
    fireEvent.keyDown(input, { key: "ArrowDown" });
    fireEvent.keyDown(input, { key: "Enter" });
    expect(input).toHaveValue("MSTR");
    expect(input).toHaveAttribute("aria-expanded", "false");
    fireEvent.click(screen.getByRole("button", { name: "Apply scope" }));
    expect(onApply).toHaveBeenCalledWith(expect.objectContaining({ symbol: "MSTR", provider: "polygon" }));
  });

  it("dismisses suggestions on first Escape and the dialog on the second", async () => {
    const searchSymbols = vi.fn().mockResolvedValue([symbolRecord("MSFT", "alpaca")]);
    const { onOpenChange } = renderPicker({ searchSymbols });
    const input = screen.getByRole("combobox", { name: "Subject symbol" });
    fireEvent.change(input, { target: { value: "ms" } });
    await screen.findByRole("option", { name: /MSFT/ });
    fireEvent.keyDown(input, { key: "Escape" });
    expect(screen.queryByRole("listbox")).not.toBeInTheDocument();
    expect(onOpenChange).not.toHaveBeenCalled();
    fireEvent.keyDown(input, { key: "Escape" });
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it("exposes an accessible expanded symbol combobox", async () => {
    const searchSymbols = vi.fn().mockResolvedValue([symbolRecord("MSFT", "alpaca")]);
    renderPicker({ searchSymbols, scope: { runId: "run-9" }, scopeDimensionsInEffect: ["symbol"] });
    fireEvent.change(screen.getByLabelText("Subject symbol"), { target: { value: "ms" } });
    await screen.findByRole("option", { name: /MSFT/ });
    expect((await axe(screen.getByRole("dialog"))).violations).toEqual([]);
  });

  it("keeps delayed suggestions closed after leaving the symbol field and reopens on focus", async () => {
    let resolveSearch: (records: SymbolRecord[]) => void;
    const response = new Promise<SymbolRecord[]>((resolve) => { resolveSearch = resolve; });
    const searchSymbols = vi.fn().mockReturnValue(response);
    renderPicker({ searchSymbols });
    const input = screen.getByRole("combobox", { name: "Subject symbol" });
    await waitFor(() => expect(input).toHaveFocus());
    fireEvent.change(input, { target: { value: "ms" } });
    await waitFor(() => expect(searchSymbols).toHaveBeenCalledTimes(1));
    act(() => screen.getByLabelText("Fund account").focus());
    await act(async () => { resolveSearch([symbolRecord("MSFT", "alpaca")]); });
    expect(input).toHaveAttribute("aria-expanded", "false");
    expect(screen.queryByRole("listbox")).not.toBeInTheDocument();
    expect(screen.getByLabelText("Fund account")).toHaveFocus();
    act(() => input.focus());
    expect(screen.getByRole("option", { name: /MSFT/ })).toBeInTheDocument();
  });

  it("keeps failure feedback and Retry available when a pending search loses focus", async () => {
    let rejectSearch: (reason: Error) => void;
    const response = new Promise<SymbolRecord[]>((_resolve, reject) => { rejectSearch = reject; });
    const searchSymbols = vi.fn().mockReturnValueOnce(response).mockResolvedValueOnce([]);
    renderPicker({ searchSymbols });
    const input = screen.getByRole("combobox", { name: "Subject symbol" });
    await waitFor(() => expect(input).toHaveFocus());
    fireEvent.change(input, { target: { value: "CUSTOM" } });
    await waitFor(() => expect(searchSymbols).toHaveBeenCalledTimes(1));
    act(() => screen.getByLabelText("Fund account").focus());
    await act(async () => { rejectSearch(new Error("offline")); });
    fireEvent.click(screen.getByRole("button", { name: "Retry symbol search" }));
    expect(await screen.findByText(/No matching symbols/)).toBeInTheDocument();
    expect(input).toHaveValue("CUSTOM");
    expect(screen.queryByRole("listbox")).not.toBeInTheDocument();
  });

  it("ignores an older response after the query changes and aborts its request", async () => {
    let resolveOld: (records: SymbolRecord[]) => void;
    const oldResponse = new Promise<SymbolRecord[]>((resolve) => { resolveOld = resolve; });
    const searchSymbols = vi.fn().mockReturnValueOnce(oldResponse).mockResolvedValueOnce([symbolRecord("AAPL", "alpaca")]);
    renderPicker({ searchSymbols });
    const input = screen.getByRole("combobox", { name: "Subject symbol" });
    fireEvent.change(input, { target: { value: "ms" } });
    await waitFor(() => expect(searchSymbols).toHaveBeenCalledTimes(1));
    fireEvent.change(input, { target: { value: "aa" } });
    expect(searchSymbols.mock.calls[0][1].signal.aborted).toBe(true);
    await screen.findByRole("option", { name: /AAPL/ });
    await act(async () => { resolveOld([symbolRecord("MSFT", "alpaca")]); });
    expect(screen.queryByRole("option", { name: /MSFT/ })).not.toBeInTheDocument();
    expect(screen.getByRole("option", { name: /AAPL/ })).toBeInTheDocument();
  });
});
