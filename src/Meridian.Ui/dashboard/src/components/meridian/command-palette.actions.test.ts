import { describe, expect, it } from "vitest";
import {
  getCommandPaletteActionsSnapshot,
  registerCommandPaletteActions,
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
