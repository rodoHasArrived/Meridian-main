import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import {
  buildWatchlistRowActions,
  ContextMenu,
  useWatchlistRowActions,
  WatchlistRowActionsTrigger,
  type UseWatchlistRowActionsOptions
} from "./watchlist-screen.row-actions";
import type { ToastApi } from "@/components/ui/toast";
import type { WatchlistRowViewModel } from "@/screens/watchlist-screen.view-model";

function makeRow(overrides: Partial<WatchlistRowViewModel> = {}): WatchlistRowViewModel {
  return {
    symbol: "AAPL",
    isRemoving: false,
    removeLabel: "Remove",
    quoteHref: "/data/quotes?symbol=AAPL",
    ...overrides
  } as WatchlistRowViewModel;
}

function stubToast(): ToastApi {
  return {
    dismiss: vi.fn(),
    show: vi.fn(() => ""),
    success: vi.fn(() => ""),
    warning: vi.fn(() => ""),
    danger: vi.fn(() => ""),
    info: vi.fn(() => "")
  };
}

function actionIds(row: WatchlistRowViewModel) {
  return buildWatchlistRowActions(row, () => undefined)
    .filter((entry) => entry.type !== "divider")
    .map((entry) => entry.id);
}

describe("buildWatchlistRowActions", () => {
  it("projects the row actions with a destructive remove last", () => {
    expect(actionIds(makeRow())).toEqual(["inspect", "open-quote", "copy-symbol", "remove"]);
    const remove = buildWatchlistRowActions(makeRow(), () => undefined).find((entry) => entry.id === "remove");
    expect(remove?.type !== "divider" && remove?.danger).toBe(true);
  });

  it("disables remove while the row mutation is in flight", () => {
    const entries = buildWatchlistRowActions(makeRow({ isRemoving: true }), () => undefined, { busy: true });
    const remove = entries.find((entry) => entry.id === "remove");
    expect(remove?.type !== "divider" && remove?.disabled).toBe(true);
  });

  it("keeps the non-destructive actions enabled", () => {
    const entries = buildWatchlistRowActions(makeRow(), () => undefined);
    for (const id of ["inspect", "open-quote", "copy-symbol"] as const) {
      const entry = entries.find((candidate) => candidate.id === id);
      expect(entry && entry.type !== "divider" ? entry.disabled : undefined).toBeFalsy();
    }
  });
});

function Harness({
  row,
  options
}: {
  row: WatchlistRowViewModel;
  options: UseWatchlistRowActionsOptions;
}) {
  const rowActions = useWatchlistRowActions(options);
  return (
    <div>
      <WatchlistRowActionsTrigger label="Open actions" onOpen={(event) => rowActions.openFor(event, row)} />
      <ContextMenu
        open={rowActions.menu.open}
        position={rowActions.menu.position}
        items={rowActions.menu.items}
        onClose={rowActions.menu.close}
        label={rowActions.menu.label}
      />
    </div>
  );
}

function baseOptions(overrides: Partial<UseWatchlistRowActionsOptions> = {}): UseWatchlistRowActionsOptions {
  return {
    onInspect: vi.fn(),
    onOpenQuote: vi.fn(),
    onRemove: vi.fn(),
    copySymbol: vi.fn().mockResolvedValue(true),
    toast: stubToast(),
    ...overrides
  };
}

describe("useWatchlistRowActions", () => {
  it("opens the menu from the trigger and inspects the targeted symbol", async () => {
    const user = userEvent.setup();
    const onInspect = vi.fn();

    render(<Harness row={makeRow({ symbol: "MSFT" })} options={baseOptions({ onInspect })} />);

    await user.click(screen.getByRole("button", { name: "Open actions" }));
    await user.click(await screen.findByRole("menuitem", { name: "Inspect symbol" }));

    expect(onInspect).toHaveBeenCalledWith("MSFT");
  });

  it("opens the live quote for the targeted row", async () => {
    const user = userEvent.setup();
    const onOpenQuote = vi.fn();
    const row = makeRow({ symbol: "TSLA", quoteHref: "/data/quotes?symbol=TSLA" });

    render(<Harness row={row} options={baseOptions({ onOpenQuote })} />);

    await user.click(screen.getByRole("button", { name: "Open actions" }));
    await user.click(await screen.findByRole("menuitem", { name: "Open live quote" }));

    expect(onOpenQuote).toHaveBeenCalledTimes(1);
    expect(onOpenQuote.mock.calls[0][0]).toMatchObject({ symbol: "TSLA", quoteHref: "/data/quotes?symbol=TSLA" });
  });

  it("copies the ticker to the clipboard and reports success on the toast", async () => {
    const user = userEvent.setup();
    const copySymbol = vi.fn().mockResolvedValue(true);
    const toast = stubToast();

    render(<Harness row={makeRow({ symbol: "NVDA" })} options={baseOptions({ copySymbol, toast })} />);

    await user.click(screen.getByRole("button", { name: "Open actions" }));
    await user.click(await screen.findByRole("menuitem", { name: "Copy symbol" }));

    expect(copySymbol).toHaveBeenCalledWith("NVDA");
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Copied NVDA"));
  });

  it("surfaces a clipboard failure on the toast", async () => {
    const user = userEvent.setup();
    const copySymbol = vi.fn().mockResolvedValue(false);
    const toast = stubToast();

    render(<Harness row={makeRow({ symbol: "AMD" })} options={baseOptions({ copySymbol, toast })} />);

    await user.click(screen.getByRole("button", { name: "Open actions" }));
    await user.click(await screen.findByRole("menuitem", { name: "Copy symbol" }));

    expect(copySymbol).toHaveBeenCalledWith("AMD");
    await waitFor(() =>
      expect(toast.danger).toHaveBeenCalledWith("Could not copy AMD", "Clipboard access was blocked.")
    );
  });

  it("removes the targeted symbol from the watchlist", async () => {
    const user = userEvent.setup();
    const onRemove = vi.fn();

    render(<Harness row={makeRow({ symbol: "GME" })} options={baseOptions({ onRemove })} />);

    await user.click(screen.getByRole("button", { name: "Open actions" }));
    await user.click(await screen.findByRole("menuitem", { name: "Remove" }));

    expect(onRemove).toHaveBeenCalledWith("GME");
  });

  it("surfaces the two-step remove confirmation state in the menu label", async () => {
    const user = userEvent.setup();
    const onRemove = vi.fn();
    // Row already awaiting its confirmation click: the view-model relabels remove.
    const row = makeRow({ symbol: "GME", removeLabel: "Confirm remove" });

    render(<Harness row={row} options={baseOptions({ onRemove })} />);

    await user.click(screen.getByRole("button", { name: "Open actions" }));
    expect(await screen.findByRole("menuitem", { name: "Confirm remove" })).toBeInTheDocument();
    expect(screen.queryByRole("menuitem", { name: "Remove" })).not.toBeInTheDocument();
  });
});
