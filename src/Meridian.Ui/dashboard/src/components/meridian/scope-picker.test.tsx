import { useState, type ComponentProps } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { axe } from "jest-axe";
import { ScopePicker } from "@/components/meridian/scope-picker";
import type { AppShellOperatingScopeInput } from "@/app-shell.operating-scope";
import type { ScopeFundAccountOption } from "@/lib/operating-scope/fund-accounts";
import type { ScopeStructureOptions } from "@/lib/operating-scope/structure-options";
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

  it("hosts workflow context under the scope controls and closes when one of its links is followed", () => {
    const { onOpenChange, onApply } = renderPicker({
      workflowContext: <a href="#next-step" onClick={(event) => event.preventDefault()}>Next workflow step</a>
    });

    const dialog = screen.getByRole("dialog", { name: "Operating scope" });
    const link = screen.getByRole("link", { name: "Next workflow step" });
    expect(dialog).toContainElement(link);
    fireEvent.click(link);
    expect(onOpenChange).toHaveBeenCalledWith(false);
    expect(onApply).not.toHaveBeenCalled();
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

    act(() => screen.getByLabelText("Subject symbol").focus());
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
      entityId: null,
      fundId: null,
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
  it("shows the retained run and every window value with screen applicability", () => {
    renderPicker({
      scope: { runId: "run-9", from: "2026-01-01", to: "2026-01-31", date: "2026-01-15", asOf: "2026-01-16" },
      scopeDimensionsInEffect: ["fundAccountId", "window"]
    });
    expect(screen.getByRole("region", { name: "Retained scope" })).toHaveTextContent("run-9");
    expect(screen.getByText("2026-01-01 to 2026-01-31 · date 2026-01-15 · as of 2026-01-16")).toBeInTheDocument();
    expect(screen.getByRole("group", { name: "Period" })).toHaveAccessibleDescription(/Applied on this screen/);
    expect(screen.getByLabelText("Subject symbol")).toHaveAccessibleDescription(/Retained for other screens/);
    expect(screen.getByLabelText("Entity")).toHaveAccessibleDescription("Retained for other screens");
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
    act(() => input.focus());
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
    act(() => input.focus());
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
    act(() => screen.getByLabelText("Subject symbol").focus());
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
    act(() => input.focus());
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
    act(() => input.focus());
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
    act(() => input.focus());
    fireEvent.change(input, { target: { value: "ms" } });
    await waitFor(() => expect(searchSymbols).toHaveBeenCalledTimes(1));
    fireEvent.change(input, { target: { value: "aa" } });
    expect(searchSymbols.mock.calls[0][1].signal.aborted).toBe(true);
    await screen.findByRole("option", { name: /AAPL/ });
    await act(async () => { resolveOld([symbolRecord("MSFT", "alpaca")]); });
    expect(screen.queryByRole("option", { name: /MSFT/ })).not.toBeInTheDocument();
    expect(screen.getByRole("option", { name: /AAPL/ })).toBeInTheDocument();
  });

  it("loads a recent scope into the draft; Apply is still what changes scope (C1)", () => {
    const { onApply } = renderPicker({ recentScopes: [{ symbol: "MSFT", fundAccountId: "fund-b" }] });
    fireEvent.click(screen.getByRole("button", { name: "Use recent scope account fund-b · MSFT" }));
    expect(onApply).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole("button", { name: "Apply scope" }));
    expect(onApply).toHaveBeenCalledWith(expect.objectContaining({ symbol: "MSFT", fundAccountId: "fund-b" }));
  });

  it("saves the draft as the operator's default, and clears it", async () => {
    const onSaveDefault = vi.fn().mockResolvedValue(undefined);
    renderPicker({ scope: { symbol: "AAPL" }, defaultScope: { provider: "alpaca" }, onSaveDefault });

    expect(screen.getByText(/alpaca\. Applied when you open the workstation/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Save as my default" }));
    await waitFor(() => expect(onSaveDefault).toHaveBeenCalledWith(expect.objectContaining({ symbol: "AAPL" })));
    expect(await screen.findByText("Saved as your default scope.")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Clear default" }));
    await waitFor(() => expect(onSaveDefault).toHaveBeenLastCalledWith(null));
  });

  it("reports a failed default save without changing scope", async () => {
    const onSaveDefault = vi.fn().mockRejectedValue(new Error("offline"));
    const { onApply } = renderPicker({ scope: { symbol: "AAPL" }, onSaveDefault });
    fireEvent.click(screen.getByRole("button", { name: "Save as my default" }));
    expect(await screen.findByText(/could not be saved/)).toBeInTheDocument();
    expect(onApply).not.toHaveBeenCalled();
  });
});

describe("ScopePicker entity, fund and period", () => {
  afterEach(() => cleanup());

  // Two entities; Fund One holds Entity A, Fund Two holds Entity B. Account a-1 is in Fund One,
  // b-1 in Fund Two, and the household-only account has no recorded fund or entity.
  const STRUCTURE: ScopeStructureOptions = {
    status: "ready",
    entities: [{ id: "entity-a", label: "Entity A" }, { id: "entity-b", label: "Entity B" }],
    funds: [
      { id: "fund-one", label: "Fund One", entityIds: ["entity-a"] },
      { id: "fund-two", label: "Fund Two", entityIds: ["entity-b"] }
    ],
    accounts: [
      { id: "acct-a-1", label: "A1 brokerage", fundId: "fund-one", entityId: "entity-a" },
      { id: "acct-b-1", label: "B1 brokerage", fundId: "fund-two", entityId: "entity-b" }
    ]
  };
  const HOUSEHOLD: ScopeFundAccountOption[] = [{ id: "acct-household", label: "Household only" }];
  const accountOptionValues = () =>
    [...(screen.queryByTestId("scope-fund-account-options")?.querySelectorAll("option") ?? [])].map((option) => option.getAttribute("value"));

  it("narrows funds by entity and accounts by fund, and says what it hid", () => {
    renderPicker({ structure: STRUCTURE, fundAccounts: HOUSEHOLD });
    expect(accountOptionValues()).toEqual(["acct-a-1", "acct-b-1", "acct-household"]);

    fireEvent.change(screen.getByLabelText("Entity"), { target: { value: "entity-b" } });
    const fundOptions = [...screen.getByLabelText("Fund").querySelectorAll("option")].map((option) => option.textContent);
    expect(fundOptions).toEqual(["All authorized", "Fund Two"]);
    expect(screen.getByLabelText("Fund")).toHaveAccessibleDescription(/Showing 1 fund holding the chosen entity/);
    expect(accountOptionValues()).toEqual(["acct-b-1"]);
    expect(screen.getByLabelText("Fund account")).toHaveAccessibleDescription(/1 with no recorded entity not shown/);

    fireEvent.change(screen.getByLabelText("Fund"), { target: { value: "fund-two" } });
    expect(accountOptionValues()).toEqual(["acct-b-1"]);
    expect(screen.getByLabelText("Fund account")).toHaveAccessibleDescription(/Showing 1 account under the chosen fund/);
  });

  it("clears a fund and account the new entity does not hold, and applies entity and fund", () => {
    const { onApply } = renderPicker({
      structure: STRUCTURE,
      fundAccounts: HOUSEHOLD,
      scope: { fundId: "fund-one", fundAccountId: "acct-a-1" }
    });

    fireEvent.change(screen.getByLabelText("Entity"), { target: { value: "entity-b" } });
    expect(screen.getByLabelText("Fund")).toHaveValue("");
    expect(screen.getByLabelText("Fund account")).toHaveValue("");
    expect(screen.getByText("Cleared the fund and fund account: not under the chosen entity.")).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText("Fund"), { target: { value: "fund-two" } });
    fireEvent.click(screen.getByRole("button", { name: "Apply scope" }));
    expect(onApply).toHaveBeenCalledWith(expect.objectContaining({ entityId: "entity-b", fundId: "fund-two", fundAccountId: null }));
  });

  it("keeps an account whose fund the data does not state when the fund changes", () => {
    renderPicker({ structure: STRUCTURE, fundAccounts: HOUSEHOLD, scope: { fundAccountId: "acct-household" } });
    fireEvent.change(screen.getByLabelText("Fund"), { target: { value: "fund-one" } });
    expect(screen.getByLabelText("Fund account")).toHaveValue("acct-household");
  });

  it("takes typed entity and fund ids, and narrows nothing, without the organization read model", () => {
    const { onApply } = renderPicker({ fundAccounts: HOUSEHOLD });

    expect(screen.getByText(/Entity and fund lists could not be loaded, so enter an id/)).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("Entity"), { target: { value: "entity-x" } });
    fireEvent.change(screen.getByLabelText("Fund"), { target: { value: "fund-x" } });
    expect(accountOptionValues()).toEqual(["acct-household"]);
    fireEvent.click(screen.getByRole("button", { name: "Apply scope" }));
    expect(onApply).toHaveBeenCalledWith(expect.objectContaining({ entityId: "entity-x", fundId: "fund-x" }));
  });

  it("offers a permission-limited operator its accounts and says no entities or funds are visible", () => {
    // What /api/workstation/scope-options returns to a trading-only operator: its accounts, with
    // the fund and entity it may not see withheld.
    const limited: ScopeStructureOptions = {
      status: "ready",
      entities: [],
      funds: [],
      accounts: [{ id: "acct-a-1", label: "A1 brokerage", fundId: null, entityId: null }]
    };
    renderPicker({ structure: limited, fundAccounts: [] });

    expect(screen.getByText("No entities or funds are visible to you, so enter an id.")).toBeInTheDocument();
    expect(screen.queryByText(/could not be loaded/)).not.toBeInTheDocument();
    expect(accountOptionValues()).toEqual(["acct-a-1"]);
  });

  it("keeps an entity id that is not in the operator's list visible instead of dropping it", () => {
    renderPicker({ structure: STRUCTURE, scope: { entityId: "entity-gone" } });
    expect(screen.getByLabelText("Entity")).toHaveValue("entity-gone");
    expect(screen.getByRole("option", { name: "entity-gone (not in your list)" })).toBeInTheDocument();
  });

  it("writes period presets on the operator's calendar and replaces a workflow date", () => {
    const now = new Date(2026, 10, 14, 23, 30);
    const { onApply } = renderPicker({ now, scope: { date: "2026-01-15" } });
    expect(screen.getByText(/\(set by the originating workflow\)/)).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Last month" }));
    expect(screen.getByRole("button", { name: "Last month" })).toHaveAttribute("aria-pressed", "true");
    expect(screen.getByText("2026-10-01 to 2026-10-31")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Apply scope" }));
    expect(onApply).toHaveBeenCalledWith(expect.objectContaining({ from: "2026-10-01", to: "2026-10-31", date: null, asOf: null }));
  });

  it("recognises the active preset and clears the window with All periods", () => {
    const now = new Date(2026, 10, 14, 9, 0);
    const { onApply } = renderPicker({ now, scope: { from: "2026-10-01", to: "2026-11-14" } });

    expect(screen.getByRole("button", { name: "QTD" })).toHaveAttribute("aria-pressed", "true");
    fireEvent.click(screen.getByRole("button", { name: "All periods" }));
    expect(within(screen.getByRole("group", { name: "Period" })).getByText("All periods", { selector: "span" })).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Apply scope" }));
    expect(onApply).toHaveBeenCalledWith({});
  });

  it("opens the shared date-range picker for a custom range and an as-of date field", () => {
    const now = new Date(2026, 10, 14, 9, 0);
    renderPicker({ now, scope: { from: "2026-03-02", to: "2026-03-09" } });

    expect(screen.getByRole("button", { name: "Custom range" })).toHaveAttribute("aria-pressed", "true");
    expect(screen.getByText("Custom range", { selector: "label" })).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "As of" }));
    expect(screen.getByText("As of date", { selector: "label" })).toBeInTheDocument();
    expect(screen.getByText("All periods", { selector: "span" })).toBeInTheDocument();
  });

  it("stages entity, fund and period in a scope reset", () => {
    const { onApply } = renderPicker({ scope: { entityId: "entity-a", fundId: "fund-one", from: "2026-11-01", to: "2026-11-14" } });
    fireEvent.click(screen.getByRole("button", { name: "Clear scope" }));
    expect(screen.getByLabelText("Entity")).toHaveValue("");
    expect(screen.getByLabelText("Fund")).toHaveValue("");
    fireEvent.click(screen.getByRole("button", { name: "Apply scope" }));
    expect(onApply).toHaveBeenCalledWith({});
  });
});
