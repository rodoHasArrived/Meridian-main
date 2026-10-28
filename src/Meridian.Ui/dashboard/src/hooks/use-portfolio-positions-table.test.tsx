import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter, useLocation, useNavigate } from "react-router-dom";
import { usePortfolioPositionsTable } from "@/hooks/use-portfolio-positions-table";
import { encodeViewStateEnvelope, readViewStateFromSearch } from "@/lib/view-state-envelope";

const SCREEN = "portfolio-workstation";
function routeWithState(state: Record<string, unknown>, screen = SCREEN) {
  const token = encodeViewStateEnvelope({ v: 1, screen, state });
  return `/portfolio?symbol=MSFT&fundAccountId=fund-1&other=keep&view=${token}#holding`;
}
function Harness() {
  const table = usePortfolioPositionsTable();
  const location = useLocation();
  const navigate = useNavigate();
  return <>
    <input aria-label="Symbol search" value={table.state.search} onChange={(e) => table.setSearch(e.target.value)} onBlur={table.flushSearch} />
    <button onClick={() => table.setSide("Long")}>Long</button>
    <button onClick={() => table.setReadiness("current")}>Current</button>
    <button onClick={() => table.toggleSort("symbol")}>Sort symbol</button>
    <button onClick={table.resetControls}>Reset controls</button>
    <button onClick={table.clearFilters}>Clear filters</button>
    <button onClick={() => navigate(-1)}>Back</button>
    <button onClick={() => navigate(1)}>Forward</button>
    <button onClick={() => navigate("/portfolio?symbol=IBM&external=1#new")}>External</button>
    <output data-testid="state">{JSON.stringify(table.state)}</output>
    <output data-testid="url">{location.pathname}{location.search}{location.hash}</output>
    {table.shareError ? <p role="alert">{table.shareError}</p> : null}
  </>;
}
function mount(initialEntries: string[] = ["/portfolio"], initialIndex?: number) {
  return render(<MemoryRouter initialEntries={initialEntries} initialIndex={initialIndex}><Harness /></MemoryRouter>);
}
function url() { return screen.getByTestId("url").textContent!; }
function state() { return JSON.parse(screen.getByTestId("state").textContent!); }
function viewState() { return readViewStateFromSearch(new URL(url(), "http://local").search, SCREEN)?.state; }
function search(value: string) { fireEvent.change(screen.getByRole("textbox", { name: "Symbol search" }), { target: { value } }); }

describe("usePortfolioPositionsTable", () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => { cleanup(); vi.useRealTimers(); });

  it.each([
    "/portfolio?view=malformed&symbol=MSFT",
    routeWithState({ selectedKind: "run", selectedId: "run-1" }),
    routeWithState({ positionsTable: { search: "foreign" } }, "trading-blotter")
  ])("defaults without rewriting old, malformed or foreign view URLs: %s", (initial) => {
    mount([initial]);
    expect(state()).toEqual({ search: "", side: null, readiness: "all", sort: null });
    act(() => vi.advanceTimersByTime(1000));
    expect(url()).toBe(initial);
  });

  it("hydrates saved controls and normalizes invalid fields independently", () => {
    mount([routeWithState({ positionsTable: { search: "Aa", side: "Long", readiness: "bad", sort: { columnId: "exposure", direction: "desc" } } })]);
    expect(state()).toEqual({ search: "Aa", side: "Long", readiness: "all", sort: null });
  });

  it("updates search immediately, debounces URL replacement, and preserves scope/hash/selection metadata", () => {
    mount([routeWithState({ selectedKind: "holding", selectedId: "aapl-long-0", custom: { keep: true } })]);
    const initial = url();
    search("AA");
    expect(state().search).toBe("AA");
    expect(url()).toBe(initial);
    act(() => vi.advanceTimersByTime(249));
    expect(url()).toBe(initial);
    search("AAPL");
    act(() => vi.advanceTimersByTime(250));
    expect(viewState()).toMatchObject({ selectedKind: "holding", selectedId: "aapl-long-0", custom: { keep: true }, positionsTable: { search: "AAPL" } });
    const parsed = new URL(url(), "http://local");
    expect(parsed.searchParams.get("symbol")).toBe("MSFT");
    expect(parsed.searchParams.get("fundAccountId")).toBe("fund-1");
    expect(parsed.searchParams.get("other")).toBe("keep");
    expect(parsed.hash).toBe("#holding");
  });

  it("flushes pending search before adjacent share controls receive their click", () => {
    mount();
    search("MSFT");
    fireEvent.blur(screen.getByRole("textbox"));
    expect(viewState()?.positionsTable).toMatchObject({ search: "MSFT" });
  });

  it("flushes the latest search with immediate filter/sort changes and resets appropriately", () => {
    mount();
    search("AAPL");
    fireEvent.click(screen.getByRole("button", { name: "Long" }));
    expect(viewState()?.positionsTable).toMatchObject({ search: "AAPL", side: "Long" });
    fireEvent.click(screen.getByRole("button", { name: "Current" }));
    fireEvent.click(screen.getByRole("button", { name: "Sort symbol" }));
    fireEvent.click(screen.getByRole("button", { name: "Sort symbol" }));
    expect(state().sort).toEqual({ columnId: "symbol", direction: "desc" });
    fireEvent.click(screen.getByRole("button", { name: "Clear filters" }));
    expect(state()).toEqual({ search: "", side: null, readiness: "all", sort: { columnId: "symbol", direction: "desc" } });
    fireEvent.click(screen.getByRole("button", { name: "Reset controls" }));
    expect(state()).toEqual({ search: "", side: null, readiness: "all", sort: null });
    act(() => vi.advanceTimersByTime(1000));
    expect(viewState()?.positionsTable).toEqual(state());
  });

  it("restores Back/Forward and cancels pending searches on external navigation", () => {
    mount();
    fireEvent.click(screen.getByRole("button", { name: "Long" }));
    fireEvent.click(screen.getByRole("button", { name: "Current" }));
    fireEvent.click(screen.getByRole("button", { name: "Back" }));
    expect(state()).toMatchObject({ side: "Long", readiness: "all" });
    fireEvent.click(screen.getByRole("button", { name: "Forward" }));
    expect(state()).toMatchObject({ side: "Long", readiness: "current" });
    search("pending");
    fireEvent.click(screen.getByRole("button", { name: "External" }));
    act(() => vi.advanceTimersByTime(1000));
    expect(state()).toEqual({ search: "", side: null, readiness: "all", sort: null });
    expect(url()).toBe("/portfolio?symbol=IBM&external=1#new");
  });

  it("cancels pending writes on unmount", () => {
    const view = mount();
    search("pending");
    expect(vi.getTimerCount()).toBe(1);
    view.unmount();
    expect(vi.getTimerCount()).toBe(0);
  });

  it("retains local input and the old token when merged state exceeds the URL budget", () => {
    const initial = routeWithState({ retainedEvidence: "x".repeat(1380) });
    expect(initial).not.toContain("view=null");
    mount([initial]);
    search("A".repeat(120));
    act(() => vi.advanceTimersByTime(250));
    expect(state().search).toBe("A".repeat(120));
    expect(url()).toBe(initial);
    expect(screen.getByRole("alert")).toHaveTextContent("could not fit");
  });
});
