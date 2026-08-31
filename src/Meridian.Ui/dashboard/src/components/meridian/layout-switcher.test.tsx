import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import { LayoutSwitcher } from "@/components/meridian/layout-switcher";
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
  render(
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
  return { onRestore, storage };
}

function openMenu() {
  fireEvent.click(screen.getByRole("button", { name: "Saved workspace layouts" }));
  return screen.getByRole("menu", { name: "Saved workspace layouts" });
}

describe("LayoutSwitcher", () => {
  afterEach(() => cleanup());

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
});
