import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { LayoutSwitcher } from "@/components/meridian/layout-switcher";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { ToastProvider } from "@/components/ui/toast";
import type { LayoutRestorePlan } from "@/lib/saved-layouts";

function memoryStorage(seed: Record<string, string> = {}): Storage {
  const map = new Map(Object.entries(seed));
  return {
    get length() {
      return map.size;
    },
    clear: () => map.clear(),
    getItem: (key: string) => map.get(key) ?? null,
    key: (index: number) => [...map.keys()][index] ?? null,
    removeItem: (key: string) => void map.delete(key),
    setItem: (key: string, value: string) => void map.set(key, value)
  } satisfies Storage;
}

function renderSwitcher(overrides: Partial<React.ComponentProps<typeof LayoutSwitcher>> = {}) {
  const onRestore = vi.fn();
  const storage = memoryStorage();
  const rendered = render(
    <ToastProvider>
      <LayoutSwitcher
        route="/accounting/reconciliation?fundAccountId=fund-a"
        operatingScope={{ fundAccountId: "fund-a" }}
        onRestore={onRestore}
        storage={storage}
        readDensity={() => "compact"}
        readPanes={() => ["live-quotes"]}
        generateId={() => "layout-test-id"}
        {...overrides}
      />
    </ToastProvider>
  );
  return { onRestore, storage, ...rendered };
}

function openMenu() {
  if (!screen.queryByRole("menu", { name: "Saved workspace layouts" })) {
    fireEvent.click(screen.getByRole("button", { name: "Saved workspace layouts" }));
  }
  return screen.getByRole("menu", { name: "Saved workspace layouts" });
}

describe("LayoutSwitcher", () => {
  afterEach(() => cleanup());

  it("reports a layout that cannot fit the retained token without closing or claiming success", async () => {
    const user = userEvent.setup();
    const { storage } = renderSwitcher({ route: `/portfolio?view=${"a".repeat(2200)}` });
    openMenu();
    await user.click(screen.getByRole("menuitem", { name: /Save current layout/ }));
    await user.type(screen.getByRole("textbox", { name: "Layout name" }), "Too large");
    await user.click(screen.getByRole("button", { name: "Save layout" }));
    expect(screen.getByRole("dialog", { name: "Save this layout" })).toBeInTheDocument();
    expect(screen.getByText("Layout could not be saved.")).toBeInTheDocument();
    expect(screen.queryByText("Saved layout Too large.")).not.toBeInTheDocument();
    expect(storage.length).toBe(0);
  });

  it("lists shared team presets and an empty state for user layouts", () => {
    renderSwitcher();
    const menu = openMenu();
    expect(within(menu).getByText("Month-End Close")).toBeInTheDocument();
    expect(within(menu).getByText("Trading Morning")).toBeInTheDocument();
    expect(within(menu).getByText(/No saved layouts yet/)).toBeInTheDocument();
  });

  it("restores a team preset by handing a plan to the shell", () => {
    const { onRestore } = renderSwitcher();
    const menu = openMenu();
    fireEvent.click(within(menu).getByRole("menuitem", { name: /Trading Morning/ }));

    expect(onRestore).toHaveBeenCalledTimes(1);
    const plan = onRestore.mock.calls[0][0] as LayoutRestorePlan;
    expect(plan.route).toBe("/trading/readiness");
    expect(plan.panes).toEqual(["watchlist", "live-quotes"]);
  });

  it("captures the current arrangement into a named layout", () => {
    renderSwitcher();
    openMenu();
    fireEvent.click(screen.getByRole("menuitem", { name: /Save current layout/ }));

    fireEvent.change(screen.getByLabelText("Layout name"), { target: { value: "My Close" } });
    fireEvent.click(screen.getByRole("button", { name: "Save layout" }));

    const menu = openMenu();
    expect(within(menu).getByRole("menuitem", { name: /My Close/ })).toBeInTheDocument();
  });

  it("restores a freshly saved user layout with the captured density and panes", () => {
    const { onRestore } = renderSwitcher();
    openMenu();
    fireEvent.click(screen.getByRole("menuitem", { name: /Save current layout/ }));
    fireEvent.change(screen.getByLabelText("Layout name"), { target: { value: "My Close" } });
    fireEvent.click(screen.getByRole("button", { name: "Save layout" }));

    const menu = openMenu();
    fireEvent.click(within(menu).getByRole("menuitem", { name: /My Close/ }));

    const plan = onRestore.mock.calls[0][0] as LayoutRestorePlan;
    expect(plan.route).toBe("/accounting/reconciliation?fundAccountId=fund-a");
    expect(plan.density).toBe("compact");
    expect(plan.panes).toEqual(["live-quotes"]);
    expect(plan.operatingScope).toEqual({ fundAccountId: "fund-a" });
  });

  it("deletes a saved user layout", () => {
    renderSwitcher();
    openMenu();
    fireEvent.click(screen.getByRole("menuitem", { name: /Save current layout/ }));
    fireEvent.change(screen.getByLabelText("Layout name"), { target: { value: "My Close" } });
    fireEvent.click(screen.getByRole("button", { name: "Save layout" }));

    let menu = openMenu();
    fireEvent.click(within(menu).getByRole("button", { name: "Delete layout My Close" }));

    menu = screen.getByRole("menu", { name: "Saved workspace layouts" });
    expect(within(menu).queryByRole("menuitem", { name: /My Close/ })).not.toBeInTheDocument();
    expect(within(menu).getByText(/No saved layouts yet/)).toBeInTheDocument();
  });

  it("opens into the picker, supports keyboard movement, and restores focus on Escape", async () => {
    const user = userEvent.setup();
    renderSwitcher({ showLabel: true });
    const trigger = screen.getByRole("button", { name: "Saved layouts" });
    expect(trigger).toHaveTextContent("Saved layouts");
    trigger.focus();
    await user.keyboard("{Enter}");
    const first = screen.getByRole("menuitem", { name: /Month-End Close/ });
    expect(first).toHaveFocus();
    await user.keyboard("{End}");
    expect(screen.getByRole("menuitem", { name: /Save current layout/ })).toHaveFocus();
    await user.keyboard("{Home}{ArrowDown}");
    expect(screen.getByRole("menuitem", { name: /Trading Morning/ })).toHaveFocus();
    await user.keyboard("{Escape}");
    expect(screen.queryByRole("menu")).not.toBeInTheDocument();
    expect(trigger).toHaveFocus();
  });

  it.each(["Cancel", "Escape", "Save"])("returns focus to the save-dialog invoker after %s", async (action) => {
    const user = userEvent.setup();
    renderSwitcher();
    openMenu();
    await user.click(screen.getByRole("menuitem", { name: /Save current layout/ }));
    const name = screen.getByRole("textbox", { name: "Layout name" });
    await waitFor(() => expect(name).toHaveFocus());
    await user.type(name, "Keyboard layout");
    if (action === "Escape") {
      await user.keyboard("{Escape}");
    } else {
      await user.click(screen.getByRole("button", { name: action === "Save" ? "Save layout" : "Cancel" }));
    }
    expect(screen.queryByRole("dialog", { name: "Save this layout" })).not.toBeInTheDocument();
    expect(screen.getByRole("menuitem", { name: /Save current layout/ })).toHaveFocus();
    await user.keyboard("{Escape}");
    expect(screen.getByRole("button", { name: "Saved workspace layouts" })).toHaveFocus();
  });

  it("restores focus to the layout picker trigger when choosing a layout", async () => {
    const user = userEvent.setup();
    const { onRestore } = renderSwitcher();
    openMenu();
    await user.keyboard("{Enter}");
    expect(onRestore).toHaveBeenCalledOnce();
    expect(screen.queryByRole("menu")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Saved workspace layouts" })).toHaveFocus();
  });


  it("names the expanded picker and passes accessibility checks with visible labels", async () => {
    const user = userEvent.setup();
    const { container } = renderSwitcher({ showLabel: true });
    await user.click(screen.getByRole("button", { name: "Saved layouts" }));
    expect(screen.getByRole("dialog", { name: "Saved workspace layouts" })).toBeInTheDocument();
    expect((await axe(container)).violations).toEqual([]);
  });

});
