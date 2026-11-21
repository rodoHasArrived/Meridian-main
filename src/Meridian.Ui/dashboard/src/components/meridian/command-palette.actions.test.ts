import { describe, expect, it, vi } from "vitest";
import { renderHook } from "@testing-library/react";
import {
  getCommandPaletteActionsSnapshot,
  registerCommandPaletteActions,
  useScreenPaletteActions,
  type CommandPaletteActionItem
} from "@/components/meridian/command-palette.actions";

function action(id: string): CommandPaletteActionItem {
  return {
    id,
    verbLabel: `Run ${id}`,
    description: `Test action ${id}`,
    run: async () => undefined
  };
}

describe("command palette action registry", () => {
  it("registers, replaces, and unregisters actions per source", () => {
    const first = [action("one")];
    const unregisterA = registerCommandPaletteActions("test:source-a", first);
    const unregisterB = registerCommandPaletteActions("test:source-b", [action("two")]);

    expect(getCommandPaletteActionsSnapshot().map((item) => item.id)).toEqual(["one", "two"]);

    const replacement = [action("one-replaced")];
    const unregisterA2 = registerCommandPaletteActions("test:source-a", replacement);
    expect(getCommandPaletteActionsSnapshot().map((item) => item.id)).toEqual(["one-replaced", "two"]);

    unregisterA();
    expect(getCommandPaletteActionsSnapshot().map((item) => item.id)).toEqual(
      ["one-replaced", "two"]
    );

    unregisterA2();
    unregisterB();
    expect(getCommandPaletteActionsSnapshot()).toEqual([]);
  });

  it("returns a stable snapshot between registrations", () => {
    const unregister = registerCommandPaletteActions("test:stable", [action("stable")]);
    const first = getCommandPaletteActionsSnapshot();
    const second = getCommandPaletteActionsSnapshot();
    expect(second).toBe(first);
    unregister();
  });
});

describe("useScreenPaletteActions", () => {
  it("registers while mounted, calls the latest handler, and unregisters on unmount", async () => {
    const first = vi.fn();
    const second = vi.fn();
    const spec = (run: () => void, disabled = false) => [{
      id: "hook:refresh", verbLabel: "Refresh", description: "Refresh test", disabled, run
    }];
    const { rerender, unmount } = renderHook(({ run, disabled }) => useScreenPaletteActions("test:hook", spec(run, disabled)), {
      initialProps: { run: first, disabled: false }
    });
    const registered = getCommandPaletteActionsSnapshot().find((item) => item.id === "hook:refresh");
    expect(registered?.disabled).toBe(false);

    rerender({ run: second, disabled: false });
    // A new closure alone does not re-register, but run still reaches the latest handler.
    expect(getCommandPaletteActionsSnapshot().find((item) => item.id === "hook:refresh")).toBe(registered);
    await registered?.run();
    expect(first).not.toHaveBeenCalled();
    expect(second).toHaveBeenCalledTimes(1);

    rerender({ run: second, disabled: true });
    expect(getCommandPaletteActionsSnapshot().find((item) => item.id === "hook:refresh")?.disabled).toBe(true);

    unmount();
    expect(getCommandPaletteActionsSnapshot().some((item) => item.id === "hook:refresh")).toBe(false);
  });
});
